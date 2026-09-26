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
    Write-Host 'Configured Booking JWT key from the existing Identity development settings.'
}
$composeArgs = @('compose','--env-file',$envPath,'-f',(Join-Path $PSScriptRoot 'compose.yml'),'-f',(Join-Path $PSScriptRoot 'booking-service.yml'))
& (Join-Path $PSScriptRoot 'Build-BookingImage.ps1')
& docker @composeArgs up -d --no-build --wait --wait-timeout 180 booking-service
if ($LASTEXITCODE -ne 0) { throw 'Booking service build/start failed.' }
Write-Host 'Booking API started. Swagger: http://localhost:5010/swagger (or BOOKING_HTTP_PORT in .env).'
