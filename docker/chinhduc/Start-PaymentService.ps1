$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot '../secrets/payment-providers.json') -PathType Leaf)) {
    throw 'Create docker/secrets/payment-providers.json from payment-providers.example.json and fill sandbox credentials first.'
}
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
    Write-Host 'Configured Payment JWT key from the existing Identity development settings.'
}
$composeArgs = @('compose','--env-file',$envPath,'-f',(Join-Path $PSScriptRoot 'compose.yml'),'-f',(Join-Path $PSScriptRoot 'payment-service.yml'))
& (Join-Path $PSScriptRoot 'Build-PaymentImage.ps1')
& docker @composeArgs up -d --no-build --wait --wait-timeout 180 payment-service
if ($LASTEXITCODE -ne 0) { throw 'Payment service build/start failed.' }
Write-Host 'Payment API started. Swagger: http://localhost:5011/swagger (or PAYMENT_HTTP_PORT in .env).'
