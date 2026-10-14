# Know Ball

Daily sports trivia games: Angular front end, ASP.NET Core API, PostgreSQL. A personal project.

Version 1 is four NBA games. The code for all four is written; the site is not hosted yet. What is left before
launch is in [docs/implementation-plan.md, section 8](docs/implementation-plan.md#8-whats-left).

| Game | Route | How it plays | State |
| --- | --- | --- | --- |
| Spordle | `/spordle` | A sports word of 4, 5 or 6 letters in six tries, like Wordle | Playable |
| Scouting Report | `/scouting-report` | Guess today's player in eight tries; each guess is graded on team, conference, division, position, height, age and number | Playable |
| Career Path | `/career-path` | Every team a player has been on, in order; six tries, a hint per miss | Playable |
| Who's That Sportsman? | `/whos-that` | A famous photo: silhouette, then year, team and a blurred photo; five tries | Built; waiting for photos |

The three player games share one engine: a daily puzzle that changes at midnight Eastern, practice rounds (not
Who's That), an archive of past dailies, per-game stats and streaks, and a share card. Players are anonymous: the
browser makes up an id and sends it as `X-Anon-Id`. The answer never leaves the server until a game is over.

## What's here

```
src/
  SportsTrivia.Domain/          entities: League, Team, Player, PlayerExternalId, PlayerTeamStint, PlayerSeasonStat, Puzzle…
  SportsTrivia.Application/     Import/  history, crosswalk and roster imports, player matching, popularity
                                Games/   the engine (GameService, PuzzleScheduler, PuzzleCalendar) and one folder per game
  SportsTrivia.Infrastructure/  EF Core (Postgres or SQLite), data sources, image store and silhouette renderer
  SportsTrivia.Api/             minimal API endpoints; also serves the built Angular app when wwwroot holds it
  SportsTrivia.Importer/        worker: history every 30 days, rosters and puzzle queues nightly at 04:00 Eastern
tests/
  SportsTrivia.UnitTests/       comparers, generators, scheduler, importers, parsers (fixture CSVs in tests/fixtures)
  SportsTrivia.IntegrationTests/ boots the API on SQLite or Postgres and plays the games through HTTP
web/sports-trivia/              Angular 21 app: core/ (API, store, share), shared/ (GameShell, player search), games/, pages/
docs/                           implementation plan and data source research
```

## Run it locally

Prerequisites: .NET 10 SDK, Node 22, Docker (optional).

```bash
docker compose up -d db                                    # Postgres on :5432 (KNOWBALL_DB_PORT to change)
dotnet run --project src/SportsTrivia.Importer -- --once   # full history, then today's rosters (about a minute)
dotnet run --project src/SportsTrivia.Api                  # http://localhost:5080
cd web/sports-trivia && npm install && npm start           # http://localhost:4200
```

No Docker? Use SQLite for both the importer and the API:

```bash
export Database__Provider=Sqlite ConnectionStrings__Trivia="Data Source=$PWD/artifacts/trivia-dev.db"
```

A SQLite file made by an older version of the schema is refused with a message; delete it and import again.

Tests:

```bash
dotnet test tests/SportsTrivia.UnitTests
dotnet test tests/SportsTrivia.IntegrationTests       # SQLite; set TRIVIA_TEST_POSTGRES to a connection string for Postgres
cd web/sports-trivia && npm test
```

`./scripts/verify.sh` is the older all-in-one gate check and still needs updating for version 1 (see the plan).

## The importer

```bash
dotnet run --project src/SportsTrivia.Importer -- --once                          # one run, then exit
dotnet run --project src/SportsTrivia.Importer -- --once --history                # reload the full history now, due or not
dotnet run --project src/SportsTrivia.Importer -- --once --source seed --no-history  # offline: ~36 bundled players
dotnet run --project src/SportsTrivia.Importer                                    # the nightly worker
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
| `GET /api/{game}/daily` | today's puzzle and this player's progress (`game` is `scouting-report`, `career-path` or `whos-that`) |
| `GET /api/{game}/puzzles/{id}` | a past daily or a practice round |
| `POST /api/{game}/{id}/guess` | `{ "playerId": 123 }` |
| `POST /api/{game}/practice` | a new practice round (not Who's That) |
| `GET /api/{game}/archive` | past dailies with this player's result on each |
| `GET /api/me/stats`, `GET /api/me/today` | stats per game; today's state for the home page |
| `GET /api/players/search?q=&includeInactive=` | autocomplete |
| `POST /api/spordle/games`, `.../{id}/guesses`, `.../{id}/hint` | Spordle (games held in memory for six idle hours) |
| `GET /api/import-runs/latest`, `GET /api/stats/summary`, `GET /health` | monitoring |
| `/api/admin/...` | admin area, below |

The game endpoints need an `X-Anon-Id` header. Guesses are limited per address: `Games:GuessesPerMinute` (60),
`Spordle:GuessesPerMinute` (20) and `Spordle:GamesPerMinute` (30).

## Admin: Who's That Sportsman? puzzles

`/admin` is off until `Admin:Key` is set (environment variable `Admin__Key`, a long random string); with no key
every admin endpoint answers 404. The page asks for the key once per browser tab.

To add a puzzle: upload the photo and a PNG cut-out of the player (on a Mac, Preview → Tools → Remove Background),
pick the player, and enter the year, team and caption. The server makes the black silhouette and the blurred
version and stores all three under random file names in `Media:Path`, served from `/photos`. Puzzles start as
drafts; schedule them, reorder them, or move them back to drafts. If the queue runs dry, the oldest puzzle is
replayed rather than showing nothing.

In development the `api` entry in `.claude/launch.json` reads the key from `artifacts/dev-admin-key` and creates
that file if it is missing. The `artifacts/` folder is not committed.

## Docker

One image holds the API and the built Angular app; the same image runs the importer.

```bash
echo "KNOWBALL_ADMIN_KEY=$(openssl rand -hex 24)" > .env
docker compose --profile site up -d --build      # site on http://localhost:8080
```

`KNOWBALL_PORT` and `KNOWBALL_DB_PORT` change the published ports. Photos live on the `photos` volume. The image
has not been built yet; the checks it still needs are in the plan's section 8.

## Database migrations

Postgres uses EF Core migrations, applied when the API starts (`InitialCreate`, then `VersionOne`). After changing
entities:

```bash
dotnet tool install dotnet-ef --tool-path .tools
.tools/dotnet-ef migrations add <Name> --project src/SportsTrivia.Infrastructure \
  --startup-project src/SportsTrivia.Infrastructure --output-dir Persistence/Migrations
```

SQLite (tests, quick local runs) uses `EnsureCreated` with a schema version number.

## Look and feel

The Trading Card theme: cream stock, navy ink, red and mustard trim, with one trim colour per game. Colours and
their roles are CSS variables in `web/sports-trivia/src/styles.css`; shared pieces use `kb-` classes there. Fonts
(Rokkitt, Alfa Slab One, Yellowtail) come from `@fontsource` packages and are bundled. `/mockups` still holds the
designs for the games not built yet and keeps its own dark palette.

## Data and images

Player data comes from the Basketball-Reference mirror, Wikidata and ESPN's public feed; headshots and logos are
the NBA's, linked from its CDN. This is fine for a personal project and would need revisiting for anything
commercial. Details in [docs/research.md](docs/research.md).
