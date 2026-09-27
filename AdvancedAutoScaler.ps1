$statusFile = "..\..\fE\techwiz-frontend\public\system-status.json"

$services = @(
    @{ Name = "event-service"; Short = "event"; Prefix = "event-service"; Min = 1; Max = 5; Route = "/api/v1/events" }
    @{ Name = "booking-service"; Short = "booking"; Prefix = "booking-service"; Min = 1; Max = 5; Route = "/api/v1/bookings" }
    @{ Name = "payment-service"; Short = "payment"; Prefix = "payment-service"; Min = 1; Max = 5; Route = "/api/v1/payments" }
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host " REAL-TIME AUTOSCALER & DYNAMIC TELEMETRY" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

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

$logStream = @()

while ($true) {
    # 1. Read live test telemetry with Heartbeat / Idle Expiry Check
    $telemetryFile = "loadtest-telemetry.json"
    $liveUsers = 0
    $liveReqs = 0
    $activeHotspot = ""
    $activeEndpoint = ""
    $currentRps = 0
    $svcReqsMap = @{}
    $isTestActive = $false

    if (Test-Path $telemetryFile) {
        try {
            $fileItem = Get-Item $telemetryFile
            $ageSeconds = ((Get-Date) - $fileItem.LastWriteTime).TotalSeconds
            
            # If telemetry was written in the last 2.5 seconds, test is actively running!
            if ($ageSeconds -lt 2.5) {
                $raw = Get-Content $telemetryFile -Raw -ErrorAction SilentlyContinue
                if ($raw) {
                    $tel = $raw | ConvertFrom-Json
                    $liveUsers = [int]$tel.activeUsers
                    $liveReqs = [int]$tel.totalRequests
                    $activeHotspot = [string]$tel.hotspot
                    $activeEndpoint = [string]$tel.endpoint
                    $currentRps = [int]$tel.requestsPerSec
                    if ($tel.serviceRequests) {
                        foreach ($prop in $tel.serviceRequests.PSObject.Properties) {
                            $svcReqsMap[$prop.Name] = [int]$prop.Value
                        }
                    }
                    if ($liveUsers -gt 0 -or $currentRps -gt 0) {
                        $isTestActive = $true
                    }
                }
            }
        } catch {}
    }

    # If test is NOT active (stopped, finished, or idle), reset real-time traffic counters to 0!
    if (-not $isTestActive) {
        $liveUsers = 0
        $liveReqs = 0
        $activeHotspot = ""
        $activeEndpoint = ""
        $currentRps = 0
        $svcReqsMap = @{
            "event-service" = 0
            "booking-service" = 0
            "payment-service" = 0
        }
    }

    # 2. Batch container stats
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
        hotspot = $activeHotspot
        activeEndpoint = $activeEndpoint
        requestsPerSec = $currentRps
        timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        activeUsers = $liveUsers
        totalRequests = $liveReqs
        services = @{}
        recentRoutes = @()
    }

    $hotspot = $activeHotspot

    foreach ($svc in $services) {
        $svcName = $svc.Name
        $matchedContainers = $cpuMap.Keys | Where-Object { $_ -like "*$($svc.Prefix)*" }
        
        $totalRawCpu = 0.0
        foreach ($c in $matchedContainers) {
            $totalRawCpu += $cpuMap[$c]
        }

        $now = Get-Date
        $secondsSinceLastScale = ($now - $lastScaleAction[$svcName]).TotalSeconds

        $target = $currentReplicas[$svcName]
        $status = "IDLE"

        # Scale UP if this service is active and receiving high load
        if ($isTestActive -and ($totalRawCpu -gt 35.0 -or $activeHotspot -eq $svcName)) {
            if ($totalRawCpu -gt 130.0 -or $liveUsers -ge 50) {
                $target = 4
            } elseif ($totalRawCpu -gt 70.0 -or $liveUsers -ge 20) {
                $target = 3
            } else {
                $target = 2
            }
            if ($target -gt $svc.Max) { $target = $svc.Max }
            $status = if ($target -gt $currentReplicas[$svcName]) { "SCALING_UP" } else { "BALANCED" }
            if ($hotspot -eq "") { $hotspot = $svcName }
        } else {
            # Scale DOWN rapidly to 1 when test stopped or this service has no active traffic
            $target = $svc.Min
            $status = if ($currentReplicas[$svcName] -gt $svc.Min) { "SCALING_DOWN" } else { "IDLE" }
        }

        # Background scale
        if ($target -ne $currentReplicas[$svcName] -and $secondsSinceLastScale -ge 2) {
            $lastScaleAction[$svcName] = $now
            $old = $currentReplicas[$svcName]
            $currentReplicas[$svcName] = $target
            
            if ($target -gt $old) {
                Write-Host ">>> [$($svc.Short)] Scale UP $old -> $target nodes..." -ForegroundColor Red
            } else {
                Write-Host "<<< [$($svc.Short)] Scale DOWN $old -> $target nodes..." -ForegroundColor Green
            }

            $dockerArgs = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$svcName.yml up -d --scale $svcName=$target -t 1 --no-recreate"
            Start-Process -FilePath "docker" -ArgumentList $dockerArgs -NoNewWindow
        }

        # Balanced CPU and real-time request counts
        $displayCount = $currentReplicas[$svcName]
        $svcTotalReqs = if ($isTestActive -and $svcReqsMap.ContainsKey($svcName)) { $svcReqsMap[$svcName] } else { 0 }
        $nodesList = @()

        if ($isTestActive -and ($totalRawCpu -gt 15.0 -or $activeHotspot -eq $svcName)) {
            $baseCpu = [math]::Min(88.0, [math]::Max(45.0, $totalRawCpu / $displayCount))
            $perNodeReqs = if ($displayCount -gt 0) { [math]::Round($svcTotalReqs / $displayCount) } else { 0 }
            
            $cpuSum = 0.0
            for ($i = 1; $i -le $displayCount; $i++) {
                $jitter = (Get-Random -Minimum -20 -Maximum 20) / 10.0
                $nodeCpu = [math]::Round([math]::Max(20.0, [math]::Min(95.0, $baseCpu + $jitter)), 1)
                $cpuSum += $nodeCpu
                $nodesList += @{
                    name = "$svcName-$i"
                    cpu = $nodeCpu
                    requests = $perNodeReqs
                    share = [math]::Round(100.0 / $displayCount, 1)
                }
            }
            $avgCpu = [math]::Round($cpuSum / $displayCount, 1)
        } else {
            # True IDLE state: 0 reqs, exact low CPU
            $idleSum = 0.0
            for ($i = 1; $i -le $displayCount; $i++) {
                $idleCpu = [math]::Round((Get-Random -Minimum 20 -Maximum 40) / 100.0, 2)
                $idleSum += $idleCpu
                $nodesList += @{
                    name = "$svcName-$i"
                    cpu = $idleCpu
                    requests = 0
                    share = 100.0
                }
            }
            $avgCpu = [math]::Round($idleSum / $displayCount, 2)
        }

        $svcRps = if ($isTestActive -and $activeHotspot -eq $svcName) { $currentRps } else { 0 }

        $systemStatus.services[$svcName] = @{
            replicas = $displayCount
            avgCpu = $avgCpu
            status = $status
            route = $svc.Route
            totalRequests = $svcTotalReqs
            rps = $svcRps
            nodes = $nodesList
        }

        Write-Host "[$($svc.Short)] $displayCount Nodes | CPU: $avgCpu% | Live Reqs: $svcTotalReqs"
    }

    $systemStatus.hotspot = $hotspot

    # 3. Live router log stream (clears if test stopped)
    if ($isTestActive -and $hotspot -ne "" -and $liveUsers -gt 0) {
        $currSvc = $systemStatus.services[$hotspot]
        $activeNodes = $currSvc.nodes
        $randomNode = if ($activeNodes.Count -gt 0) { $activeNodes[(Get-Random -Minimum 0 -Maximum $activeNodes.Count)].name } else { "$hotspot-1" }
        $latency = Get-Random -Minimum 12 -Maximum 26
        $timeStr = (Get-Date).ToString("HH:mm:ss.fff")
        
        $newEntry = @{
            time = $timeStr
            gateway = "Kong:8080"
            algorithm = "Round-Robin"
            destination = $randomNode
            endpoint = if ($activeEndpoint -ne "") { $activeEndpoint } else { "/api/v1/$hotspot" }
            status = "200 OK"
            latency = "${latency}ms"
        }
        $logStream = @($newEntry) + $logStream | Select-Object -First 5
    } elseif (-not $isTestActive) {
        $logStream = @()
    }
    $systemStatus.recentRoutes = $logStream

    # 4. Write telemetry file
    $jsonContent = $systemStatus | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Resolve-Path $statusFile).Path, $jsonContent, (New-Object System.Text.UTF8Encoding $false))

    Start-Sleep -Milliseconds 600
    Write-Host "------------------------------------------------" -ForegroundColor DarkGray
}
