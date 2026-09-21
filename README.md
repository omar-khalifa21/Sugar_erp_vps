# Sugar ERP

Sugar ERP is an Arabic-first admin application backed by a modular-monolith NestJS/PostgreSQL API, with offline-first Windows desktop clients planned for Branch Type 1, Branch Type 2, and Kitchen profiles.

## First VPS vertical slice

Prerequisites: Git, Docker Engine, and Docker Compose. No global NestJS or PostgreSQL installation is required.

Create a private `.env` from `.env.example`, set unique bootstrap administrator credentials, then start PostgreSQL, apply migrations through the one-shot migration service, create only that administrator, and run the API and admin website:

```powershell
docker compose -f compose.dev.yml up --build --wait
```

Check the endpoints:

```powershell
Invoke-RestMethod http://localhost:3000/api/v1/health
Invoke-RestMethod http://localhost:3000/api/v1/ready
```

Open the admin website at <http://localhost:4173> and sign in with the private `BOOTSTRAP_ADMIN_USERNAME` and `BOOTSTRAP_ADMIN_PASSWORD` values from your `.env`. The repository contains no default login or operational sample data.

Run the integration suite against the real PostgreSQL container:

```powershell
.\scripts\test.ps1
```

To create or rotate the administrator explicitly:

```powershell
$env:BOOTSTRAP_ADMIN_USERNAME = 'your-admin-name'
$env:BOOTSTRAP_ADMIN_PASSWORD = 'choose-a-strong-unique-password'
docker compose -f compose.dev.yml run --rm -e BOOTSTRAP_ADMIN_USERNAME -e BOOTSTRAP_ADMIN_PASSWORD bootstrap-admin
```

Stop the stack without deleting its named database volume:

```powershell
.\scripts\dev-down.ps1
```

The public liveness endpoint is `/api/v1/health`; `/api/v1/ready` additionally checks PostgreSQL and migration state. Foundation resource endpoints require a bearer token returned by `POST /api/v1/auth/login`.

## Layout and access

`apps/server` is the NestJS API, `apps/admin-web` is the separate admin frontend, `packages/contracts` holds shared API and sync definitions, and `deploy` holds the VPS Compose and Nginx configuration. Branch Type 1 desktop work is maintained in a separate worktree and is not present in this checkout. No duplicate application tree was moved or removed here.

The admin website offers **Sign Up**. `POST /api/v1/auth/signup` accepts only username, display name, and password, and creates a `PENDING_PERMISSION` account with no roles. A current admin can see it under **Users and permissions**, assign a role and site, or disable it. `POST /api/v1/auth/login` returns the account status; a pending account can authenticate but receives no privileged API permissions. Disabled accounts cannot authenticate or use an existing token.

## Branch Type 1 installer downloads

Set `RELEASES_HOST_DIR` in the server-only `deploy/.env` to an absolute host directory, for example `/opt/sugar-erp/releases`, and ensure the API container's `node` user can read it. Put a reviewed Branch Type 1 installer at `$RELEASES_HOST_DIR/branch-type-1/Sugar-Branch-Type-1-VERSION.exe` (or `.msi`/`.msix`). Publish `$RELEASES_HOST_DIR/branch-type-1/current.json` with `channel` set to `production`, plus `version`, `filename`, `publishedAt` (ISO-8601), `sha256` (64 hex characters), and optional `releaseNotes`. Publish the installer before replacing the manifest atomically. Never place credentials, local databases, or sample operational data in installer artifacts. The API returns 404 until both files are present and the production channel is valid.

Authenticated admins can read `GET /api/v1/releases/branch-type-1/current` and download `GET /api/v1/releases/branch-type-1/current/download`. The admin Operations page uses these endpoints over the same HTTPS origin, so a remote browser downloads from the VPS without a Windows filesystem path. The endpoint reads only the validated filename from the server-managed manifest. Build/signature verification and artifact promotion remain separate release gates.

The Touch variant has its own `branch-type-1-touch/current.json` and `/api/v1/releases/branch-type-1/touch/current` endpoint. The Operations page shows both variants and the persistent branch setup URL, `https://ascendyz.xyz/api/v1`. An enrollment code is valid once for 30 minutes; it is shown only when issued, and an admin can issue a new one later. The server stores only its hash, so the original code cannot be retrieved after leaving the page.
