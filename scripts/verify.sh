#!/usr/bin/env bash
# Pre-launch check: build, test, run the importer (twice, to prove it is repeatable), start the
# nightly worker, smoke-test the API and the daily games, build/test the Angular app, and build
# and smoke-test the Docker image.
# Works on macOS (bash 3.2) and Linux. Writes artifacts/verify.log and artifacts/verify-summary.txt.
#
#   ./scripts/verify.sh               # everything
#   SKIP_WEB=1 ./scripts/verify.sh    # without the Angular app
#   SKIP_DOCKER=1 ./scripts/verify.sh # without the Docker image
#
# Postgres runs in Docker on KNOWBALL_DB_PORT (default 5432); set it if that port is taken.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
mkdir -p artifacts
LOG="$ROOT/artifacts/verify.log"
SUMMARY="$ROOT/artifacts/verify-summary.txt"
: > "$LOG"
: > "$SUMMARY"
exec > >(tee -a "$LOG") 2>&1

export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
# Load nvm if node isn't on PATH (common on macOS).
if ! command -v node >/dev/null 2>&1 && [ -s "$HOME/.nvm/nvm.sh" ]; then
  # shellcheck disable=SC1091
  . "$HOME/.nvm/nvm.sh" >/dev/null 2>&1 && nvm use --lts >/dev/null 2>&1 || nvm use default >/dev/null 2>&1 || true
fi

FAILED=0
result() { # result <PASS|FAIL|SKIP|INFO> <message>
  printf '%-5s %s\n' "$1" "$2" | tee -a "$SUMMARY"
  [ "$1" = "FAIL" ] && FAILED=1
  return 0
}
section() { printf '\n==================== %s ====================\n' "$1"; }

