#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
BACKUP=${1:?Pass an existing PostgreSQL custom-format backup}
test -s "$BACKUP"
if [ -f "$BACKUP.sha256" ]; then sha256sum -c "$BACKUP.sha256"; fi
CONTAINER=$(docker compose --env-file "$ROOT/.env" -f "$ROOT/deploy/docker-compose.yml" ps -q postgres)
[ -n "$CONTAINER" ]
RESTORE_DB="sugar_restore_check_$(date -u +%Y%m%d%H%M%S)_$$"
cleanup() { docker exec "$CONTAINER" sh -c 'dropdb -U "$POSTGRES_USER" --if-exists "$1"' sh "$RESTORE_DB"; }
trap cleanup EXIT HUP INT TERM
docker exec "$CONTAINER" sh -c 'createdb -U "$POSTGRES_USER" "$1"' sh "$RESTORE_DB"
docker exec -i "$CONTAINER" sh -c 'pg_restore -U "$POSTGRES_USER" -d "$1" --exit-on-error --no-owner' sh "$RESTORE_DB" < "$BACKUP"
docker exec "$CONTAINER" sh -c 'psql -U "$POSTGRES_USER" -d "$1" -v ON_ERROR_STOP=1 -Atc "SELECT count(*) FROM _prisma_migrations; SELECT count(*) FROM sites; SELECT count(*) FROM sync_events;"' sh "$RESTORE_DB"
printf 'Restore verification passed in isolated database: %s\n' "$RESTORE_DB"
