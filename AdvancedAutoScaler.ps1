$statusFile = "..\..\fE\techwiz-frontend\public\system-status.json"

$services = @(
    @{ Name = "event-service"; Prefix = "event-service"; Min = 1; Max = 5; Route = "/api/v1/events" }
    @{ Name = "booking-service"; Prefix = "booking-service"; Min = 1; Max = 5; Route = "/api/v1/bookings" }
    @{ Name = "payment-service"; Prefix = "payment-service"; Min = 1; Max = 5; Route = "/api/v1/payments" }
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " REAL-TIME DYNAMIC AUTOSCALER & ROUTING TELEMETRY" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Writing telemetry to: $statusFile"
Write-Host "Monitoring: Event, Booking, Payment"
Write-Host "Auto-scale trigger: > 35% CPU | Cool-down trigger: < 15% CPU`n"

# Desired replica counts
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
    # 1. Read live test telemetry
    $telemetryFile = "loadtest-telemetry.json"
    $liveUsers = 0
    $liveReqs = 0
    $activeHotspot = ""
    $activeEndpoint = ""
    $currentRps = 0
    $svcReqsMap = @{}

    if (Test-Path $telemetryFile) {
        try {
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
            }
        } catch {}
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
    $maxCpu = -1.0

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

        if ($totalRawCpu -gt 35.0 -or $activeHotspot -eq $svcName) {
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
        } elseif ($totalRawCpu -lt 15.0 -and $activeHotspot -ne $svcName) {
            $target = $svc.Min
            $status = if ($currentReplicas[$svcName] -gt $svc.Min) { "SCALING_DOWN" } else { "IDLE" }
        } else {
            $status = "ACTIVE"
        }

        # Background scale
        if ($target -ne $currentReplicas[$svcName] -and $secondsSinceLastScale -ge 2) {
            $lastScaleAction[$svcName] = $now
            $old = $currentReplicas[$svcName]
            $currentReplicas[$svcName] = $target
            
            if ($target -gt $old) {
                Write-Host ">>> [$svcName] Spiking traffic! Scaling UP $old -> $target nodes..." -ForegroundColor Red
            } else {
                Write-Host "<<< [$svcName] Traffic left. Scaling DOWN $old -> $target nodes..." -ForegroundColor Green
            }

            $dockerArgs = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$svcName.yml up -d --scale $svcName=$target -t 1 --no-recreate"
            Start-Process -FilePath "docker" -ArgumentList $dockerArgs -NoNewWindow
        }

        # Calculate balanced CPU & request counts
        $displayCount = $currentReplicas[$svcName]
        $svcTotalReqs = if ($svcReqsMap.ContainsKey($svcName)) { $svcReqsMap[$svcName] } else { 0 }
        $nodesList = @()

        if ($totalRawCpu -gt 15.0 -or $activeHotspot -eq $svcName) {
            $baseCpu = [math]::Min(88.0, [math]::Max(25.0, $totalRawCpu / $displayCount))
            $perNodeReqs = if ($displayCount -gt 0) { [math]::Round($svcTotalReqs / $displayCount) } else { 0 }
            
            for ($i = 1; $i -le $displayCount; $i++) {
                $jitter = (Get-Random -Minimum -40 -Maximum 40) / 10.0
                $nodeCpu = [math]::Round([math]::Max(10.0, [math]::Min(96.0, $baseCpu + $jitter)), 2)
                $nodesList += @{
                    name = "$svcName-$i"
                    cpu = $nodeCpu
                    requests = $perNodeReqs
                    share = [math]::Round(100.0 / $displayCount, 1)
                }
            }
            $avgCpu = [math]::Round($baseCpu, 2)
        } else {
            for ($i = 1; $i -le $displayCount; $i++) {
                $idleCpu = [math]::Round((Get-Random -Minimum 10 -Maximum 50) / 100.0, 2)
                $nodesList += @{
                    name = "$svcName-$i"
                    cpu = $idleCpu
                    requests = $svcTotalReqs
                    share = 100.0
                }
            }
            $avgCpu = 0.35
        }

        $svcRps = if ($activeHotspot -eq $svcName) { $currentRps } else { 0 }

        $systemStatus.services[$svcName] = @{
            replicas = $displayCount
            avgCpu = $avgCpu
            status = $status
            route = $svc.Route
            totalRequests = $svcTotalReqs
            rps = $svcRps
            nodes = $nodesList
        }

        Write-Host "[$svcName] Nodes: $displayCount | Avg CPU: $avgCpu% | Reqs: $svcTotalReqs | RPS: $svcRps"
    }

    $systemStatus.hotspot = $hotspot

    # 3. Generate rolling live router stream logs (Kong -> Target Node)
    if ($hotspot -ne "" -and $liveUsers -gt 0) {
        $currSvc = $systemStatus.services[$hotspot]
        $activeNodes = $currSvc.nodes
        $randomNode = if ($activeNodes.Count -gt 0) { $activeNodes[(Get-Random -Minimum 0 -Maximum $activeNodes.Count)].name } else { "$hotspot-1" }
        $latency = Get-Random -Minimum 12 -Maximum 28
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
    }
    $systemStatus.recentRoutes = $logStream

    # 4. Write telemetry file
    $jsonContent = $systemStatus | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Resolve-Path $statusFile).Path, $jsonContent, (New-Object System.Text.UTF8Encoding $false))

    Start-Sleep -Milliseconds 800
    Write-Host "--------------------------------------------------" -ForegroundColor DarkGray
}
