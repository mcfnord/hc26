#!/bin/bash
# update.sh — the test box's self-update, run every 2 minutes by hexc-update.timer.
#
# Fetches the branch named in /etc/hexc/testbox.env. If its head is a commit this box has
# not tried yet, checks it out and runs deploy.sh (which smoke-tests and reverts itself on
# failure). Each commit is tried once: a failing commit is not retried every 2 minutes, and
# a manual `hexc-revert` stays in place until the next push.
#
# The whole body is in braces so bash has read all of it before git replaces this file.
{
set -euo pipefail
source /etc/hexc/testbox.env      # BRANCH, SRC
ROOT=/opt/hexc

exec 9>/run/hexc-update.lock
flock -n 9 || { echo "Another update is still running; skipping this check."; exit 0; }

cd "$SRC"
git fetch -q origin "$BRANCH"
new=$(git rev-parse --short FETCH_HEAD)
seen=$(cat "$ROOT/last-seen" 2>/dev/null || true)
[[ "$new" == "$seen" ]] && exit 0

echo "==> New commit on $BRANCH: $new ($(git log -1 --format=%s FETCH_HEAD))"
echo "$new" > "$ROOT/last-seen"
git reset -q --hard FETCH_HEAD
"$SRC/deploy/deploy.sh" 9>&- || echo "!!! Deploy of $new failed; the previous release is still live. Run hexc-status."

# Keep the disk from filling: the 10 newest releases and 20 newest DB backups.
keep=$(tail -n 10 "$ROOT/history" 2>/dev/null || true)
for d in "$ROOT"/releases/*/; do
    name=$(basename "$d")
    grep -qxF "$name" <<<"$keep" || rm -rf "$d"
done
ls -1t "$ROOT"/backups/*.db 2>/dev/null | tail -n +21 | xargs -r rm -f
exit 0
}
