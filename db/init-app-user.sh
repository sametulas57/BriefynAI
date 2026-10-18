#!/bin/bash
# Runs once per "docker compose up" (service db-init): waits for SQL Server, then applies init-app-user.sql as sa.
set -euo pipefail
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
HOST="${DB_HOST:-db}"
for i in $(seq 1 60); do
  if $SQLCMD -S "$HOST" -U sa -C -l 5 -Q "SELECT 1" >/dev/null 2>&1; then break; fi
  sleep 2
done
$SQLCMD -S "$HOST" -U sa -C -b -I \
  -v DB_NAME="${DB_NAME:-BriefynDB}" APP_DB_USER="${APP_DB_USER:-briefyn_app}" APP_DB_PASSWORD="$APP_DB_PASSWORD" \
  -i /db/init-app-user.sql
echo "db-init: ${APP_DB_USER:-briefyn_app} hazır"
if [ -n "${READONLY_DB_PASSWORD:-}" ]; then
  $SQLCMD -S "$HOST" -U sa -C -b -I \
    -v DB_NAME="${DB_NAME:-BriefynDB}" RO_DB_USER="${RO_DB_USER:-briefyn_okur}" RO_DB_PASSWORD="$READONLY_DB_PASSWORD" \
    -i /db/init-readonly-user.sql
  echo "db-init: ${RO_DB_USER:-briefyn_okur} (salt okunur) hazır"
fi
