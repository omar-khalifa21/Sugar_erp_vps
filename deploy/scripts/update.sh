#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
ENV_FILE="$ROOT/.env"
COMPOSE_FILE="$ROOT/deploy/docker-compose.yml"

if [ ! -f "$ENV_FILE" ]; then
    echo "Create $ENV_FILE from deploy/.env.example and fill it manually before deployment." >&2
    exit 1
fi

cd "$ROOT"
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" config --quiet
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" build api web migrate
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" run --rm migrate
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --no-build --force-recreate api web
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps
for url in http://127.0.0.1:3000/api/v1/ready http://127.0.0.1:4173/; do
    ready=0
    for attempt in $(seq 1 30); do
        if curl --fail --silent --show-error "$url" >/dev/null 2>&1; then
            ready=1
            break
        fi
        sleep 2
    done
    if [ "$ready" -ne 1 ]; then
        echo "Service did not become ready: $url" >&2
        exit 1
    fi
done
