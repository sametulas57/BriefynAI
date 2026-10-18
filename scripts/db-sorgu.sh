#!/usr/bin/env bash
# Salt okunur veritabanı sorgusu (2026-10-18). Hiçbir şeyi değiştiremez.
#   scripts/db-sorgu.sh --sema                 tablolar ve kolonlar
#   scripts/db-sorgu.sh "SELECT TOP 5 Id, Status FROM MeetingSummaries"
# Parola .env'deki READONLY_DB_PASSWORD'dan okunur, ekrana yazılmaz. Yığın açık olmalı (briefyn-sql).
set -euo pipefail
cd "$(dirname "$0")/.."
PASS=$(grep -E '^READONLY_DB_PASSWORD=' .env | head -1 | cut -d= -f2- | sed -e "s/^'//" -e "s/'$//")
[ -n "$PASS" ] || { echo "READONLY_DB_PASSWORD .env'de yok"; exit 1; }
if [ "${1:-}" = "--sema" ]; then
  Q="SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_NAME, ORDINAL_POSITION"
else
  Q="${1:?Sorgu ver ya da --sema}"
fi
docker exec -e SQLCMDPASSWORD="$PASS" briefyn-sql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U briefyn_okur -d BriefynDB -C -W -s '|' -Q "SET NOCOUNT ON; $Q"
