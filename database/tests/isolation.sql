SET NOCOUNT ON;
DECLARE @service varchar(20), @other varchar(20), @sql nvarchar(max);
DECLARE services CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM (VALUES('event'),('booking'),('payment'),('notification')) AS s(name);
OPEN services;
FETCH NEXT FROM services INTO @service;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @sql=N'USE '+QUOTENAME('fanhub_'+@service)+N';
        IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1) THROW 51000, ''Invalid FK'', 1;
        IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1) THROW 51000, ''Invalid CHECK'', 1;';
    EXEC sys.sp_executesql @sql;
    SET @sql=N'USE '+QUOTENAME('fanhub_'+@service)+N';
    EXECUTE AS LOGIN = ''fanhub_'+@service+N'_app'';
    BEGIN TRY
        IF HAS_PERMS_BY_NAME(''dbo.USER_PROJECTION'',''OBJECT'',''INSERT'') <> 1 THROW 51000, ''Missing own database write access'', 1;
        IF HAS_PERMS_BY_NAME(''dbo.__schema_migrations'',''OBJECT'',''UPDATE'') <> 0 THROW 51000, ''App can mutate migration history'', 1;
        IF HAS_PERMS_BY_NAME(DB_NAME(),''DATABASE'',''CREATE TABLE'') <> 0 THROW 51000, ''App has DDL permission'', 1;
        IF DB_NAME() = ''fanhub_payment'' AND (HAS_PERMS_BY_NAME(''dbo.WALLET_LEDGER'',''OBJECT'',''UPDATE'') <> 0 OR HAS_PERMS_BY_NAME(''dbo.WALLET_LEDGER'',''OBJECT'',''DELETE'') <> 0)
            THROW 51000, ''Ledger is mutable by app'', 1;
        IF (SELECT COUNT(*) FROM sys.databases WHERE name IN (''fanhub_event'',''fanhub_booking'',''fanhub_payment'',''fanhub_notification'') AND HAS_DBACCESS(name)=1) <> 1
            THROW 51000, ''Cross-service database access permitted'', 1;
        REVERT;
    END TRY
    BEGIN CATCH
        REVERT;
        THROW;
    END CATCH;';
    EXEC sys.sp_executesql @sql;
    PRINT 'PASS isolation: '+@service;
    FETCH NEXT FROM services INTO @service;
END;
CLOSE services;
DEALLOCATE services;
