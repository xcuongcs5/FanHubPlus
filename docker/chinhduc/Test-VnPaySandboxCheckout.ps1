param([Parameter(Mandatory=$true)][string]$BearerToken, [string]$ApiUrl='http://localhost:5011')
$ErrorActionPreference = 'Stop'
# This creates an UNPAID 10,000 VND deposit intent. It never enters card data or fabricates IPN.
$secretPath = Join-Path $PSScriptRoot '../secrets/payment-providers.json'
$settings = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
if (-not $settings.VnPay.Sandbox) { throw 'This smoke test is sandbox-only.' }
try {
    $body = @{amount=10000; provider='VNPay'; idempotency_key=('sandbox-smoke-' + [Guid]::NewGuid().ToString('N'))} | ConvertTo-Json
    $intent = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/v1/wallets/deposit" -Headers @{Authorization="Bearer $BearerToken"} -ContentType 'application/json' -Body $body
    $checkoutUri = [Uri]$intent.checkout_url
    if ($checkoutUri.Scheme -ne 'https' -or $checkoutUri.Host -ne 'sandbox.vnpayment.vn') { throw 'Unexpected checkout host.' }
    $page = Invoke-WebRequest -Uri $checkoutUri -MaximumRedirection 8
    $content = if ($page.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($page.Content) } else { [string]$page.Content }
    $title = if ($content -match '(?is)<title>(.*?)</title>') { [Net.WebUtility]::HtmlDecode($Matches[1]).Trim() } else { 'No title' }
    if ($page.BaseResponse.RequestMessage.RequestUri.AbsolutePath -ne '/paymentv2/Transaction/PaymentMethod.html') {
        throw 'Sandbox did not display the payment-method selection page.'
    }
    $snapshot = @{transaction_id=$intent.transaction_id; checkout_url=$intent.checkout_url; expires_at=$intent.expires_at; http_status=[int]$page.StatusCode; page_title=$title}
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot '../secrets/payment-sandbox-smoke.json'), ($snapshot | ConvertTo-Json))
    Write-Host "Sandbox checkout HTTP $($page.StatusCode). Page title: $title"
    Write-Host 'Unpaid intent details saved under ignored docker/secrets/payment-sandbox-smoke.json. This does not verify payment/IPN/refund.'
} catch {
    # Do not dump exception request URLs, bearer tokens or signed checkout URLs into logs.
    throw 'Sandbox smoke test failed. Check API/provider connectivity and merchant settings; no secret details were logged.'
}
