# API reference

All endpoints live under `/api` in `src/KnowBall.Api/Endpoints/`. The game endpoints need an `X-Anon-Id` header:
the browser makes up an id on its first visit (`web/know-ball/src/app/core/anon-id.ts`) and sends it on every call.
It identifies a browser, not an account. Errors the API reports itself have the shape `{ "error": "..." }`.

## Player games (Scouting Report, Career Path, Back of the Card, Team Photo)

`{game}` is `scouting-report`, `career-path`, `back-of-the-card` or `team-photo`.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/{game}/daily` | Today's puzzle (Eastern date) and this player's progress |
| `GET /api/{game}/puzzles/{id}` | A past daily or a practice round |
| `POST /api/{game}/{id}/guess` | `{ "playerId": 123, "requestId": "<uuid>" }`; `requestId` is optional |
| `POST /api/{game}/practice` | A new practice round |
| `GET /api/{game}/archive` | Past dailies, newest first, with this player's result on each |
| `GET /api/me/stats` | Played, win rate, streaks and guess distribution per game, from dailies only |
| `GET /api/me/today` | Where this player stands on each of today's puzzles, for the home page |
| `GET /api/players/search?q=&league=&includeInactive=` | Autocomplete for the guess box (`league` defaults to `nba`) |

A puzzle's answer is attached to the response only once the game is won or lost. A guess with a `requestId`
that was already recorded returns the original result instead of spending another guess; the same id with a
different player returns 409.

Scouting Report, Career Path and Back of the Card also have an NFL edition. `daily`, `practice` and `archive` take `?league=nba` or
`?league=nfl` (`nba` when left out); each league has its own daily, numbered from one, with its own archive,
streak and stats. Every puzzle response carries `league`, and so does each entry from `me/stats` and `me/today`.
A guess at a player from another league is refused with 400, and a league a game has no edition in returns 404.
The NFL editions are live only when `Games:Leagues` lists `nfl` (`nba,nfl` in appsettings); the importer reads
the same setting to decide which queues to fill. The site's NFL pages are `/scouting-report/nfl`, `/career-path/nfl` and `/back-of-the-card/nfl`.

An NFL Back of the Card payload has `columns` (`label` and `decimals`) for the player's position group (passing,
rushing, receiving or defence), each season's `games` and `values` as season totals, and a career line of totals;
seasons are by their own year. The NBA payload keeps `points`, `rebounds` and `assists` per game.

Team Photo has five answers instead of one. Its payload is a Finals team (`team`, `season`, `wins`, `losses`,
`finals` of `won` or `lost`) and five `slots`, each with a `position`, a `row` (`back` or `front`) and that
player's games, points, rebounds and assists that season. A guess at a starter is correct with feedback
`{ "slot": 2 }`; a guess at one of the team's bench players is wrong but sends `"miss": false` and feedback
`{ "bench": true }`; anyone else is a miss. `maxGuesses` (4) is a limit on misses, not guesses, and the game is
won once every slot is named. The answer is `{ "slots": [{ "playerId", "name", "headshotUrl" }] }` in slot order.
In `me/stats` its distribution counts solves by misses (0 to 3), and in `me/today` its `guesses` are misses.

## Letter Jacket

| Endpoint | Purpose |
| --- | --- |
| `POST /api/letter-jacket/games` | Start a round (`wordLength` 4, 5 or 6) |
| `GET /api/letter-jacket/games/{id}` | A saved round, for resuming after a reload |
| `POST /api/letter-jacket/games/{id}/guesses` | `{ "word": "pitch", "requestId": "<uuid>" }` |
| `POST /api/letter-jacket/games/{id}/hint` | A category or letter hint |

Rounds are stored in the database, so attempts and hints survive API restarts. A guess that is not a real word
is reported as invalid and costs nothing. Retrying the same word with the same `requestId` returns its original
result; reusing the id for a different word returns 409. Rounds expire after six idle hours; new rounds and the
nightly importer delete expired rows, and an expired round offers a fresh game without recording a loss.

## Numbers Don't Lie

| Endpoint | Purpose |
| --- | --- |
| `GET /api/numbers-dont-lie/daily` | The shared five-question round for today (Eastern date) |
| `GET /api/numbers-dont-lie/quizzes/{id}` | A saved daily or practice round |
| `POST /api/numbers-dont-lie/practice` | A practice round filtered by leagues, topics and season years |
| `POST /api/numbers-dont-lie/practice/coverage` | How many questions a practice filter can draw from |
| `POST /api/numbers-dont-lie/quizzes/{id}/answers` | Answer the current question |
| `POST /api/numbers-dont-lie/quizzes/{id}/continue` | Acknowledge the explanation and move on |

Moves carry a `requestId` for safe retries and an `expectedRevision` so two tabs cannot advance the same round
twice. Daily rounds also return `dailyStreak`, the Eastern days in a row, ending today or
yesterday, on which this browser finished the daily round (`null` in practice). The server scores answers and reveals a solution only after its question is answered. Topics are `history`, `nicknames`, `awards`, `teams` and `playoff-moments`; the daily round
draws only questions marked daily-eligible. See
[numbers-dont-lie/](numbers-dont-lie/README.md) for the content scope and design notes.

## Monitoring

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Database health check; never rate limited |
| `GET /api/import-runs/latest` | The last importer run, for monitoring |
| `GET /api/stats/summary` | Counts of leagues, teams, players and headshots |
| `GET /api/games/paused` | The slugs of games paused by their feature flags; usually `[]` |

## Rate limits

Every request to the site, pages and files included, is limited per address by `RateLimits:RequestsPerMinute`
(300; 0 turns it off; `/health` is exempt). A cold page load makes about a dozen requests. Guesses have stricter
limits on top: `Games:GuessesPerMinute` (60, also covering practice rounds and Numbers Don't Lie),
`LetterJacket:GuessesPerMinute` (20, shared by guesses and hints) and `LetterJacket:GamesPerMinute` (30). IPv6 addresses
count by their /64. A visitor over a limit gets `429` with `Retry-After` in seconds.

Behind nginx the address comes from `X-Forwarded-For`, trusted only from loopback and the networks in
`ForwardedHeaders:KnownNetworks`, so a made-up header cannot dodge the limits.

## Pausing a game

Each game has a feature flag in the `FeatureManagement` section: `LetterJacket`, `ScoutingReport`, `CareerPath`,
`BackOfTheCard`, `TeamPhoto` and `NumbersDontLie`. A flag that is not set leaves its game on. Set one to `false` to
take that game off the site when its puzzles have gone wrong: its endpoints answer `503` with an `error` the game
page shows, the home page shows its card as taking a break, and the other games, stats and today's progress carry
on. `GET /api/games/paused` lists the paused slugs.

On the server, `deploy/docker-compose.yml` passes each flag from `.env`: add `KNOWBALL_CAREER_PATH=false` (or
`KNOWBALL_LETTER_JACKET`, `KNOWBALL_SCOUTING_REPORT`, `KNOWBALL_BACK_OF_THE_CARD`, `KNOWBALL_TEAM_PHOTO`,
`KNOWBALL_NUMBERS_DONT_LIE`), then run `docker compose up -d site` in `~/know-ball` so the container takes the new
value. Remove the line, or set it to `true`, and run the same command to bring the game back.

Flags are plain on/off here. `Microsoft.FeatureManagement` could also switch one by a time window or a share of
visitors, but nothing needs that yet.
