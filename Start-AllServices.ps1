Write-Host "====== KHOI DONG TOAN BO FANHUBPLUS MOCROSERVICES ======" -ForegroundColor Cyan
Write-Host "1. Khoi dong Ha tang (SQL Server, Redis, RabbitMQ)..."
.\docker\chinhduc\Start-Databases.ps1 -Verify

Write-Host "
2. Khoi dong Identity Service..."
.\docker\chinhduc\Start-IdentityService.ps1

Write-Host "
3. Khoi dong Event Service..."
.\docker\chinhduc\Start-EventService.ps1

Write-Host "
4. Khoi dong Booking Service..."
.\docker\chinhduc\Start-BookingService.ps1

Write-Host "
5. Khoi dong Notification Service..."
.\docker\chinhduc\Start-NotificationService.ps1

Write-Host "
6. Khoi dong Payment Service..."
.\docker\chinhduc\Start-PaymentService.ps1

Write-Host "
====== HOAN TAT ======" -ForegroundColor Green
Write-Host "Cac service hien dang chay ngam trong Docker."
Write-Host "De dung toan bo, chay lenh: docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml down" -ForegroundColor Yellow
