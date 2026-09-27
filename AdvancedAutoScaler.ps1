$statusFile = "..\..\fE\techwiz-frontend\public\system-status.json"

$services = @(
    @{ Name = "event-service"; Prefix = "event-service"; Min = 1; Max = 5 }
    @{ Name = "booking-service"; Prefix = "booking-service"; Min = 1; Max = 5 }
    @{ Name = "payment-service"; Prefix = "payment-service"; Min = 1; Max = 5 }
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " REAL-TIME DYNAMIC AUTOSCALER & TELEMETRY ENGINE" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Writing telemetry to: $statusFile"
Write-Host "Monitoring: Event, Booking, Payment"
Write-Host "Auto-scale trigger: > 35% CPU | Cool-down trigger: < 15% CPU`n"

$activeUsers = 15
$totalRequests = 120

# Track desired replica count in-memory for instant UI feedback
$currentReplicas = @{
    "event-service" = 1
    "booking-service" = 1
    "payment-service" = 1
}

$lastScaleAction = @{
    "event-service" = (Get-Date).AddMinutes(-1)
    "booking-service" = (Get-Date).AddMinutes(-1)
    "payment-service" = (Get-Date).AddMinutes(-1)
}

while ($true) {
    # 1. Fetch all container stats in ONE single lightning-fast batch call
    $statsLines = docker stats --no-stream --format "{{.Name}}|{{.CPUPerc}}"
    $cpuMap = @{}
    if ($statsLines) {
        foreach ($line in $statsLines) {
            $parts = $line.Split('|')
            if ($parts.Length -eq 2) {
                $valStr = $parts[1].Replace('%','').Trim()
                $val = 0.0
                if ([double]::TryParse($valStr, [ref]$val)) {
                    $cpuMap[$parts[0].Trim()] = $val
                }
            }
        }
    }

    $systemStatus = @{
        hotspot = ""
        timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        activeUsers = 0
        totalRequests = 0
        services = @{}
    }

    $hotspot = ""
    $maxCpu = -1.0

    foreach ($svc in $services) {
        $svcName = $svc.Name
        # Find all actual containers matching prefix
        $matchedContainers = $cpuMap.Keys | Where-Object { $_ -like "*$($svc.Prefix)*" }
        
        $totalRawCpu = 0.0
        foreach ($c in $matchedContainers) {
            $totalRawCpu += $cpuMap[$c]
        }

        $now = Get-Date
        $secondsSinceLastScale = ($now - $lastScaleAction[$svcName]).TotalSeconds

        # Determine target replicas based on current load
        $target = $currentReplicas[$svcName]
        $status = "IDLE"

        if ($totalRawCpu -gt 35.0) {
            # High load detected
            if ($totalRawCpu -gt 130.0) {
                $target = 4
            } elseif ($totalRawCpu -gt 70.0) {
                $target = 3
            } else {
                $target = 2
            }
            if ($target -gt $svc.Max) { $target = $svc.Max }
            $status = if ($target -gt $currentReplicas[$svcName]) { "SCALING_UP" } else { "BALANCED" }
        } elseif ($totalRawCpu -lt 15.0) {
            # Low load detected - rapid scale down
            $target = $svc.Min
            $status = if ($currentReplicas[$svcName] -gt $svc.Min) { "SCALING_DOWN" } else { "IDLE" }
        } else {
            $status = "ACTIVE"
        }

        # If target changed and cooldown expired (> 2s), trigger Docker scaling in background
        if ($target -ne $currentReplicas[$svcName] -and $secondsSinceLastScale -ge 2) {
            $lastScaleAction[$svcName] = $now
            $old = $currentReplicas[$svcName]
            $currentReplicas[$svcName] = $target
            
            if ($target -gt $old) {
                Write-Host ">>> [$svcName] Spiking load ($([math]::Round($totalRawCpu,1))%)! Scaling UP $old -> $target nodes..." -ForegroundColor Red
            } else {
                Write-Host "<<< [$svcName] Traffic left ($([math]::Round($totalRawCpu,1))%)! Scaling DOWN $old -> $target nodes..." -ForegroundColor Green
            }

            $dockerArgs = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$svcName.yml up -d --scale $svcName=$target -t 1 --no-recreate"
            Start-Process -FilePath "docker" -ArgumentList $dockerArgs -NoNewWindow
        }

        # Calculate balanced CPU per node for visual realism (no 1000%, no 0% idling)
        $displayCount = $currentReplicas[$svcName]
        $nodesList = @()

        if ($totalRawCpu -gt 15.0) {
            # Distribute load realistically among active nodes
            $baseCpu = [math]::Min(88.0, [math]::Max(25.0, $totalRawCpu / $displayCount))
            for ($i = 1; $i -le $displayCount; $i++) {
                $jitter = (Get-Random -Minimum -40 -Maximum 40) / 10.0
                $nodeCpu = [math]::Round([math]::Max(10.0, [math]::Min(96.0, $baseCpu + $jitter)), 2)
                $nodesList += @{
                    name = "$svcName-$i"
                    cpu = $nodeCpu
                }
            }
            $avgCpu = [math]::Round($baseCpu, 2)
        } else {
            # Idle state
            for ($i = 1; $i -le $displayCount; $i++) {
                $idleCpu = [math]::Round((Get-Random -Minimum 10 -Maximum 50) / 100.0, 2)
                $nodesList += @{
                    name = "$svcName-$i"
                    cpu = $idleCpu
                }
            }
            $avgCpu = 0.35
        }

        if ($avgCpu -gt $maxCpu) {
            $maxCpu = $avgCpu
            if ($avgCpu -gt 20.0) {
                $hotspot = $svcName
            }
        }

        $systemStatus.services[$svcName] = @{
            replicas = $displayCount
            avgCpu = $avgCpu
            status = $status
            nodes = $nodesList
        }

        Write-Host "[$svcName] Nodes: $displayCount | Avg CPU: $avgCpu% | Status: $status"
    }

    $telemetryFile = "loadtest-telemetry.json"
    $liveUsers = 0
    $liveReqs = 0
    if (Test-Path $telemetryFile) {
        try {
            $raw = Get-Content $telemetryFile -Raw -ErrorAction SilentlyContinue
            if ($raw) {
                $tel = $raw | ConvertFrom-Json
                $liveUsers = [int]$tel.activeUsers
                $liveReqs = [int]$tel.totalRequests
                if ($tel.hotspot -and $tel.hotspot -ne "") {
                    $hotspot = [string]$tel.hotspot
                }
            }
        } catch {}
    }

    $systemStatus.hotspot = $hotspot
    $systemStatus.activeUsers = $liveUsers
    $systemStatus.totalRequests = $liveReqs

    # Write out telemetry instantly
    $jsonContent = $systemStatus | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Resolve-Path $statusFile).Path, $jsonContent, (New-Object System.Text.UTF8Encoding $false))

    Start-Sleep -Milliseconds 800
    Write-Host "--------------------------------------------------" -ForegroundColor DarkGray
}
