$ErrorActionPreference = 'Stop'
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " FANHUB ADVANCED AUTO-SCALER (MULTI-SERVICE)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$services = @(
    @{ Name = 'event-service'; Prefix = 'fanhub-chinhduc-event-service'; Max = 5; Min = 1 },
    @{ Name = 'booking-service'; Prefix = 'fanhub-chinhduc-booking-service'; Max = 5; Min = 1 },
    @{ Name = 'payment-service'; Prefix = 'fanhub-chinhduc-payment-service'; Max = 5; Min = 1 }
)

$cpuThresholdUp = 80.0
$cpuThresholdDown = 20.0

Write-Host "Monitoring Services: Event, Booking, Payment"
Write-Host "Scale UP at > $cpuThresholdUp% CPU"
Write-Host "Scale DOWN at < $cpuThresholdDown% CPU
"

while ($true) {
    foreach ($svc in $services) {
        $containers = docker ps --format "{{.Names}}" | Select-String $svc.Prefix
        $replicaCount = if ($containers -eq $null) { 0 } else { @($containers).Count }
        
        if ($replicaCount -eq 0) { continue }

        $totalCpu = 0.0
        foreach ($container in $containers) {
            $stats = docker stats $container --no-stream --format "{{.CPUPerc}}"
            if ($stats) {
                $cpuValue = $stats.ToString().Replace('%', '').Trim()
                $totalCpu += [double]$cpuValue
            }
        }
        
        $avgCpu = $totalCpu / $replicaCount
        Write-Host "[$($svc.Name)] Replicas: $replicaCount | Avg CPU: $([math]::Round($avgCpu, 2))%"

        if ($avgCpu -gt $cpuThresholdUp -and $replicaCount -lt $svc.Max) {
            $newCount = $replicaCount + 1
            Write-Host ">>> ALERT: High CPU detected on $($svc.Name)! Scaling UP to $newCount replicas..." -ForegroundColor Red
            $args = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$($svc.Name).yml up -d --scale $($svc.Name)=$newCount --no-recreate"
            Start-Process -FilePath "docker" -ArgumentList $args -NoNewWindow -Wait
        }
        elseif ($avgCpu -lt $cpuThresholdDown -and $replicaCount -gt $svc.Min) {
            $newCount = $replicaCount - 1
            Write-Host "<<< INFO: Low CPU on $($svc.Name). Scaling DOWN to $newCount replicas..." -ForegroundColor Green
            $args = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/$($svc.Name).yml up -d --scale $($svc.Name)=$newCount --no-recreate"
            Start-Process -FilePath "docker" -ArgumentList $args -NoNewWindow -Wait
        }
    }
    Start-Sleep -Seconds 3
    Write-Host "----------------------------------" -ForegroundColor DarkGray
}