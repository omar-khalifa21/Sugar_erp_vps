$ErrorActionPreference = 'Stop'
docker compose -f compose.dev.yml up --build --wait
