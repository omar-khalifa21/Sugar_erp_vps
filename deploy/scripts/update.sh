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
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --no-build api web
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps
curl --fail --silent --show-error --retry 8 --retry-delay 2 http://127.0.0.1:3000/api/v1/ready
curl --fail --silent --show-error --retry 8 --retry-delay 2 http://127.0.0.1:4173/
