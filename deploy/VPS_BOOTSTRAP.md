# Sugar VPS bootstrap and current release boundary

VPS: Ubuntu 24.04 at `198.199.85.22`. Public API target: `https://ascendyz.xyz/api/v1`. Never use the IP as an untrusted HTTP enrollment URL.

`compose.prod.yml` keeps PostgreSQL on a private Docker bridge with **no published port** and binds the API to host loopback. Nginx is the only intended public entry point. `/opt/sugar/.env` must be root-owned mode 0600 with unique `POSTGRES_PASSWORD` and `JWT_SECRET`; never commit or display those values. Run migrations as the explicit one-shot `migrate` service before API promotion. Do not run the synthetic development seed on production.

DNS/HTTPS: both `ascendyz.xyz` and `www.ascendyz.xyz` resolve to the VPS. Certbot uses webroot `/var/www/letsencrypt` and has issued a certificate for both names. The active host Nginx site is `deploy/nginx/sugar-https.conf`; it redirects HTTP to HTTPS, proxies the API to loopback port 3000, and proxies the existing admin build to loopback port 4173. Install `deploy/nginx/renewal-reload.sh` as `/etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh` (mode 0755) so renewed certificates are loaded. Always run `nginx -t` before reloading. Verify with `certbot renew --dry-run --no-random-sleep-on-renew`.

Routine update from the Git-backed `/opt/sugar-erp` checkout:

```sh
cd /opt/sugar-erp
git pull --ff-only
docker compose -f compose.prod.yml build api web migrate
docker compose -f compose.prod.yml run --rm migrate
docker compose -f compose.prod.yml up -d --no-build --force-recreate api web
docker compose -f compose.prod.yml ps
curl -fsS http://127.0.0.1:3000/api/v1/ready
curl -fsS https://ascendyz.xyz/api/v1/ready
```

The root-owned `/opt/sugar-erp/.env` is server-only mode 0600. PostgreSQL data resides in the named volume `sugar-erp_postgres-data`; do not remove this volume during updates. Take and verify an off-VPS database backup before business data is onboarded.

The current server route forwards branch `kitchen_request.submitted` events into a kitchen device's pull feed when exactly one active kitchen site exists; an ambiguous or absent route is rejected without acknowledging the branch event. Admins can inspect recent submissions using `GET /api/v1/admin/kitchen/requests`. Admin-only, read-only event views for recent site sales and shift closures are `GET /api/v1/admin/sites/:siteId/sales` and `/shift-closes` (200 most recent). They show server-received facts with `as_of`; they are **not** normalized financial/stock projections or a finished dashboard. This is not a complete production workflow: the kitchen desktop is not yet in this checkout, Branch Type 1's incoming applier currently rejects cross-site origin IDs, and durable normalized business projections/dispatch handling still need an end-to-end implementation and tests. No production sites or users should be onboarded until those gaps and backup/restore tests are closed. The production dependency audit still reports high-severity advisories; review and resolve them before public launch.
