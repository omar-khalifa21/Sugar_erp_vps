#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
SITE=/etc/nginx/sites-available/sugar-erp.conf
ENABLED=/etc/nginx/sites-enabled/sugar-erp.conf

if [ ! -f /etc/letsencrypt/live/ascendyz.xyz/fullchain.pem ]; then
    echo "Issue the certificate for ascendyz.xyz and www.ascendyz.xyz before enabling HTTPS." >&2
    exit 1
fi

install -m 644 "$ROOT/deploy/nginx/app.conf" "$SITE"
ln -sfn "$SITE" "$ENABLED"
for old in /etc/nginx/sites-enabled/default /etc/nginx/sites-enabled/sugar-https.conf /etc/nginx/sites-enabled/sugar-http.conf; do
    if [ -L "$old" ]; then unlink "$old"; fi
done
nginx -t
systemctl reload nginx
install -m 755 "$ROOT/deploy/scripts/renewal-reload.sh" /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
curl --fail --silent --show-error --head https://ascendyz.xyz/
