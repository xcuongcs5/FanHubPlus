#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
export SQLCMDPASSWORD="${SQLCMDPASSWORD:?Missing migration password}"
server="${SQL_SERVER:-sqlserver}"
root="${DATABASE_ROOT:-/database}"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
run_sql() { "$SQLCMD" -S "$server" -U sa -C -b -V 16 -r 1 -l 30 -t 120 -x "$@"; }

for service in event booking payment notification; do
    database="fanhub_${service}"
    login="fanhub_${service}_app"
    key="${service^^}_DB_PASSWORD"
    password="${!key:?Missing service database password}"
    # No quote, $, whitespace or sqlcmd metacharacters in bootstrap literals.
    if [[ ! "$password" =~ ^[a-zA-Z0-9_@#!%+=.-]{20,128}$ || "$password" == REPLACE_* ]]; then
        echo "Invalid $key: generate credentials with Initialize-Environment.ps1." >&2
        exit 1
    fi
    cat > "$work/bootstrap.sql" <<SQL
SET NOCOUNT ON;
IF DB_ID(N'$database') IS NULL EXEC(N'CREATE DATABASE [$database]');
IF SUSER_ID(N'$login') IS NULL
    CREATE LOGIN [$login] WITH PASSWORD = N'$password', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
SQL
    run_sql -d master -i "$work/bootstrap.sql"
    # Credentials are never printed or retained in the repository.
    cat > "$work/history.sql" <<SQL
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock = sys.sp_getapplock @Resource=N'fanhub-schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
IF @lock < 0 THROW 51000, 'Cannot acquire migration lock', 1;
IF OBJECT_ID(N'dbo.__schema_migrations', N'U') IS NULL
    CREATE TABLE dbo.__schema_migrations (
        version varchar(150) NOT NULL PRIMARY KEY,
        checksum char(64) NOT NULL,
        applied_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
    );
COMMIT;
SQL
    run_sql -d "$database" -i "$work/history.sql"
    # Migrations are globally ordered by filename across the service and common directories.
    # Filenames must start with a unique increasing version, e.g. 003_add_index.sql.
    mapfile -t migrations < <(find "$root/$service" "$root/common" -maxdepth 1 -name '*.sql' -printf '%f %p\n' | sort | cut -d ' ' -f 2-)
    [[ "${#migrations[@]}" -gt 0 ]] || { echo "No migrations found for $service" >&2; exit 1; }
    previous=''
    for migration in "${migrations[@]}"; do
        version="$(basename "$migration" .sql)"
        [[ "$version" =~ ^[0-9]{3}_[a-z0-9_]+$ ]] || { echo "Invalid migration filename" >&2; exit 1; }
        number="${version%%_*}"
        [[ "$number" != "$previous" ]] || { echo "Duplicate migration version $number" >&2; exit 1; }
        previous="$number"
        checksum="$(sha256sum "$migration" | cut -d ' ' -f 1)"
        cat > "$work/apply.sql" <<SQL
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock = sys.sp_getapplock @Resource=N'fanhub-schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
IF @lock < 0 THROW 51000, 'Cannot acquire migration lock', 1;
IF EXISTS (SELECT 1 FROM dbo.__schema_migrations WHERE version='$version')
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.__schema_migrations WHERE version='$version' AND checksum <> '$checksum')
        THROW 51001, 'Applied migration checksum changed; add a new migration instead', 1;
    COMMIT;
    PRINT 'Already applied: $database/$version';
    RETURN;
END;
SQL
        # Execute as a separate dynamic batch so already-applied DDL is not compiled.
        # It remains in this transaction; escaping also preserves Unicode source text.
        printf "EXEC(N'" >> "$work/apply.sql"
        sed "s/'/''/g" "$migration" >> "$work/apply.sql"
        printf "');\n" >> "$work/apply.sql"
        cat >> "$work/apply.sql" <<SQL
INSERT dbo.__schema_migrations(version,checksum) VALUES('$version','$checksum');
COMMIT;
PRINT 'Applied: $database/$version';
SQL
        run_sql -d "$database" -i "$work/apply.sql"
    done
    cat > "$work/permissions.sql" <<SQL
SET NOCOUNT ON;
IF DATABASE_PRINCIPAL_ID(N'$login') IS NULL CREATE USER [$login] FOR LOGIN [$login];
-- No db_owner, DDL, migration history writes, or access to other service databases.
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.' + QUOTENAME(name) + N' TO [$login];'
FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') AND name <> '__schema_migrations';
EXEC sys.sp_executesql @sql;
GRANT SELECT ON dbo.__schema_migrations TO [$login];
IF OBJECT_ID(N'dbo.WALLET_LEDGER',N'U') IS NOT NULL
    EXEC(N'DENY UPDATE, DELETE ON dbo.WALLET_LEDGER TO [$login]');
SQL
    run_sql -d "$database" -i "$work/permissions.sql"
    # Fail on stale/mismatched .env credentials rather than silently reporting success.
    SQLCMDPASSWORD="$password" "$SQLCMD" -S "$server" -U "$login" -d "$database" -C -b -l 15 -Q 'SET NOCOUNT ON; SELECT COUNT(*) AS applied_migrations FROM dbo.__schema_migrations;'
done
echo 'All four service databases migrated.'