API_PID=""; WORKER_PID=""
cleanup() {
  [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null
  [ -n "$WORKER_PID" ] && kill "$WORKER_PID" 2>/dev/null
  return 0
}
trap cleanup EXIT

section "Toolchain"
date
uname -a
if ! command -v dotnet >/dev/null 2>&1; then
  result FAIL "dotnet SDK not found (install from https://dot.net)"
  exit 1
fi
SDK_VERSION="$(dotnet --version)"
SDK_MAJOR="${SDK_VERSION%%.*}"
TFM_MAJOR="$SDK_MAJOR"
[ "$TFM_MAJOR" -gt 10 ] && TFM_MAJOR=10
if [ "$TFM_MAJOR" -lt 8 ]; then
  result FAIL ".NET SDK $SDK_VERSION is too old; need 8 or newer"
  exit 1
fi
export TriviaTfm="net${TFM_MAJOR}.0"
result INFO ".NET SDK $SDK_VERSION → target $TriviaTfm"
command -v node >/dev/null 2>&1 && result INFO "node $(node -v), npm $(npm -v)" || result INFO "node not found"

section "Restore and build"
if dotnet build KnowBall.sln -c Release; then
  result PASS "dotnet build"
else
  result FAIL "dotnet build"
  exit 1
fi

section "EF Core migrations"
MIGRATIONS=src/KnowBall.Infrastructure/Persistence/Migrations
if ls $MIGRATIONS/*_InitialCreate.cs $MIGRATIONS/*_VersionOne.cs >/dev/null 2>&1; then
  result PASS "InitialCreate and VersionOne migrations present"
else
  result FAIL "InitialCreate or VersionOne migration missing from $MIGRATIONS"
fi

section "Unit tests"
if dotnet test tests/KnowBall.UnitTests -c Release --no-build --logger "trx;LogFileName=unit.trx" --results-directory artifacts/test-results; then
  result PASS "unit tests"
else
  result FAIL "unit tests"
fi

section "Integration tests (SQLite)"
if dotnet test tests/KnowBall.IntegrationTests -c Release --no-build --logger "trx;LogFileName=integration-sqlite.trx" --results-directory artifacts/test-results; then
  result PASS "integration tests on SQLite"
else
  result FAIL "integration tests on SQLite"
fi

section "Database"
DB_MODE="sqlite"
DB_PORT="${KNOWBALL_DB_PORT:-5432}"
PG_CS="Host=localhost;Port=$DB_PORT;Database=sports_trivia;Username=trivia;Password=trivia"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  if docker compose up -d db; then
    for _ in $(seq 1 40); do
      if docker compose exec -T db pg_isready -U trivia -d sports_trivia >/dev/null 2>&1; then DB_MODE="postgres"; break; fi
      sleep 1
    done
  fi
fi
if [ "$DB_MODE" = "postgres" ]; then
  export Database__Provider=Postgres
  export ConnectionStrings__Trivia="$PG_CS"
  result INFO "using Postgres in Docker on port $DB_PORT"

  section "Integration tests (Postgres)"
  # A separate database so tests don't touch dev data.
  docker compose exec -T db psql -U trivia -d sports_trivia -c "DROP DATABASE IF EXISTS sports_trivia_test" >/dev/null 2>&1
  docker compose exec -T db psql -U trivia -d sports_trivia -c "CREATE DATABASE sports_trivia_test" >/dev/null 2>&1
  if TRIVIA_TEST_POSTGRES="Host=localhost;Port=$DB_PORT;Database=sports_trivia_test;Username=trivia;Password=trivia" \
     dotnet test tests/KnowBall.IntegrationTests -c Release --no-build --logger "trx;LogFileName=integration-pg.trx" --results-directory artifacts/test-results; then
    result PASS "integration tests on Postgres"
  else
    result FAIL "integration tests on Postgres"
  fi
else
  export Database__Provider=Sqlite
  export ConnectionStrings__Trivia="Data Source=$ROOT/artifacts/trivia-dev.db"
  result INFO "Docker not available; using SQLite at artifacts/trivia-dev.db (Postgres is covered in CI)"
fi

# --no-history everywhere: the full Basketball-Reference history is a long download, and the
# nightly worker loads it on the real host.
export Import__HistoryRefreshDays=0
IMPORTER="src/KnowBall.Importer/bin/Release/$TriviaTfm/KnowBall.Importer.dll"
API="src/KnowBall.Api/bin/Release/$TriviaTfm/KnowBall.Api.dll"

section "Import: live ESPN rosters"
LIVE_OK=0
if Import__MaxAttempts=2 Import__RetryDelaySeconds=5 dotnet "$IMPORTER" --once --source espn --no-history; then
  result PASS "live import from ESPN"
  LIVE_OK=1
else
  result INFO "live import from ESPN failed (see log); falling back to seed data for the rest of the run"
fi

if [ "$LIVE_OK" = "0" ]; then
  section "Import: seed data"
  if dotnet "$IMPORTER" --once --source seed --no-history; then
    result PASS "seed import"
  else
    result FAIL "seed import"
  fi
fi

SOURCE="seed"; [ "$LIVE_OK" = "1" ] && SOURCE="espn"
section "Import: second run must be a no-op"
SECOND="$(Import__MaxAttempts=1 dotnet "$IMPORTER" --once --source "$SOURCE" --no-history 2>&1)"
echo "$SECOND"
if echo "$SECOND" | grep -q "Succeeded: .* 0 new, 0 updated, 0 deactivated, 0 team changes"; then
  result PASS "re-running the import changes nothing (idempotent)"
else
  result FAIL "second import was not a no-op"
fi

section "Nightly worker"
WORKER_LOG="$ROOT/artifacts/worker.log"
Import__Source="$SOURCE" Import__RunOnStartup=true dotnet "$IMPORTER" > "$WORKER_LOG" 2>&1 &
WORKER_PID=$!
for _ in $(seq 1 90); do
  grep -q "Next import at" "$WORKER_LOG" 2>/dev/null && break
  sleep 1
done
kill "$WORKER_PID" 2>/dev/null; wait "$WORKER_PID" 2>/dev/null; WORKER_PID=""
cat "$WORKER_LOG"
if grep -q "succeeded" "$WORKER_LOG" && grep -q "Next import at" "$WORKER_LOG"; then
  result PASS "worker ran an import on startup and scheduled the next nightly run ($(grep -o 'Next import at [^(]*' "$WORKER_LOG" | tail -1))"
else
  result FAIL "worker did not run and schedule an import"
fi

section "API smoke test"
API_LOG="$ROOT/artifacts/api.log"
ASPNETCORE_URLS="http://localhost:5080" ASPNETCORE_ENVIRONMENT=Development dotnet "$API" > "$API_LOG" 2>&1 &
API_PID=$!
for _ in $(seq 1 60); do
  curl -fs http://localhost:5080/health >/dev/null 2>&1 && break
  sleep 1
done
SEARCH="$(curl -fs 'http://localhost:5080/api/players/search?q=jokic' || true)"
LATEST="$(curl -fs 'http://localhost:5080/api/import-runs/latest' || true)"
SUMMARY_JSON="$(curl -fs 'http://localhost:5080/api/stats/summary' || true)"
echo "search: $SEARCH"; echo "latest: $LATEST"; echo "summary: $SUMMARY_JSON"
echo "$SEARCH" | grep -q 'Joki' && result PASS "GET /api/players/search?q=jokic → $(echo "$SEARCH" | grep -o '"fullName":"[^"]*"' | head -1)" || result FAIL "player search"
echo "$LATEST" | grep -q '"status":"Succeeded"' && result PASS "GET /api/import-runs/latest reports Succeeded" || result FAIL "latest import run endpoint"
[ -n "$SUMMARY_JSON" ] && result INFO "data summary: $SUMMARY_JSON"

# Spordle: a new game must not carry its answer, and a nonsense word must be refused.
GAME="$(curl -fs -X POST -H 'Content-Type: application/json' -d '{"wordLength":5}' http://localhost:5080/api/spordle/games || true)"
GAME_ID="$(echo "$GAME" | grep -o '"gameId":"[^"]*"' | cut -d'"' -f4)"
BAD_GUESS="$(curl -fs -X POST -H 'Content-Type: application/json' -d '{"word":"zzzzz"}' "http://localhost:5080/api/spordle/games/$GAME_ID/guesses" || true)"
echo "spordle game: $GAME"; echo "spordle guess: $BAD_GUESS"
if [ -n "$GAME_ID" ] && ! echo "$GAME" | grep -qi 'answer' && echo "$BAD_GUESS" | grep -q '"valid":false'; then
  result PASS "Spordle: a game starts without its answer and refuses a nonsense word"
else
  result FAIL "Spordle endpoints"
fi

# The daily games: today's puzzle must come without its answer, and the admin area must refuse
# a request without the key (404 when no key is set, 401 with a wrong one).
ANON="verify-$$"
DAILY="$(curl -fs -H "X-Anon-Id: $ANON" http://localhost:5080/api/scouting-report/daily || true)"
echo "scouting report daily: $DAILY"
if echo "$DAILY" | grep -q '"puzzleId"' && echo "$DAILY" | grep -q '"answer":null'; then
  result PASS "GET /api/scouting-report/daily returns today's puzzle without the answer"
else
  result FAIL "GET /api/scouting-report/daily"
fi
curl -fs -H "X-Anon-Id: $ANON" http://localhost:5080/api/me/today >/dev/null && result PASS "GET /api/me/today" || result FAIL "GET /api/me/today"
ADMIN_CODE="$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5080/api/admin/overview)"
case "$ADMIN_CODE" in
  401|404) result PASS "GET /api/admin/overview refused without the key ($ADMIN_CODE)" ;;
  *)       result FAIL "GET /api/admin/overview without the key gave $ADMIN_CODE" ;;
esac
kill "$API_PID" 2>/dev/null; wait "$API_PID" 2>/dev/null; API_PID=""

if [ "${SKIP_WEB:-0}" != "1" ]; then
  section "Angular"
  if command -v npm >/dev/null 2>&1; then
    (
      cd web/know-ball || exit 1
      npm install --no-audit --no-fund || exit 10
      npm test || exit 11
      npm run build || exit 12
    )
    case $? in
      0)  result PASS "Angular install, unit tests and production build" ;;
      10) result FAIL "npm install" ;;
      11) result FAIL "Angular unit tests" ;;
      12) result FAIL "Angular build" ;;
      *)  result FAIL "Angular step" ;;
    esac
  else
    result FAIL "npm not found"
  fi
fi

if [ "${SKIP_DOCKER:-0}" != "1" ]; then
  section "Docker image"
  if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    if ./scripts/docker-smoke.sh; then
      result PASS "Docker image builds and passes its smoke test"
    else
      result FAIL "Docker image smoke test"
    fi
  else
    result INFO "Docker not available; image not checked (CI checks it)"
  fi
fi

section "Summary"
cat "$SUMMARY"
if [ "$FAILED" = "0" ]; then
  echo "GATE: PASS" | tee -a "$SUMMARY"
else
  echo "GATE: FAIL" | tee -a "$SUMMARY"
fi
exit "$FAILED"
