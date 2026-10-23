# Know Ball

Daily sports trivia games: Angular front end, ASP.NET Core API, PostgreSQL. A personal project.

Version 1 contains three games: Spordle, Scouting Report and Career Path. The site has been live on GCP since
2026-10-16 at [knowball.nathanzimmerman.com](https://knowball.nathanzimmerman.com).

Who's That Sportsman? was removed from the active project on 2026-10-20 after the photo/cutout review.
It remains a [future enhancement](docs/future-games.md). **Numbers Don't Lie**, a cross-sport statistics quiz,
is the proposed fourth game; its [implementation plan](docs/numbers-dont-lie-implementation-plan.md)
defines the recommended rules, data proof and release gates. A [playable preview](docs/prototypes/numbers-dont-lie/README.md)
and [24-question source review](docs/numbers-dont-lie-data-sources.md) now support the first review gate.
The footer links to the hosted review sample at `/preview/numbers-dont-lie/index.html?ngsw-bypass=true`.
It supports daily-preview and practice rounds; reloading resets progress. The full daily game is not launched.

The remaining launch gates are production nightly-import evidence, backup and restore verification, real-phone
and installed-PWA checks. See [the launch checklist](docs/implementation-plan.md#8-whats-left).

| Game | Route | How it plays | State |
| --- | --- | --- | --- |
| Spordle | `/spordle` | A sports word of 4, 5 or 6 letters in six tries, like Wordle | Playable |
| Scouting Report | `/scouting-report` | Guess today's player in eight tries; each guess is graded on team, conference, division, position, height, age and number | Playable |
| Career Path | `/career-path` | Every team a player has been on, in order; six tries, a hint per miss | Playable |

The two player games share one engine: a daily puzzle that changes at midnight Eastern, practice rounds,
an archive of past dailies, per-game stats and streaks, and a share card. Players are anonymous: the
browser makes up an id and sends it as `X-Anon-Id`. The answer never leaves the server until a game is over.

## What's here

```
src/
  KnowBall.Domain/          entities: League, Team, Player, PlayerExternalId, PlayerTeamStint, PlayerSeasonStat, Puzzle…
  KnowBall.Application/     Import/  history, crosswalk and roster imports, player matching, popularity
                                Games/   the engine (GameService, PuzzleScheduler, PuzzleCalendar) and one folder per game
  KnowBall.Infrastructure/  EF Core (Postgres or SQLite), data sources
  KnowBall.Api/             minimal API endpoints; also serves the built Angular app when wwwroot holds it
  KnowBall.Importer/        worker: history every 30 days, rosters and puzzle queues nightly at 04:00 Eastern
tests/
  KnowBall.UnitTests/       comparers, generators, scheduler, importers, parsers (fixture CSVs in tests/fixtures)
  KnowBall.IntegrationTests/ boots the API on SQLite or Postgres and plays the games through HTTP
web/know-ball/              Angular 21 app: core/ (API, store, share), shared/ (GameShell, player search), games/, pages/
docs/                           implementation plan and data source research
```

## Run it locally

Prerequisites: .NET 10 SDK, Node 22, Docker (optional).

```bash
docker compose up -d db                                    # Postgres on :5432 (KNOWBALL_DB_PORT to change)
dotnet run --project src/KnowBall.Importer -- --once   # full history, then today's rosters (about a minute)
dotnet run --project src/KnowBall.Api                  # http://localhost:5080
cd web/know-ball && npm install && npm start           # http://localhost:4200
```

No Docker? Use SQLite for both the importer and the API:

```bash
export Database__Provider=Sqlite ConnectionStrings__Trivia="Data Source=$PWD/artifacts/trivia-dev.db"
```

A SQLite file made by an older version of the schema is refused with a message; delete it and import again.

Tests:

```bash
dotnet test tests/KnowBall.UnitTests
dotnet test tests/KnowBall.IntegrationTests       # SQLite; set TRIVIA_TEST_POSTGRES to a connection string for Postgres
cd web/know-ball && npm test
cd web/know-ball && npm run e2e                    # Playwright: every game played in Chromium against the real API
```

The end-to-end tests start their own site (`web/know-ball/e2e/serve.sh`) on port 5180 with a fresh SQLite
database loaded offline from the test fixtures and seed rosters, so they need the .NET SDK too. They win and lose
each game: the player games learn today's answer by playing it through the API first, and Spordle solves its
random word from the hints and scores. Run `npx playwright install chromium` once before the first run.

`./scripts/verify.sh` runs everything above plus the importer, an API smoke test and the Docker image check
(`./scripts/docker-smoke.sh`, which CI also runs). Set `KNOWBALL_DB_PORT` if port 5432 is taken.

## The importer

```bash
dotnet run --project src/KnowBall.Importer -- --once                          # one run, then exit
dotnet run --project src/KnowBall.Importer -- --once --history                # reload the full history now, due or not
dotnet run --project src/KnowBall.Importer -- --once --source seed --no-history  # offline: ~36 bundled players
dotnet run --project src/KnowBall.Importer                                    # the nightly worker
```

Each run: reload the player history if it is due, then today's rosters, then top up every game's queue of daily
puzzles and delete old practice rounds.

| Source | Gives | When |
| --- | --- | --- |
| Basketball-Reference CSV mirror (GitHub) | Every player since 1947: bio, season stats, awards, team history | Every `Import:HistoryRefreshDays` (30) |
| Wikidata | NBA.com ids (for headshots) and birthplaces | Right after the history |
| ESPN roster feed | Current team, jersey number, new players | Nightly |

| Setting | Default | Meaning |
| --- | --- | --- |
| `Import:Source` | `espn` | `espn` (live), `seed` (bundled sample) or `nba-stats` (stats.nba.com; not responding since October 2026) |
| `Import:FallbackSource` | empty | source to try if the primary fails |
| `Import:DailyAt` / `Import:TimeZone` | `04:00` / `America/New_York` | nightly run time, safe across daylight saving changes |
| `Import:RunOnStartup` | `true` | also run when the worker starts |
| `Import:MaxAttempts` / `Import:RetryDelaySeconds` | `3` / `60` | retries before giving up for the night |
| `Import:HistoryRefreshDays` | `30` | 0 = only with `--history` |
| `Import:PuzzleDaysAhead` | `30` | days of daily puzzles kept queued per generated game |
| `Import:PracticeRetentionDays` | `7` | practice rounds older than this are deleted |
| `Sources:Bref:Directory` | empty | read the CSVs from a folder instead of GitHub |

Every run writes an `ImportRun` row; `GET /api/import-runs/latest` is the monitoring hook. Running twice with the
same data changes nothing, a trade closes one team stint and opens the next, and a roster feed that suddenly
returns under half the active players fails the run instead of retiring the league.

## API

| Endpoint | Purpose |
| --- | --- |
| `GET /api/{game}/daily` | today's puzzle and this player's progress (`game` is `scouting-report` or `career-path`) |
| `GET /api/{game}/puzzles/{id}` | a past daily or a practice round |
| `POST /api/{game}/{id}/guess` | `{ "playerId": 123 }` |
| `POST /api/{game}/practice` | a new practice round |
| `GET /api/{game}/archive` | past dailies with this player's result on each |
| `GET /api/me/stats`, `GET /api/me/today` | stats per game; today's state for the home page |
| `GET /api/players/search?q=&includeInactive=` | autocomplete |
| `POST /api/spordle/games`, `.../{id}/guesses`, `.../{id}/hint` | Spordle (database-backed rounds; six-hour idle expiry) |
| `GET /api/import-runs/latest`, `GET /api/stats/summary`, `GET /health` | monitoring |

The game endpoints need an `X-Anon-Id` header. Guesses are limited per address: `Games:GuessesPerMinute` (60),
`Spordle:GuessesPerMinute` (20) and `Spordle:GamesPerMinute` (30).

Spordle stores rounds in the existing database, retaining attempts and hints across API restarts.
Guess bodies accept an optional UUID `requestId`: retry the same word with the same ID to receive its
original result without spending another attempt. Reusing the ID for a different word returns 409.
Older clients can omit it. Rounds expire after six idle hours; new rounds and the nightly importer
delete expired rows. Expired rounds offer a fresh game without recording a loss. The server session
is persisted; restoring the displayed board after a browser reload is not implemented.

## Docker

One image holds the API and the built Angular app; the same image runs the importer.

```bash
docker compose --profile site up -d --build      # site on http://localhost:8080
```

`KNOWBALL_PORT` and `KNOWBALL_DB_PORT` change the published ports. The legacy `photos` volume is retained for backup compatibility; active games do not upload or serve images. The image
runs as the non-root `app` user; `./scripts/docker-smoke.sh` builds it and checks it end to end, as CI does.

Production runs on a GCP VM behind nginx at https://knowball.nathanzimmerman.com, deployed by CI from `main`;
see [deploy/README.md](deploy/README.md).

## Database migrations

Postgres uses EF Core migrations, applied when the API starts (`InitialCreate`, `VersionOne`, then
`PersistSpordleSessions`). The latest migration adds only the sessions table and expiry index; player
and daily-game data is unchanged. The first deployment cannot recover previous in-memory rounds.
After changing
entities:

```bash
dotnet tool install dotnet-ef --tool-path .tools
.tools/dotnet-ef migrations add <Name> --project src/KnowBall.Infrastructure \
  --startup-project src/KnowBall.Infrastructure --output-dir Persistence/Migrations
```

SQLite (tests, quick local runs) uses `EnsureCreated` with a schema version number.
The current schema is version 3; older local SQLite files must be rebuilt and re-imported as directed by
the startup error. Production Postgres is migrated in place.

## Look and feel

The Trading Card theme: cream stock, navy ink, red and mustard trim, with one trim colour per game. Colours and
their roles are CSS variables in `web/know-ball/src/styles.css`; shared pieces use `kb-` classes there. Fonts
(Rokkitt, Alfa Slab One, Yellowtail) come from `@fontsource` packages and are bundled. `/mockups` still holds the
designs for the games not built yet and keeps its own dark palette.

## Data and images

Player data comes from the Basketball-Reference mirror, Wikidata and ESPN's public feed; headshots and logos are
the NBA's, linked from its CDN. This is fine for a personal project and would need revisiting for anything
commercial. Details in [docs/research.md](docs/research.md).

