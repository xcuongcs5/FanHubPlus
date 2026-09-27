$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-Databases.ps1')
$envPath = Join-Path $PSScriptRoot '.env'
$composeArgs = @('compose','--env-file',$envPath,'-f',(Join-Path $PSScriptRoot 'compose.yml'),'-f',(Join-Path $PSScriptRoot 'gps-service.yml'))
& docker @composeArgs build gps-location-service
& docker @composeArgs up -d --no-build --wait --wait-timeout 180 gps-location-service
if ($LASTEXITCODE -ne 0) { throw 'GPS Location service build/start failed.' }
Write-Host 'GPS Location Service started. Port: http://localhost:3001'