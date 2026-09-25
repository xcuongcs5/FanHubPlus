$ErrorActionPreference = 'Stop'
$values = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $PSScriptRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim("'", '"') }
}
$testNames = 'FANHUB_TEST_SQL_ADMIN','FANHUB_TEST_RABBIT_PASSWORD','FANHUB_TEST_RABBIT_PORT','FANHUB_TEST_RABBIT_MANAGEMENT_PORT'
$saved = @{}
foreach ($name in $testNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:FANHUB_TEST_SQL_ADMIN = "Server=127.0.0.1,$($values.SQL_PORT);Database=master;User Id=sa;Password=$($values.MSSQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True"
    $env:FANHUB_TEST_RABBIT_PASSWORD = $values.RABBITMQ_PASSWORD
    $env:FANHUB_TEST_RABBIT_PORT = $values.RABBITMQ_PORT
    $env:FANHUB_TEST_RABBIT_MANAGEMENT_PORT = $values.RABBITMQ_MANAGEMENT_PORT
    & dotnet test (Join-Path $PSScriptRoot '../../tests/FanHub.PaymentWalletService.IntegrationTests/FanHub.PaymentWalletService.IntegrationTests.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Payment integration tests failed.' }
} finally {
    foreach ($name in $testNames) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
