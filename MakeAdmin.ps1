param (
    [Parameter(Mandatory=$true, HelpMessage="Nhap email tai khoan ban muon nang cap len Admin")]
    [string]$Email
)

$sqlQuery = @"
DECLARE @UserId UNIQUEIDENTIFIER = (SELECT Id FROM Users WHERE Email = '$Email');
IF @UserId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM UserRoles WHERE UserId = @UserId AND RoleId = '11111111-1111-1111-1111-111111111111')
    BEGIN
        INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, '11111111-1111-1111-1111-111111111111');
        PRINT '=== THANH CONG: Tai khoan $Email da duoc cap quyen Admin! ===';
    END
    ELSE
    BEGIN
        PRINT '=== THONG BAO: Tai khoan nay da la Admin tu truoc roi! ===';
    END
END
ELSE
BEGIN
    PRINT '=== LOI: Khong tim thay email nay trong database! ===';
END
"@

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " FANHUB - TOOL CAP QUYEN ADMIN TUDONG" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Dang ket noi vao Database SQL Server..."

$cmd = "docker exec fanhub-chinhduc-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P Fh9@02FE4691F98196E2AB6BE7EBE079BCD01BC7F999648B72D6 -C -d FanHub_Identity -Q `"$sqlQuery`""
Invoke-Expression $cmd

Write-Host "`nNeu bao thanh cong, vui long dAng xuat va dAng nhap lai tren web de nhan quyen." -ForegroundColor Yellow