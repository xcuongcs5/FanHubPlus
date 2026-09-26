$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-Databases.ps1')

$envPath = Join-Path $PSScriptRoot '.env'
$composeArgs = @('compose','--env-file',$envPath,'-f',(Join-Path $PSScriptRoot 'compose.yml'),'-f',(Join-Path $PSScriptRoot 'search-service.yml'),'-f',(Join-Path $PSScriptRoot 'blockchain-service.yml'))

& docker @composeArgs up -d --build --wait --wait-timeout 180 blockchain-service
if ($LASTEXITCODE -ne 0) { throw 'Blockchain service build/start failed.' }
Write-Host 'Blockchain Service started. Port: http://localhost:3004 (or BLOCKCHAIN_HTTP_PORT in .env).'
