param (
    [Parameter(Mandatory=$true, HelpMessage="Nhap email cua tai khoan can nang cap")]
    [string]$Email
)

$sql = @"
USE fanhub_identity;
DECLARE @UserId UNIQUEIDENTIFIER = (SELECT Id FROM Users WHERE Email = '$($Email.ToLower())');
DECLARE @AdminRoleId UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';

IF @UserId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM UserRoles WHERE UserId = @UserId AND RoleId = @AdminRoleId)
    BEGIN
        INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, @AdminRoleId);
        PRINT 'SUCCESS: Tai khoan [$Email] da duoc nang cap len Admin!';
    END
    ELSE BEGIN
        PRINT 'INFO: Tai khoan [$Email] da la Admin tu truoc roi!';
    END
END
ELSE BEGIN
    PRINT 'ERROR: Khong tim thay tai khoan [$Email] trong he thong. Vui long dang ky tren web truoc!';
END
"@

$tempFile = "promotecmd.sql"
$sql | Out-File -FilePath $tempFile -Encoding utf8

Write-Host "Dang cap quyen Admin cho [$Email]..." -ForegroundColor Cyan
docker cp $tempFile fanhub-chinhduc-sqlserver-1:/promotecmd.sql
docker exec fanhub-chinhduc-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Fh9@02FE4691F98196E2AB6BE7EBE079BCD01BC7F999648B72D6" -C -i /promotecmd.sql

Remove-Item $tempFile -ErrorAction SilentlyContinue
Write-Host "Xong! Vui long Dang xuat va Dang nhap lai de cap nhat quyen." -ForegroundColor Green