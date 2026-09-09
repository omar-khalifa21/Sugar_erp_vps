$ErrorActionPreference = 'Stop'
docker compose -f compose.dev.yml up -d --build --wait postgres migrate
docker compose -f compose.dev.yml --profile test run --rm api-test
