#!/bin/sh
set -eu
umask 077
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
BACKUP_DIR=${SUGAR_BACKUP_DIR:-"$ROOT/backups/postgres"}
RETENTION_DAYS=${SUGAR_BACKUP_RETENTION_DAYS:-14}
case "$RETENTION_DAYS" in ''|*[!0-9]*) echo 'Retention must be a positive integer' >&2; exit 1;; esac
[ "$RETENTION_DAYS" -gt 0 ]
mkdir -p "$BACKUP_DIR"
exec 9>"$BACKUP_DIR/.backup.lock"
flock -n 9 || exit 0
CONTAINER=$(docker compose --env-file "$ROOT/.env" -f "$ROOT/deploy/docker-compose.yml" ps -q postgres)
[ -n "$CONTAINER" ]
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
TARGET="$BACKUP_DIR/sugar-$STAMP.dump"
trap 'rm -f "$TARGET.tmp"' EXIT HUP INT TERM
docker exec "$CONTAINER" sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner' > "$TARGET.tmp"
test -s "$TARGET.tmp"
docker exec -i "$CONTAINER" pg_restore --list < "$TARGET.tmp" >/dev/null
mv "$TARGET.tmp" "$TARGET"
sha256sum "$TARGET" > "$TARGET.sha256"
find "$BACKUP_DIR" -maxdepth 1 -type f \( -name 'sugar-*.dump' -o -name 'sugar-*.dump.sha256' \) -mtime +"$RETENTION_DAYS" -delete
printf '{"last_success_utc":"%s","filename":"%s","retention_days":%s}\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(basename "$TARGET")" "$RETENTION_DAYS" > "$BACKUP_DIR/status.json.tmp"
mv "$BACKUP_DIR/status.json.tmp" "$BACKUP_DIR/status.json"
printf 'Backup verified: %s\n' "$TARGET"
