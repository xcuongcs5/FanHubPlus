Write-Host "====== KHOI DONG TOAN BO FANHUBPLUS MOCROSERVICES ======" -ForegroundColor Cyan
Write-Host "1. Khoi dong Ha tang (SQL Server, Redis, RabbitMQ)..."
.\docker\chinhduc\Start-Databases.ps1 -Verify

Write-Host "`n2. Khoi dong Identity Service..."
.\docker\chinhduc\Start-IdentityService.ps1

Write-Host "`n3. Khoi dong Event Service..."
.\docker\chinhduc\Start-EventService.ps1

Write-Host "`n4. Khoi dong Booking Service..."
.\docker\chinhduc\Start-BookingService.ps1

Write-Host "`n5. Khoi dong Notification Service..."
.\docker\chinhduc\Start-NotificationService.ps1

Write-Host "`n6. Khoi dong Payment Service..."
.\docker\chinhduc\Start-PaymentService.ps1

Write-Host "`n7. Khoi dong Search Service (MongoDB + Node.js)..."
.\docker\chinhduc\Start-SearchService.ps1

Write-Host "`n8. Khoi dong Blockchain Service (Node.js)..."
.\docker\chinhduc\Start-BlockchainService.ps1

Write-Host "`n9. Khoi dong Chatbot Service (Python + Qdrant)..."
.\docker\chinhduc\Start-ChatbotService.ps1

Write-Host "`n====== HOAN TAT ======" -ForegroundColor Green
Write-Host "Cac service hien dang chay ngam trong Docker."
Write-Host "De dung toan bo, chay lenh: docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml down" -ForegroundColor Yellow
