$ErrorActionPreference = 'Stop'
$compose = @('compose', '--env-file', (Join-Path $PSScriptRoot '.env'), '-f', (Join-Path $PSScriptRoot 'compose.yml'),
    '-f', (Join-Path $PSScriptRoot 'payment-service.yml'), '-f', (Join-Path $PSScriptRoot 'payment-tunnel.yml'))
$secretPath = Join-Path $PSScriptRoot '../secrets/payment-providers.json'
$settings = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
if (-not $settings.VnPay.Sandbox) { throw 'Quick Tunnel is for sandbox only.' }
& docker @compose up -d --no-build vnpay-tunnel
if ($LASTEXITCODE -ne 0) { throw 'Tunnel startup failed.' }
$tunnelId = (& docker @compose ps -q vnpay-tunnel).Trim()
$baseUrl = $null
for ($attempt=0; $attempt -lt 20; $attempt++) {
    $logs = (& docker logs $tunnelId 2>&1) -join "`n"
    $urls = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($urls.Count -gt 0) { $baseUrl = $urls[$urls.Count-1].Value; break }
    Start-Sleep -Seconds 2
}
if (-not $baseUrl) { throw 'Tunnel has not announced a public hostname; inspect container logs.' }
$returnUrl = "$baseUrl/api/v1/payments/return/vnpay"
if ($settings.VnPay.ReturnUrl -ne $returnUrl) {
    $settings.VnPay.ReturnUrl = $returnUrl
    [IO.File]::WriteAllText($secretPath, ($settings | ConvertTo-Json -Depth 10))
    & docker @compose restart payment-service
    if ($LASTEXITCODE -ne 0) { throw 'Payment restart failed.' }
}
$state = @{ active=$true; base_url=$baseUrl; ipn_url="$baseUrl/api/v1/payments/webhook/vnpay"; return_url=$returnUrl; generated_at=[DateTime]::UtcNow.ToString('o') }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot '../secrets/payment-tunnel.json'), ($state | ConvertTo-Json))
Write-Host "IPN URL: $($state.ipn_url)"
Write-Host "Return URL: $returnUrl"
Write-Host 'Register the IPN URL with VNPAY. Keep Docker running; a tunnel restart can change this hostname.'
