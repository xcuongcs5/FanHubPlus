param([switch]$WithFirebase)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-Databases.ps1')
$envPath = Join-Path $PSScriptRoot '.env'
if (-not (Select-String -LiteralPath $envPath -Pattern '^JWT_SECRET=' -Quiet)) {
    # Reuse this repository's Identity development key without printing it.
    $identityPath = Join-Path $PSScriptRoot '../../services/dotnet/FanHub.IdentityService/appsettings.json'
    $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
    $jwtSecret = $identity.Jwt.Secret
    if ([string]::IsNullOrWhiteSpace($jwtSecret) -or $jwtSecret.Length -lt 32) {
        throw 'Set JWT_SECRET in docker/chinhduc/.env to the same signing key used by Identity.'
    }
    if ($jwtSecret -match "['\r\n]") { throw 'Set a dotenv-compatible JWT_SECRET in .env manually.' }
    [IO.File]::AppendAllText($envPath, "JWT_SECRET='$jwtSecret'`n", [Text.UTF8Encoding]::new($false))
    Write-Host 'Configured Notification JWT key from the existing Identity development settings.'
}
$composeArgs = @('compose','--env-file',$envPath,'-f',(Join-Path $PSScriptRoot 'compose.yml'),'-f',(Join-Path $PSScriptRoot 'notification-service.yml'))
if ($WithFirebase) { $composeArgs += @('-f',(Join-Path $PSScriptRoot 'notification-firebase.yml')) }
& (Join-Path $PSScriptRoot 'Build-NotificationImage.ps1')
& docker @composeArgs up -d --no-build --wait --wait-timeout 180 notification-service
if ($LASTEXITCODE -ne 0) { throw 'Notification service build/start failed.' }
Write-Host 'Notification API started. Swagger: http://localhost:5012/swagger (or NOTIFICATION_HTTP_PORT in .env).'
