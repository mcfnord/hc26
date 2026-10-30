#!/bin/bash
# revert.sh — roll the live site back to a previous release.
#
#   ./deploy/revert.sh              # back one release, restoring the DB as it was then
#   ./deploy/revert.sh 2            # back two releases
#   ./deploy/revert.sh --keep-db    # roll code back but keep the current database
#
# The current DB is always copied to backups/pre-revert-<time>.db first, so a revert
# never destroys data. See deploy.sh for the layout.
set -euo pipefail
ROOT=/opt/hexc
DATA=/var/lib/hexc
DB="$DATA/hexc.db"

n=1; keep=0
for a in "$@"; do
    case "$a" in --keep-db) keep=1 ;; *) n="$a" ;; esac
done

lines=$(wc -l < "$ROOT/history")
if (( lines <= n )); then
    echo "Only $lines release(s) in history; cannot go back $n." >&2; exit 1
fi

from=$(tail -n1 "$ROOT/history")
# The release being abandoned; its backup holds the DB from just before it went live.
abandoned=$(tail -n "$n" "$ROOT/history" | head -n1)
to=$(tail -n "$((n+1))" "$ROOT/history" | head -n1)

echo "==> Reverting $from -> $to"
if [[ -f "$DB" ]]; then
    # .backup, not cp: in WAL mode a plain copy can miss recent rows (see deploy.sh).
    sqlite3 "$DB" ".backup '$ROOT/backups/pre-revert-$(date +%Y%m%d-%H%M%S).db'"
    if (( keep == 0 )) && [[ -f "$ROOT/backups/$abandoned.db" ]]; then
        echo "==> Restoring DB from backups/$abandoned.db"
        # Stop first and drop the WAL files: SQLite would otherwise replay the old WAL
        # onto the restored file and corrupt it.
        systemctl stop hexc
        cp "$ROOT/backups/$abandoned.db" "$DB"
        rm -f "$DB-wal" "$DB-shm"
        chown hexc:hexc "$DB"
    fi
fi

ln -sfn "$ROOT/releases/$to" "$ROOT/current"
head -n "-$n" "$ROOT/history" > "$ROOT/history.tmp" && mv "$ROOT/history.tmp" "$ROOT/history"
systemctl restart hexc

for i in $(seq 1 30); do
    if curl -sf http://127.0.0.1:5235/healthz | grep -q "\"release\":\"$to\""; then
        echo "==> LIVE: $to (reverted from $from)  $(date -u +%FT%TZ)" | tee -a "$ROOT/deploys.log"
        exit 0
    fi
    sleep 1
done
echo "!!! $to did not come up healthy" >&2; exit 1
