#!/usr/bin/env bash
# Starts the site for the Playwright tests, the way the Docker image runs it: one API process that
# also serves the built Angular app. Each start gets a fresh SQLite database under artifacts/e2e,
# loaded without the network: player history from the test fixture CSVs, the bundled seed rosters,
# and the daily puzzles queued from them.
#
# Playwright runs this itself (playwright.config.ts). Build the Angular app first (npm run build);
# set E2E_SKIP_DOTNET_BUILD=1 to use API and importer builds that are already there.
set -euo pipefail

WEB="$(cd "$(dirname "$0")/.." && pwd)"
ROOT="$(cd "$WEB/../.." && pwd)"
TFM="${TriviaTfm:-net10.0}"
SITE="$WEB/dist/know-ball/browser"
WORK="$ROOT/artifacts/e2e"

if [ ! -f "$SITE/index.html" ]; then
  echo "No Angular build in $SITE; run npm run build first." >&2
  exit 1
fi

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 TriviaTfm="$TFM"
if [ "${E2E_SKIP_DOTNET_BUILD:-0}" != "1" ]; then
  dotnet build "$ROOT/src/KnowBall.Api" -c Release
  dotnet build "$ROOT/src/KnowBall.Importer" -c Release
fi

rm -rf "$WORK"
mkdir -p "$WORK"
export Database__Provider=Sqlite
export ConnectionStrings__Trivia="Data Source=$WORK/trivia.db"

HISTORY="$ROOT/tests/fixtures/bref"

# Career Path needs the history, read here from the fixtures. Wikidata is pointed at a closed port,
# so that step fails at once and the import stays offline.
Sources__Bref__Directory="$HISTORY" Sources__Wikidata__Endpoint="http://127.0.0.1:9/sparql" \
  dotnet "$ROOT/src/KnowBall.Importer/bin/Release/$TFM/KnowBall.Importer.dll" --once --source seed --history

# The tests play many rounds from one address, so the per-address limits are raised.
export ASPNETCORE_URLS="http://127.0.0.1:${E2E_PORT:-5180}"
export ASPNETCORE_WEBROOT="$SITE"
export Games__GuessesPerMinute=10000
export Spordle__GuessesPerMinute=10000
export Spordle__GamesPerMinute=10000
exec dotnet "$ROOT/src/KnowBall.Api/bin/Release/$TFM/KnowBall.Api.dll"
