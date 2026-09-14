# Sugar ERP VPS deployment

The Git checkout is `/opt/sugar-erp`. Host Nginx terminates TLS; Compose runs the NestJS API, the existing admin web build, a one-shot migration job, and PostgreSQL. The database uses the existing named volume `sugar-erp_postgres-data`. Do not delete that volume or run `docker compose down -v`.

## Files

- `docker-compose.yml`: service orchestration, private network, health checks, restart policies, and localhost-only host ports.
- `backend/Dockerfile`: development, migration, and non-root production API images.
- `web/Dockerfile`: builds and serves the existing admin web assets.
- `nginx/app.conf`: HTTP redirect and HTTPS proxy for both domain names.
- `scripts/update.sh`: validates configuration, builds images, migrates, starts services, and checks local health.
- `scripts/deploy.sh`: installs the tracked Nginx site and renewal hook after a certificate exists.
- `scripts/renewal-reload.sh`: tests and reloads Nginx when Certbot renews a certificate.
- `.env.example`: variable names and empty placeholders only.

## Server-only environment

Create `/opt/sugar-erp/.env` manually from `deploy/.env.example`, set mode `0600`, and fill every value. `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` must match the credentials used to initialize an existing database volume. `DATABASE_URL` must point to `postgres:5432` from inside Docker and use the same database, user, and password; URL-encode reserved password characters. `JWT_SECRET` is the API signing secret. The example contains no usable credentials. Do not commit or paste the filled file into logs.

The currently running VPS was deployed with an earlier Compose layout and has an existing root-owned `.env`. The refactor does **not** overwrite it. Review and complete its missing keys yourself before running the new Compose stack. Docker Compose's `config --quiet` step rejects missing values. Do not change the existing database user's credentials just to satisfy the new file format; doing so does not reinitialize a persistent PostgreSQL volume.

## First deployment and updates

```sh
ssh root@198.199.85.22
cd /opt/sugar-erp
git pull --ff-only
chmod 600 .env
docker compose --env-file .env -f deploy/docker-compose.yml config --quiet
sh deploy/scripts/update.sh
sh deploy/scripts/deploy.sh
curl -fsS https://ascendyz.xyz/api/v1/ready
```

The certificate for `ascendyz.xyz` and `www.ascendyz.xyz` already exists on this VPS. On a fresh server, verify DNS and HTTP first, then issue it with `certbot certonly --webroot -w /var/www/letsencrypt -d ascendyz.xyz -d www.ascendyz.xyz` before running `deploy.sh`. The script runs `nginx -t` before reload. Renewal is handled by `certbot.timer` plus the tracked reload hook.

For later updates, run `git pull --ff-only` followed by `sh deploy/scripts/update.sh`. The script applies migrations before replacing the API container. Take a verified off-VPS backup before onboarding business data or performing a schema-changing release.

## pgAdmin over SSH

PostgreSQL is bound on the VPS at `127.0.0.1:5432`; UFW must **not** allow public port 5432. On Windows, open a terminal and leave this tunnel running:

```powershell
ssh -N -L 5433:127.0.0.1:5432 root@198.199.85.22
```

In pgAdmin, connect to host `127.0.0.1`, port `5433`, and enter the database name, username, and password you configured in the VPS `.env`. Choose another local port if 5433 is occupied. Public inbound ports remain 22, 80, and 443 only.

The deployed API and admin UI are a foundation, not a complete production business workflow. Do not onboard live business data until the documented workflow, backup/restore, and dependency-audit gaps are resolved.
