#!/usr/bin/env bash
# Serialize image pulls and container replacement with backups and storage cleanup.
set -euo pipefail
[[ "${1:-}" =~ ^[0-9a-f]{40}$ ]] || { echo 'Expected a full commit SHA' >&2; exit 1; }
cd "${KNOWBALL_ROOT:-$HOME/know-ball}"
exec 9>.operations.lock
flock -w 600 9
sed -i '/^KNOWBALL_TAG=/d' .env
printf 'KNOWBALL_TAG=%s\n' "$1" >> .env
docker compose pull -q
docker compose up -d --remove-orphans
