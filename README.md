# Sugar ERP

Sugar ERP is an Arabic-first admin application backed by a modular-monolith NestJS/PostgreSQL API, with offline-first Windows desktop clients planned for Branch Type 1, Branch Type 2, and Kitchen profiles.

## First VPS vertical slice

Prerequisites: Git, Docker Engine, and Docker Compose. No global NestJS or PostgreSQL installation is required.

Start PostgreSQL, apply migrations through the one-shot migration service, seed synthetic local data, and run the API and admin website:

```powershell
docker compose -f compose.dev.yml up --build --wait
```

Check the endpoints:

```powershell
Invoke-RestMethod http://localhost:3000/api/v1/health
Invoke-RestMethod http://localhost:3000/api/v1/ready
```

Open the admin website at <http://localhost:4173>. The default Compose-only demo login is `admin` / `SugarAdmin2026!`. It is an intentionally public local-development credential and must never be reused in staging or production; override `DEV_ADMIN_USERNAME` and `DEV_ADMIN_PASSWORD` in `.env` whenever the environment is shared.

Run the integration suite against the real PostgreSQL container:

```powershell
.\scripts\test.ps1
```

To reseed a development administrator with your own local password:

```powershell
$env:DEV_ADMIN_PASSWORD = 'choose-at-least-12-characters'
docker compose -f compose.dev.yml run --rm -e DEV_ADMIN_PASSWORD -e DEV_SEED_SYNTHETIC=true seed
```

Stop the stack without deleting its named database volume:

```powershell
.\scripts\dev-down.ps1
```

The public liveness endpoint is `/api/v1/health`; `/api/v1/ready` additionally checks PostgreSQL and migration state. Foundation resource endpoints require a bearer token returned by `POST /api/v1/auth/login`.
