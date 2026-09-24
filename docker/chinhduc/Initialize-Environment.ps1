$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '.env'
if (Test-Path -LiteralPath $target) {
    Write-Host 'Existing .env preserved. No credentials changed.'
    exit 0
}
function New-LocalSecret {
    $bytes = New-Object byte[] 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    # Hex avoids SQL/sqlcmd, shell and Compose interpolation metacharacters.
    return 'Fh9@' + [BitConverter]::ToString($bytes).Replace('-', '')
}
$names = 'MSSQL_SA_PASSWORD','EVENT_DB_PASSWORD','BOOKING_DB_PASSWORD',
    'PAYMENT_DB_PASSWORD','NOTIFICATION_DB_PASSWORD','RABBITMQ_PASSWORD','REDIS_PASSWORD'
$lines = @('# Local development secrets. Do not commit this file.')
foreach ($name in $names) { $lines += "$name=$(New-LocalSecret)" }
$lines += 'SQL_PORT=14334','RABBITMQ_PORT=5673','RABBITMQ_MANAGEMENT_PORT=15673','REDIS_PORT=6381'
[IO.File]::WriteAllText($target, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host 'Created docker/chinhduc/.env with independent random passwords.'
