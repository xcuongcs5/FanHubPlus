$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-Databases.ps1')

$envPath = Join-Path $PSScriptRoot '.env'
$composeArgs = @('compose','--env-file',$envPath,'-f',(Join-Path $PSScriptRoot 'compose.yml'),'-f',(Join-Path $PSScriptRoot 'search-service.yml'))

& docker @composeArgs up -d --build --wait --wait-timeout 180 search-service
if ($LASTEXITCODE -ne 0) { throw 'Search service build/start failed.' }
Write-Host 'Search Service started. Port: http://localhost:3003 (or SEARCH_HTTP_PORT in .env).'
