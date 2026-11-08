# The importer

`src/KnowBall.Importer` is a worker that keeps the player data and the puzzle queues fresh. The same Docker image
runs it beside the API.

```bash
dotnet run --project src/KnowBall.Importer -- --once                             # one run, then exit
dotnet run --project src/KnowBall.Importer -- --once --history                   # reload the full history now, due or not
dotnet run --project src/KnowBall.Importer -- --once --source seed --no-history --leagues nba  # offline: ~36 bundled players
dotnet run --project src/KnowBall.Importer -- --once --leagues nfl --history     # just the NFL, history included
dotnet run --project src/KnowBall.Importer -- --nightly                          # one night's work (import, then the Numbers queue), then exit
dotnet run --project src/KnowBall.Importer                                       # the nightly worker
```

In production (`deploy/docker-compose.yml`) the importer container runs `run-nightly.sh` instead of the worker: it
runs `--nightly` once on start and then at `Import:DailyAt` in `Import:TimeZone`, each time as a short-lived
process, so no .NET process sits idle on the shared VM between imports. It logs `Next import at ...` like the
worker does.

## What a run does

For each league in `Import:Leagues` (NBA first, then NFL; one failing never stops the other):

1. Reload the player history if it is due (every `Import:HistoryRefreshDays`). For the NBA, then link each player
   to their NBA.com id and birthplace through Wikidata.
2. Load today's rosters, with retries and (NBA only) an optional fallback source.

Then:

3. Top up every generated game's queue of daily puzzles, then delete old practice rounds, expired Letter Jacket
   rounds and Numbers Don't Lie practice past its 30-day expiry. This happens even after a failed roster import, because yesterday's rosters still make fair puzzles.

Every run writes an `ImportRun` row; `GET /api/import-runs/latest` is the monitoring hook. Running twice with the
same data changes nothing, a trade closes one team stint and opens the next, and a roster feed that suddenly
returns under half the active players (counted once each) fails the run instead of retiring the league
(`PlayerImportService.MinRetainedFraction`). The ESPN source also fails unless its team list covers all 30 NBA
teams and every roster has players, since a few missing teams would still clear the half-league check. A roster run applies its changes in one save, so a failed run leaves
nothing half-applied and keeps only the failed run record. A history run saves players in batches of 500
(`HistoryImportService.BatchSize`) so the NFL's 12,000-odd players fit on the small VM; one that fails part way
keeps the batches already saved, and the next run carries on from there. `--once` and `--nightly` exit non-zero if
any league's roster run failed.

The [nightly import check](../.github/workflows/nightly-import-check.yml) runs the same import against the live
sources every night on a throwaway SQLite database, so an upstream format change shows up before the production
run hits it.

## Sources

| Source | Gives | When |
| --- | --- | --- |
| Basketball-Reference CSV mirror (GitHub) | Every player since 1947: bio, season stats, awards, team history | Every `Import:HistoryRefreshDays` (30) |
| Wikidata | NBA.com ids (for headshots) and birthplaces | Right after the history |
| ESPN roster feed | Current team, jersey number, new players | Nightly |
| nflverse release files (GitHub) | Every NFL player on a game-day roster since 1990 (two seasons or more, or on a roster now): bio, draft slot, NFL.com headshot, team each season; Pro Bowls, All-Pros and career value for fame | Every `Import:HistoryRefreshDays` (30) |
| nflverse current-season roster | Current team, jersey number, active status, new players | Nightly |

The NFL history reads the season roster file up to 2015 and the weekly roster files from 2016, because from 2016
the season files list each player once and lose mid-season trades. A season counts for a team when the player was
active or inactive on a game day there (weekly files), or ended the season with or was traded from it (season files);
practice squad and released rows do not count. The fame score (`PopularityCalculator.FromFootballCareer`) is
weighted towards quarterbacks and skill players, so linemen and specialists only rank high as real stars.
Measured on a full load: about 35 seconds and under 200 MB of managed heap; a second run changes nothing.

More on each source, and the ones that were rejected, in [research.md](research.md).

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `Import:Leagues` | `nba,nfl` | Leagues to import; `--leagues` sets it for one run |
| `Import:Source` | `espn` | NBA roster source: `espn` (live), `seed` (bundled sample) or `nba-stats` (stats.nba.com) |
| `Import:FallbackSource` | empty | NBA source to try if the primary fails |
| `Import:DailyAt` / `Import:TimeZone` | `04:00` / `America/New_York` | Nightly run time, safe across daylight saving changes |
| `Import:RunOnStartup` | `true` | Also run when the worker starts |
| `Import:MaxAttempts` / `Import:RetryDelaySeconds` | `3` / `60` | Retries before giving up for the night |
| `Import:HistoryRefreshDays` | `30` | 0 = only with `--history` |
| `Import:PuzzleDaysAhead` | `30` | Days of daily puzzles kept queued per generated game |
| `Import:PracticeRetentionDays` | `7` | Practice rounds older than this are deleted |
| `Sources:Bref:Directory` | empty | Read the CSVs from a folder instead of GitHub |
| `Sources:Nflverse:Directory` | empty | Read the nflverse files from a folder laid out like the releases (`players/players.csv`) |
| `Sources:Nflverse:FirstSeason` / `WeeklyFromSeason` | `1990` / `2016` | First season of rosters read, and the first read from the weekly files |
| `Sources:Nflverse:MinSeasons` | `2` | Players with fewer game-day seasons are left out unless they are on a roster now |
| `NumbersDontLie:QueueEnabled` | `false` | Queue approved Numbers Don't Lie snapshots (needs `CatalogueId` and `LaunchDate`) |

## Numbers Don't Lie content tools

These run offline, before any database work:

```bash
dotnet run --project src/KnowBall.Importer -- --validate-numbers <catalogue.json> [--require-approved]
dotnet run --project src/KnowBall.Importer -- --simulate-numbers <approved.json> --start 2026-11-01
```
