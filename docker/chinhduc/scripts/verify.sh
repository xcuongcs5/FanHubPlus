#!/usr/bin/env bash
set -Eeuo pipefail
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
for service in event booking payment notification; do
    "$SQLCMD" -S "${SQL_SERVER:-sqlserver}" -U sa -C -I -b -V 16 -r 1 -x \
        -d "fanhub_$service" -i "/database/tests/$service.sql"
done
"$SQLCMD" -S "${SQL_SERVER:-sqlserver}" -U sa -C -I -b -V 16 -r 1 -x \
    -d master -i /database/tests/isolation.sql
echo 'Database checks passed. Test data was rolled back.'
