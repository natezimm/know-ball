#!/usr/bin/env bash
# Builds the Docker image and checks that it works as deployed: the site starts on an empty
# Postgres database and applies its migrations, the importer in the same image loads data and
# queues puzzles, and the daily games answer without leaking answers on the Eastern calendar.
#
#   ./scripts/docker-smoke.sh               # build, test, then remove the containers and volumes
#   SKIP_BUILD=1 ./scripts/docker-smoke.sh  # use an image already tagged know-ball
#   KEEP=1 ./scripts/docker-smoke.sh        # leave it running afterwards
#
# Runs as its own compose project on ports 18090 and 15433 (SMOKE_PORT, SMOKE_DB_PORT), so it
# leaves a dev setup and other containers alone.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-knowball-smoke}"
export KNOWBALL_PORT="${SMOKE_PORT:-18090}"
export KNOWBALL_DB_PORT="${SMOKE_DB_PORT:-15433}"
BASE="http://localhost:$KNOWBALL_PORT"
ANON="smoke-test-player"
WORK="$(mktemp -d)"

FAILED=0
pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1"; FAILED=1; }
compose() { docker compose --profile site "$@"; }

cleanup() {
  if [ "$FAILED" != "0" ]; then
    echo "---- site logs ----"
    compose logs --no-color --tail 80 site
  fi
  [ "${KEEP:-0}" = "1" ] || compose down -v --remove-orphans >/dev/null 2>&1
  rm -rf "$WORK"
}
trap cleanup EXIT

if [ "${SKIP_BUILD:-0}" = "1" ]; then BUILD="--no-build"; else BUILD="--build"; fi
if ! compose up -d $BUILD db site; then
  fail "docker compose up"
  exit 1
fi

for _ in $(seq 1 90); do
  curl -fs "$BASE/health" >/dev/null 2>&1 && break
  sleep 1
done
if curl -fs "$BASE/health" >/dev/null; then
  pass "site started on an empty database and applied its migrations"
else
  fail "site did not become healthy"
  exit 1
fi

curl -fs "$BASE/" | grep -qi '<app-root' && pass "GET / serves the Angular app" || fail "GET / does not serve the Angular app"

# The importer runs from the same image; seed data keeps this offline and quick.
IMPORT="$(compose run --rm -T importer --once --source seed --no-history 2>&1)"
echo "$IMPORT" | tail -5
if echo "$IMPORT" | grep -q "Succeeded"; then pass "importer loaded seed data"; else fail "importer"; fi
echo "$IMPORT" | grep -q "Queued .* Scouting Report puzzles" && pass "importer queued puzzles" || fail "importer queued no puzzles"

DAILY="$(curl -fs -H "X-Anon-Id: $ANON" "$BASE/api/scouting-report/daily" || true)"
if echo "$DAILY" | grep -q '"puzzleId"' && echo "$DAILY" | grep -q '"answer":null'; then
  pass "GET /api/scouting-report/daily answers without the answer"
else
  fail "GET /api/scouting-report/daily: $DAILY"
fi
curl -fs -H "X-Anon-Id: $ANON" "$BASE/api/me/today" >/dev/null && pass "GET /api/me/today" || fail "GET /api/me/today"

# The retired photo API must remain unavailable even if an old key is still configured.
CODE="$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/whos-that/daily")"
[ "$CODE" = "404" ] && pass "retired photo API is absent" || fail "retired photo API gave $CODE"

SITE_TODAY="$(echo "$DAILY" | grep -o '"date":"[0-9-]*"' | cut -d'"' -f4)"
EASTERN_TODAY="$(TZ=America/New_York date +%F)"
[ "$SITE_TODAY" = "$EASTERN_TODAY" ] && pass "daily calendar uses Eastern time" || fail "daily date is '$SITE_TODAY', expected '$EASTERN_TODAY'"

[ "$FAILED" = "0" ] && echo "DOCKER SMOKE: PASS" || echo "DOCKER SMOKE: FAIL"
exit "$FAILED"
