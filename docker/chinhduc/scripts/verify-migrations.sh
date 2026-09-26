#!/usr/bin/env bash
# Tests only the already-migrated development databases. Copies files to /tmp.
set -Eeuo pipefail
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
cp -R /database "$work/database"
export DATABASE_ROOT="$work/database"

printf '\n-- deliberate checksum probe\n' >> "$DATABASE_ROOT/event/001_initial.sql"
if bash /scripts/migrate.sh > "$work/result.log" 2>&1; then
    echo 'FAIL: modified applied migration was accepted' >&2
    exit 1
fi
grep -q 'Applied migration checksum changed' "$work/result.log"
echo 'PASS migration: modified applied file rejected'
cp /database/event/001_initial.sql "$DATABASE_ROOT/event/001_initial.sql"

cat > "$DATABASE_ROOT/event/999_rollback_probe.sql" <<'SQL'
CREATE TABLE dbo.__migration_rollback_probe(id int NOT NULL);
THROW 51999, 'Intentional rollback verification', 1;
SQL
if bash /scripts/migrate.sh > "$work/result.log" 2>&1; then
    echo 'FAIL: deliberately failing migration was accepted' >&2
    exit 1
fi
grep -q 'Intentional rollback verification' "$work/result.log"
/opt/mssql-tools18/bin/sqlcmd -S "${SQL_SERVER:-sqlserver}" -U sa -C -I -b -x -d fanhub_event -Q "
IF OBJECT_ID(N'dbo.__migration_rollback_probe',N'U') IS NOT NULL THROW 51000, 'DDL was not rolled back', 1;
IF EXISTS(SELECT 1 FROM dbo.__schema_migrations WHERE version='999_rollback_probe') THROW 51000, 'Failed migration was recorded', 1;
PRINT 'PASS migration: failed DDL and history rolled back together';"
