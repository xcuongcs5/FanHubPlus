$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-Databases.ps1')

$envPath = Join-Path $PSScriptRoot '.env'
$chatbotEnvPath = Join-Path $PSScriptRoot '..\..\services\python\chatbot-service\.env'

# Copy GEMINI_API_KEY from chatbot .env if it exists, or just pass it directly if we loaded it in session
$composeArgs = @('compose', '--env-file', $envPath, '--env-file', $chatbotEnvPath, '-f', (Join-Path $PSScriptRoot 'compose.yml'), '-f', (Join-Path $PSScriptRoot 'chatbot-service.yml'))

& docker @composeArgs up -d --build --wait --wait-timeout 180 chatbot-service qdrant
if ($LASTEXITCODE -ne 0) { throw 'Chatbot service build/start failed.' }
Write-Host 'Chatbot Service started. Port: http://localhost:3005'
