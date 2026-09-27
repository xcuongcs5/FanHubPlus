$ErrorActionPreference = 'Stop'
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " FANHUB ADVANCED AUTO-SCALER (MULTI-SERVICE)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$services = @(
    @{ Name = 'event-service'; Prefix = 'fanhub-chinhduc-event-service'; Max = 5; Min = 1 },
    @{ Name = 'booking-service'; Prefix = 'fanhub-chinhduc-booking-service'; Max = 5; Min = 1 },
    @{ Name = 'payment-service'; Prefix = 'fanhub-chinhduc-payment-service'; Max = 5; Min = 1 }
)

$cpuThresholdUp = 35.0
$cpuThresholdDown = 15.0

$statusFile = "C:\Users\xcuon\OneDrive\Desktop\fE\techwiz-frontend\public\system-status.json"

Write-Host "Monitoring Services: Event, Booking, Payment"
Write-Host "Writing Live JSON to: $statusFile"
Write-Host "Scale UP at > $cpuThresholdUp% CPU"
Write-Host "Scale DOWN at < $cpuThresholdDown% CPU`n"

$totalRequests = 100
$activeUsers = 12


$lastScaleTime = @{
    "event-service" = (Get-Date).AddMinutes(-1)
    "booking-service" = (Get-Date).AddMinutes(-1)
    "payment-service" = (Get-Date).AddMinutes(-1)
}

while ($true) {

    $systemStatus = @{
        activeUsers = $activeUsers
        totalRequests = $totalRequests
        services = @{}
    }

    $hotspot = ""
    $maxCpu = -1

    foreach ($svc in $services) {
        $containers = docker ps --format "{{.Names}}" | Select-String $svc.Prefix
        $replicaCount = if ($containers -eq $null) { 0 } else { @($containers).Count }
        
        $svcStatus = @{
            replicas = $replicaCount
            avgCpu = 0
            status = "IDLE"
            nodes = @()
        }

        if ($replicaCount -gt 0) {
            $totalCpu = 0.0
            foreach ($container in $containers) {
                $containerStr = $container.ToString().Trim()
                $stats = docker stats $containerStr --no-stream --format "{{.CPUPerc}}"
                if ($stats) {
                    $cpuValue = $stats.ToString().Replace('%', '').Trim()
                    $cpuDouble = [double]$cpuValue
                    $totalCpu += $cpuDouble
                    $svcStatus.nodes += @{ name = $containerStr; cpu = $cpuDouble }
                }
            }
            
            $avgCpu = $totalCpu / $replicaCount
            $svcStatus.avgCpu = [math]::Round($avgCpu, 2)
            
            if ($avgCpu -gt $maxCpu) {
                $maxCpu = $avgCpu
                if ($avgCpu -gt 30) { $hotspot = $svc.Name }
            }

            Write-Host "[$($svc.Name)] Replicas: $replicaCount | Avg CPU: $($svcStatus.avgCpu)%"

            
            $timeSinceLastScale = ((Get-Date) - $lastScaleTime[$svc.Name]).TotalSeconds
            
            if ($timeSinceLastScale -lt 8) {
                $svcStatus.status = "SCALING_IN_PROGRESS"
            } else {
                if ($avgCpu -gt $cpuThresholdUp -and $replicaCount -lt $svc.Max) {
                    if ($avgCpu -gt 85.0 -and $replicaCount + 2 -lt $svc.Max) { $newCount = $replicaCount + 3 } elseif ($avgCpu -gt 60.0 -and $replicaCount + 1 -lt $svc.Max) { $newCount = $replicaCount + 2 } else { $newCount = $replicaCount + 1 }
                    $svcStatus.status = "SCALING_UP"
                    $lastScaleTime[$svc.Name] = Get-Date
                    Write-Host ">>> ALERT: High CPU detected on $($svc.Name)! Scaling UP to $newCount replicas..." -ForegroundColor Red
                    $args = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$($svc.Name).yml up -d --scale $($svc.Name)=$newCount --no-recreate"
                    Start-Process -FilePath "docker" -ArgumentList $args -NoNewWindow
                }
                elseif ($avgCpu -lt $cpuThresholdDown -and $replicaCount -gt $svc.Min) {
                    $newCount = $replicaCount - 1
                    $svcStatus.status = "SCALING_DOWN"
                    $lastScaleTime[$svc.Name] = Get-Date
                    Write-Host "<<< INFO: Low CPU on $($svc.Name). Scaling DOWN to $newCount replicas..." -ForegroundColor Green
                    $args = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$($svc.Name).yml up -d --scale $($svc.Name)=$newCount --no-recreate"
                    Start-Process -FilePath "docker" -ArgumentList $args -NoNewWindow
                }
                elseif ($avgCpu -gt 50) {
                    $svcStatus.status = "HIGH_LOAD"
                }
            }
 elseif ($avgCpu -gt 60.0 -and $replicaCount + 1 -lt $svc.Max) { $newCount = $replicaCount + 2 } else { $newCount = $replicaCount + 1 }
                $svcStatus.status = "SCALING_UP"
                Write-Host ">>> ALERT: High CPU detected on $($svc.Name)! Scaling UP to $newCount replicas..." -ForegroundColor Red
                $args = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$($svc.Name).yml up -d --scale $($svc.Name)=$newCount --no-recreate"
                Start-Process -FilePath "docker" -ArgumentList $args -NoNewWindow -Wait
            }
            elseif ($avgCpu -lt $cpuThresholdDown -and $replicaCount -gt $svc.Min) {
                $newCount = $replicaCount - 1
                $svcStatus.status = "SCALING_DOWN"
                Write-Host "<<< INFO: Low CPU on $($svc.Name). Scaling DOWN to $newCount replicas..." -ForegroundColor Green
                $args = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$($svc.Name).yml up -d --scale $($svc.Name)=$newCount --no-recreate"
                Start-Process -FilePath "docker" -ArgumentList $args -NoNewWindow -Wait
            }
            elseif ($avgCpu -gt 50) {
                $svcStatus.status = "HIGH_LOAD"
            }
        }
        
        $systemStatus.services[$svc.Name] = $svcStatus
    }
    
    $systemStatus.hotspot = $hotspot
    $systemStatus.timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    
    # Fake incrementing users/requests to look lively if no hotspot, or massive spike if hotspot
    if ($hotspot -ne "") {
        $activeUsers += Get-Random -Minimum 10 -Maximum 50
        $totalRequests += Get-Random -Minimum 50 -Maximum 200
    } else {
        $activeUsers = [math]::Max(5, $activeUsers + (Get-Random -Minimum -5 -Maximum 5))
        $totalRequests += Get-Random -Minimum 1 -Maximum 5
    }
    $systemStatus.activeUsers = $activeUsers
    $systemStatus.totalRequests = $totalRequests

    $systemStatus | ConvertTo-Json -Depth 5 | Out-File -FilePath $statusFile -Encoding utf8
    Start-Sleep -Seconds 1
    Write-Host "----------------------------------" -ForegroundColor DarkGray
}