# API reference

All endpoints live under `/api` in `src/KnowBall.Api/Endpoints/`. The game endpoints need an `X-Anon-Id` header:
the browser makes up an id on its first visit (`web/know-ball/src/app/core/anon-id.ts`) and sends it on every call.
It identifies a browser, not an account. Errors the API reports itself have the shape `{ "error": "..." }`.

## Player games (Scouting Report, Career Path)

`{game}` is `scouting-report` or `career-path`.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/{game}/daily` | Today's puzzle (Eastern date) and this player's progress |
| `GET /api/{game}/puzzles/{id}` | A past daily or a practice round |
| `POST /api/{game}/{id}/guess` | `{ "playerId": 123, "requestId": "<uuid>" }`; `requestId` is optional |
| `POST /api/{game}/practice` | A new practice round |
| `GET /api/{game}/archive` | Past dailies, newest first, with this player's result on each |
| `GET /api/me/stats` | Played, win rate, streaks and guess distribution per game, from dailies only |
| `GET /api/me/today` | Where this player stands on each of today's puzzles, for the home page |
| `GET /api/players/search?q=&includeInactive=` | Autocomplete for the guess box |

A puzzle's answer is attached to the response only once the game is won or lost. A guess with a `requestId`
that was already recorded returns the original result instead of spending another guess; the same id with a
different player returns 409.

## Spordle

| Endpoint | Purpose |
| --- | --- |
| `POST /api/spordle/games` | Start a round (`wordLength` 4, 5 or 6) |
| `GET /api/spordle/games/{id}` | A saved round, for resuming after a reload |
| `POST /api/spordle/games/{id}/guesses` | `{ "word": "pitch", "requestId": "<uuid>" }` |
| `POST /api/spordle/games/{id}/hint` | A category or letter hint |

Rounds are stored in the database, so attempts and hints survive API restarts. A guess that is not a real word
is reported as invalid and costs nothing. Retrying the same word with the same `requestId` returns its original
result; reusing the id for a different word returns 409. Rounds expire after six idle hours; new rounds and the
nightly importer delete expired rows, and an expired round offers a fresh game without recording a loss.

## Numbers Don't Lie (beta)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/numbers-dont-lie/daily` | The shared five-question round for today (Eastern date) |
| `GET /api/numbers-dont-lie/quizzes/{id}` | A saved daily or practice round |
| `POST /api/numbers-dont-lie/practice` | A practice round filtered by leagues, topics and season years |
| `POST /api/numbers-dont-lie/practice/coverage` | How many questions a practice filter can draw from |
| `POST /api/numbers-dont-lie/quizzes/{id}/answers` | Answer the current question |
| `POST /api/numbers-dont-lie/quizzes/{id}/continue` | Acknowledge the explanation and move on |

Moves carry a `requestId` for safe retries and an `expectedRevision` so two tabs cannot advance the same round
twice. The server scores answers and reveals a solution only after its question is answered. See
[numbers-dont-lie/](numbers-dont-lie/README.md) for the content scope and design notes.

## Monitoring

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Database health check; never rate limited |
| `GET /api/import-runs/latest` | The last importer run, for monitoring |
| `GET /api/stats/summary` | Counts of leagues, teams, players and headshots |

## Rate limits

Every request to the site, pages and files included, is limited per address by `RateLimits:RequestsPerMinute`
(300; 0 turns it off; `/health` is exempt). A cold page load makes about a dozen requests. Guesses have stricter
limits on top: `Games:GuessesPerMinute` (60, also covering practice rounds and Numbers Don't Lie),
`Spordle:GuessesPerMinute` (20, shared by guesses and hints) and `Spordle:GamesPerMinute` (30). IPv6 addresses
count by their /64. A visitor over a limit gets `429` with `Retry-After` in seconds.

Behind nginx the address comes from `X-Forwarded-For`, trusted only from loopback and the networks in
`ForwardedHeaders:KnownNetworks`, so a made-up header cannot dodge the limits.
