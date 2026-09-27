$ErrorActionPreference = 'Stop'
Write-Host 'Khoi dong Kong API Gateway...' -ForegroundColor Cyan

$composeArgs = @('compose', '--env-file', (Join-Path $PSScriptRoot '.env'), '-f', (Join-Path $PSScriptRoot 'compose.yml'), '-f', (Join-Path $PSScriptRoot 'kong-compose.yml'))

& docker @composeArgs up -d --wait --wait-timeout 180 kong
if ($LASTEXITCODE -ne 0) { throw 'Kong API Gateway start failed.' }
Write-Host 'Kong API Gateway started on port 8000.' -ForegroundColor Green