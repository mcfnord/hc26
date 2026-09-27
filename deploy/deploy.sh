#!/bin/bash
# deploy.sh — publish the committed HEAD of this repo as a new release and make it live.
#
#   ./deploy/deploy.sh            # deploy HEAD (working tree must be clean)
#   ./deploy/deploy.sh --dirty    # allow uncommitted changes (release named <sha>-dirty-<time>)
#
# Layout:
#   /opt/hexc/releases/<sha>/   published server
#   /opt/hexc/current           symlink to the live release
#   /opt/hexc/history           one release name per line, oldest first; last line is live
#   /opt/hexc/backups/<name>.db copy of the DB taken just before <name> went live
#   /var/lib/hexc/hexc.db       the live database
#
# If the smoke test fails after restart, the previous release is restored automatically.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ROOT=/opt/hexc
DATA=/var/lib/hexc
DB="$DATA/hexc.db"
URL=http://127.0.0.1:5235

cd "$REPO"
sha=$(git rev-parse --short HEAD)
if [[ -n "$(git status --porcelain)" ]]; then
    if [[ "${1:-}" == "--dirty" ]]; then
        sha="$sha-dirty-$(date +%H%M%S)"
    else
        echo "Working tree is not clean. Commit first, or pass --dirty." >&2
        exit 1
    fi
fi

mkdir -p "$ROOT/releases" "$ROOT/backups" "$DATA"
target="$ROOT/releases/$sha"
prev=$(readlink -f "$ROOT/current" 2>/dev/null || true)

echo "==> Publishing $sha"
rm -rf "$target"
dotnet publish HexC.Server -c Release -o "$target" -v q --nologo
echo "HEXC_RELEASE=$sha" > "$target/release.env"

if [[ -f "$DB" ]]; then
    echo "==> Backing up DB to backups/$sha.db"
    cp "$DB" "$ROOT/backups/$sha.db"
fi

echo "==> Switching current -> $sha"
ln -sfn "$target" "$ROOT/current"
echo "$sha" >> "$ROOT/history"
chown -R hexc:hexc "$DATA"
systemctl restart hexc

echo "==> Smoke test"
ok=0
for i in $(seq 1 30); do
    if body=$(curl -sf "$URL/healthz" 2>/dev/null) && [[ "$body" == *"\"release\":\"$sha\""* ]]; then
        ok=1; break
    fi
    sleep 1
done
if [[ $ok == 1 ]]; then
    gid="smoke$(date +%s | tr 0-9 a-j)"
    curl -sf -X POST "$URL/Game/create?gameId=$gid" >/dev/null \
      && curl -sf -X POST "$URL/Game/ai-move?gameId=$gid" >/dev/null || ok=0
fi

if [[ $ok == 1 ]]; then
    echo "==> LIVE: $sha  ($(date -u +%FT%TZ))" | tee -a "$ROOT/deploys.log"
else
    echo "!!! Smoke test FAILED for $sha" | tee -a "$ROOT/deploys.log" >&2
    journalctl -u hexc -n 30 --no-pager >&2 || true
    if [[ -n "$prev" ]]; then
        echo "==> Reverting to $(basename "$prev")" >&2
        ln -sfn "$prev" "$ROOT/current"
        sed -i '$d' "$ROOT/history"
        systemctl restart hexc
    fi
    exit 1
fi
