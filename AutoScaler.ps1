$ErrorActionPreference = 'Stop'
$serviceName = "fanhub-chinhduc-booking-service"

$minInstances = 1
$maxInstances = 5
$targetCpuPercent = 40.0
$currentInstances = 1

Clear-Host
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host " FanHubPlus Auto-Scaler (HPA) Dashboard" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "Monitoring CPU for: $serviceName" -ForegroundColor DarkGray
Write-Host "Target CPU: $targetCpuPercent %" -ForegroundColor DarkGray
Write-Host ""

$currentContainers = (docker ps --format "{{.Names}}" | Select-String $serviceName).Count
if ($currentContainers -gt 0) { $currentInstances = $currentContainers }

while ($true) {
    $stats = docker stats --no-stream --format "{{.Name}},{{.CPUPerc}}" | Select-String $serviceName
    
    $totalCpu = 0.0
    $count = 0
    foreach ($line in $stats) {
        $parts = $line.ToString().Split(",")
        $cpuStr = $parts[1].Replace("%", "").Trim()
        $cpu = [double]$cpuStr
        $totalCpu += $cpu
        $count++
    }

    if ($count -eq 0) {
        Write-Host "[!] No containers running. Waiting..." -ForegroundColor Yellow
        Start-Sleep -Seconds 2
        continue
    }

    $avgCpu = [math]::Round($totalCpu / $count, 2)
    $time = Get-Date -Format "HH:mm:ss"
    
    $color = "Green"
    if ($avgCpu -gt 30) { $color = "Yellow" }
    if ($avgCpu -gt 60) { $color = "Red" }

    Write-Host "[$time] CPU Load: " -NoNewline
    Write-Host "$avgCpu% " -ForegroundColor $color -NoNewline
    Write-Host "| Replicas: $currentInstances " -NoNewline

    if ($avgCpu -gt $targetCpuPercent -and $currentInstances -lt $maxInstances) {
        $currentInstances++
        Write-Host " => SPIKE DETECTED! Scaling UP to $currentInstances..." -ForegroundColor Red
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "docker"
        $psi.Arguments = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/booking-service.yml up -d --scale booking-service=$currentInstances --no-build"
        $psi.WindowStyle = "Hidden"
        [System.Diagnostics.Process]::Start($psi) | Out-Null
    }
    elseif ($avgCpu -lt ($targetCpuPercent / 2) -and $currentInstances -gt $minInstances) {
        $currentInstances--
        Write-Host " => Load stable. Scaling DOWN to $currentInstances..." -ForegroundColor Green
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "docker"
        $psi.Arguments = "compose -f docker/chinhduc/compose.yml -f docker/chinhduc/booking-service.yml up -d --scale booking-service=$currentInstances --no-build"
        $psi.WindowStyle = "Hidden"
        [System.Diagnostics.Process]::Start($psi) | Out-Null
    }
    else {
        Write-Host " => Stable" -ForegroundColor DarkGray
    }

    Start-Sleep -Seconds 2
}