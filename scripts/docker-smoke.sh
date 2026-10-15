#!/usr/bin/env bash
# Builds the Docker image and checks that it works as deployed: the site starts on an empty
# Postgres database and applies its migrations, the importer in the same image loads data and
# queues puzzles, the daily games answer without leaking answers, the admin area needs its key,
# the daily calendar is on Eastern time, and SkiaSharp can render a puzzle whose photos are
# written to the /data volume by the non-root user the image runs as.
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
export KNOWBALL_ADMIN_KEY="smoke-$(date +%s)-$RANDOM"
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

CODE="$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/admin/overview")"
[ "$CODE" = "401" ] && pass "admin refuses a request without the key" || fail "admin without key gave $CODE, expected 401"

OVERVIEW="$(curl -fs -H "X-Admin-Key: $KNOWBALL_ADMIN_KEY" "$BASE/api/admin/overview" || true)"
SITE_TODAY="$(echo "$OVERVIEW" | grep -o '"today":"[0-9-]*"' | cut -d'"' -f4)"
EASTERN_TODAY="$(TZ=America/New_York date +%F)"
if [ "$SITE_TODAY" = "$EASTERN_TODAY" ]; then
  pass "the daily calendar is on Eastern time ($SITE_TODAY)"
else
  fail "site says today is '$SITE_TODAY', Eastern says $EASTERN_TODAY"
fi

# A Who's That Sportsman? puzzle: a photo and a cut-out with a clear background.
python3 - "$WORK" <<'PY'
import struct, sys, zlib
def png(path, clear):
    w = h = 200
    rows = b""
    for y in range(h):
        rows += b"\x00"
        for x in range(w):
            inside = 60 <= x < 140 and 30 <= y < 180
            rows += bytes((220, 80, 40, 255)) if inside else (bytes((0, 0, 0, 0)) if clear else bytes((70, 130, 180, 255)))
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))
png(sys.argv[1] + "/photo.png", False)
png(sys.argv[1] + "/cutout.png", True)
PY
PLAYER_ID="$(curl -fs "$BASE/api/players/search?q=lebron" | grep -o '"id":[0-9]*' | head -1 | cut -d: -f2)"
CREATED="$(curl -fs -H "X-Admin-Key: $KNOWBALL_ADMIN_KEY" \
  -F "photo=@$WORK/photo.png;type=image/png" -F "cutout=@$WORK/cutout.png;type=image/png" \
  -F "playerId=$PLAYER_ID" -F "year=2016" -F "team=Cleveland Cavaliers" -F "caption=Smoke test" \
  "$BASE/api/admin/whos-that/puzzles" || true)"
SILHOUETTE="$(echo "$CREATED" | grep -o '"silhouetteUrl":"[^"]*"' | cut -d'"' -f4)"
if [ -n "$SILHOUETTE" ]; then
  pass "admin created a puzzle (SkiaSharp rendered it)"
else
  fail "admin could not create a puzzle: $CREATED"
fi
if [ -n "$SILHOUETTE" ] && compose exec -T site test -s "/data/photos/$(basename "$SILHOUETTE")"; then
  pass "photos were written to the /data volume"
else
  fail "no photo file under /data/photos"
fi
if [ -n "$SILHOUETTE" ] && curl -fs -o /dev/null "$BASE$SILHOUETTE"; then
  pass "GET $SILHOUETTE serves the silhouette"
else
  fail "silhouette is not served"
fi

[ "$FAILED" = "0" ] && echo "DOCKER SMOKE: PASS" || echo "DOCKER SMOKE: FAIL"
exit "$FAILED"
