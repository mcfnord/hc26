#!/bin/bash
# setup.sh — turn a fresh Ubuntu 24.04 droplet into the HexC test box (PLAN.md, Vision 2, M0).
#
# Paste into the droplet's console, as root:
#   curl -fsSL https://raw.githubusercontent.com/mcfnord/hc26/claude/clever-shannon-zxtkn3/deploy/testbox/setup.sh | bash
#
# Safe to run again. Afterwards the box updates itself from the branch every 2 minutes
# (update.sh). Commands it installs:
#   hexc-status    what is live, recent deploys and errors (paste this to Claude)
#   hexc-update    check for a new commit now instead of waiting
#   hexc-revert    go back one release (see deploy/revert.sh); stays until the next push
set -euo pipefail

BRANCH="${BRANCH:-claude/clever-shannon-zxtkn3}"
REPO_URL="${REPO_URL:-https://github.com/mcfnord/hc26}"
SRC=/opt/hexc/src

[[ $EUID == 0 ]] || { echo "Run this as root." >&2; exit 1; }
. /etc/os-release
[[ "$VERSION_ID" == "24.04" ]] || echo "Warning: written for Ubuntu 24.04; this is $PRETTY_NAME." >&2

echo "==> Swap (building .NET on a 1 GB droplet needs it)"
if ! swapon --show | grep -q .; then
    fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap -q /swapfile && swapon /swapfile
    grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "==> Packages (a few minutes)"
apt-get update -q
DEBIAN_FRONTEND=noninteractive apt-get install -y -q \
    dotnet-sdk-8.0 nginx sqlite3 git curl certbot python3-certbot-nginx >/dev/null

echo "==> User and folders"
id hexc >/dev/null 2>&1 || useradd --system --home-dir /var/lib/hexc --shell /usr/sbin/nologin hexc
mkdir -p /opt/hexc /var/lib/hexc /etc/hexc
chown hexc:hexc /var/lib/hexc

echo "==> Source: $BRANCH"
if [[ -d "$SRC/.git" ]]; then
    git -C "$SRC" fetch -q origin "$BRANCH" && git -C "$SRC" reset -q --hard FETCH_HEAD
else
    git clone -q --branch "$BRANCH" "$REPO_URL" "$SRC"
fi

echo "==> Address"
ip=$(curl -sf --max-time 3 http://169.254.169.254/metadata/v1/interfaces/public/0/ipv4/address || hostname -I | awk '{print $1}')
host="${ip//./-}.sslip.io"     # sslip.io resolves this name to $ip: a free HTTPS name with no DNS setup

echo "==> nginx for $host"
sed "s/__HOST__/$host/" "$SRC/deploy/testbox/nginx.conf" > /etc/nginx/sites-available/hexc-testbox
[[ -e /proc/net/if_inet6 ]] || sed -i '/listen \[::\]/d' /etc/nginx/sites-available/hexc-testbox   # no IPv6 here
ln -sfn /etc/nginx/sites-available/hexc-testbox /etc/nginx/sites-enabled/hexc-testbox
rm -f /etc/nginx/sites-enabled/default
nginx -t -q
systemctl reload nginx 2>/dev/null || systemctl restart nginx

echo "==> HTTPS certificate"
if certbot --nginx -d "$host" --non-interactive --agree-tos --register-unsafely-without-email --redirect -q; then
    site="https://$host"
else
    site="http://$ip"
    echo "    No certificate (Let's Encrypt refused or was unreachable). The site works over plain HTTP." >&2
fi

echo "==> Services"
cat > /etc/hexc/testbox.env <<EOF
BRANCH=$BRANCH
SRC=$SRC
SITE_URL=$site
EOF
sed 's/(johns.living)/(test box)/' "$SRC/deploy/hexc.service" > /etc/systemd/system/hexc.service
cp "$SRC/deploy/testbox/hexc-update.service" "$SRC/deploy/testbox/hexc-update.timer" /etc/systemd/system/
ln -sfn "$SRC/deploy/testbox/hexc-status" /usr/local/bin/hexc-status
ln -sfn "$SRC/deploy/testbox/update.sh"   /usr/local/bin/hexc-update
ln -sfn "$SRC/deploy/revert.sh"           /usr/local/bin/hexc-revert
systemctl daemon-reload
systemctl enable -q hexc
systemctl enable -q --now hexc-update.timer

echo "==> First build and deploy (a few minutes)"
rm -f /opt/hexc/last-seen
"$SRC/deploy/testbox/update.sh"

if curl -sf --max-time 5 http://127.0.0.1:5235/healthz >/dev/null; then
    echo
    echo "HexC test box is ready: $site"
    echo "It now updates itself from $BRANCH every 2 minutes. Run hexc-status any time."
else
    echo
    echo "!!! The server did not come up. Paste the output of hexc-status to Claude." >&2
    exit 1
fi
