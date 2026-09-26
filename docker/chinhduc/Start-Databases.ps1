[CmdletBinding()]
param([switch]$Verify)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Initialize-Environment.ps1')
$composeArgs = @('compose', '--env-file', (Join-Path $PSScriptRoot '.env'), '-f', (Join-Path $PSScriptRoot 'compose.yml'))
function Invoke-Compose([string[]]$CommandArgs) {
    & docker @composeArgs @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed (exit $LASTEXITCODE)." }
}
Invoke-Compose @('config','--quiet')
Invoke-Compose @('up','-d','--wait','--wait-timeout','300','sqlserver','rabbitmq','redis')
Invoke-Compose @('run','--rm','db-migrate')
if ($Verify) {
    Invoke-Compose @('run','--rm','--entrypoint','/bin/bash','db-migrate','/scripts/verify.sh')
    Invoke-Compose @('run','--rm','--entrypoint','/bin/bash','db-migrate','/scripts/verify-migrations.sh')
}
Write-Host 'Ready: SQL Server localhost,14334 (or SQL_PORT in .env). Four independent service databases.'
