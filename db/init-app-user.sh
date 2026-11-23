#!/bin/bash
# ADR-0009: only init services connect as the PostgreSQL superuser.
set -euo pipefail

if [ "${1:-}" = "readonly" ]; then
  if [ -n "${READONLY_DB_PASSWORD:-}" ]; then
    psql -X -d briefyn -v ON_ERROR_STOP=1 \
      -v readonly_password="$READONLY_DB_PASSWORD" \
      -f /db/init-readonly-user.sql
    echo "db-readonly: briefyn_okur (salt okunur) hazır"
  fi
  exit 0
fi

psql -X -d postgres -v ON_ERROR_STOP=1 \
  -v app_password="$APP_DB_PASSWORD" \
  -v migrator_password="$MIGRATOR_DB_PASSWORD" \
  -f /db/init-app-user.sql
echo "db-init: briefyn_app ve briefyn_migrator hazır"
