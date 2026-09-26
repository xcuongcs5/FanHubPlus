Write-Host "====== KHOI DONG NODEJS SERVICES ======" -ForegroundColor Cyan

# 1. Media Streaming Service
Write-Host "
1. Khoi dong Media Streaming Service..."
cd services\nodejs\media-streaming-service
if (-not (Test-Path .env)) {
    Copy-Item .env.example .env
    Write-Host "Da copy .env.example sang .env"
}
Write-Host "Dang cai dat dependencies (npm install)..."
npm install
Write-Host "Khoi dong Media Streaming (Port 3002) trong cua so moi..."
Start-Process "cmd" -ArgumentList "/c npm run dev" -WindowStyle Normal
cd ..\..\..

# 2. GPS Location Service
Write-Host "
2. Khoi dong GPS Location Service..."
cd services\nodejs\gps-location-service
Write-Host "Khoi dong Redis & RabbitMQ rieng cho GPS..."
docker compose -f docker-compose.dev.yml up -d

Write-Host "Dang cai dat dependencies (npm install)..."
npm install
Write-Host "Khoi dong GPS Location (Port 3001) trong cua so moi..."
Start-Process "cmd" -ArgumentList "/c npm run dev" -WindowStyle Normal
cd ..\..\..

Write-Host "
====== HOAN TAT ======" -ForegroundColor Green
Write-Host "Hai Node.js service da duoc bat len trong 2 cua so Command Prompt (cmd) moi."
