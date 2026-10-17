# Implementation plan

Written 2026-10-12, after the design picks. It replaces the "Roadmap" section of the original build plan;
everything else in that plan (stack, architecture, API contract) still stands.

Updated 2026-10-13: ESPN replaces balldontlie as the nightly source, the NFL has a plan, and a ninth game,
Spordle, is added and built. What was tested and found for every data source is in [research.md](research.md).

**Names changed on 2026-10-13.** "Spordle" is now the Wordle-style word game. The player-guessing game that the
build plan calls Spordle is now **Scouting Report**. The site itself is now called **Know Ball**, and on 2026-10-14 the code
was renamed to match (`SportsTrivia` became `KnowBall`).

## 1. Where things stand

Updated 2026-10-13, end of day. **The version 1 code is written** (Milestones 0 to 4), but it is not live yet.
What is still missing is listed in [section 8](#8-whats-left).

- **Playable**: Spordle, Scouting Report and Career Path, each with a daily puzzle, practice rounds (not
  Spordle, which is always random), an archive, stats and a share card.
- **Built, but no puzzles yet**: Who's That Sportsman?. The game, the admin area and the image pipeline are done
  and tested. It needs about 30 photos uploaded through `/admin`; until then the home page shows "Coming soon".
- **Live data works**: a fresh database fills in 30 to 50 seconds from the Basketball-Reference mirror, Wikidata
  and ESPN (5,533 players), and a second run changes nothing.
- **Tests**: 213 unit, 26 integration (SQLite) and 99 Angular, all passing on 2026-10-13. Postgres has not been
  run against the new schema yet; CI does that on every push.
- **Not done**: the Docker image has never been built, there is no host, and the site has not been tried on a
  real phone.
- **Design**: one option chosen per game from the `/mockups` gallery (below).
- **Theme**: Trading Card, chosen 2026-10-13 from nine candidates at `/mockups/themes` and applied to the whole
  app: cream card stock, navy ink, red and mustard trim; Rokkitt for text, Alfa Slab One for headings and tiles,
  Yellowtail for script wordmarks. The fonts are bundled. The palette and roles are in
  `web/know-ball/src/styles.css`.
- **Version 1 is four games**: Spordle (built), Scouting Report, Career Path and Who's That Sportsman?. If the
  photo collection for the last one lags, version 1 ships with three. The other five are designed but not scheduled.

## 2. Design picks and what each one commits us to

| Game | Pick | Consequences for the build |
| --- | --- | --- |
| Scouting Report | B, Guess cards | Headshot on every guess, so players need NBA ids. Guess box docked at the bottom. The share grid is generated from the clue colours, since the cards themselves don't make one. |
| Who's That Sportsman? | B, Iconic moment | Every puzzle is hand-made: an admin area, image storage and a cut-out step move into the MVP. Answers are often retired players, so the database needs full history, not just current rosters. No unlimited practice mode. |
| Career Path | B, Journey list | Needs every player's team history with years. Team names are shown as text, so defunct teams (Sonics, Nets in New Jersey) can use a text badge where no logo exists. |
| Stat Quiz | A, Multiple choice | Needs season stats for every season. Questions come from templates; answers are checked one question at a time on the server. |
| Geo Sports | B, Card, map, result | A real map library (Leaflet). Arena coordinates already exist; hometown rounds need birthplace coordinates. |
| Sportle | B, Trading card back | Needs season stats plus three written hints per puzzle, generated from templates with an admin override. |
| Sound It Out | B, Letter bank | Hand-made puzzles (reuses the admin area and image storage). No typing, so no autocomplete. |
| Who's the GOAT? | B, 100-point budget | Needs career totals, awards and championships for an all-time pool of players; saved rankings on the server for the site-wide average. |
| Spordle (new) | Built like Nerdle, not from the mockups: 4, 5 or 6 letters, on-screen keyboard, a category clue and a letter hint | Needs no player stats: 555 hand-picked sports words in 26 categories, plus a general dictionary for guesses. Play-anytime random words, so it does not use the daily engine. The three mockup options at `/mockups/spordle` are superseded. |

The big shift from the original plan: three of the eight picks need **historical** data, and two need
**hand-made content**. Both are handled in Milestone 0 and Milestone 3.

## 3. Data plan

No single free source covers everything, so the importer combines three. All were tested on 2026-10-12 or 10-13;
the details, the rejected providers and the NFL sources are in [research.md](research.md).

| Source | What it gives | How it is used | Checked |
| --- | --- | --- | --- |
| Basketball-Reference CSV mirror ([sumitrodatta/bball-reference-datasets](https://github.com/sumitrodatta/bball-reference-datasets), `Data/`) | Every player since 1947: position, height, weight, birth date, college, first and last season, Hall of Fame flag; per-season stats by team; All-Star picks; All-NBA and other end-of-season teams; award voting with winners; draft history | The spine. One bulk load, re-run a few times a year. Team history is derived from the per-season team rows. | Files download directly; last updated April 2026 and include the 2025-26 season |
| Wikidata (SPARQL) | For 5,518 players: NBA.com id, Basketball-Reference id, birth date; birthplace coordinates for 5,321 of them | The crosswalk. Links the spine to NBA ids (for headshot URLs) and supplies hometown coordinates for Geo Sports. | Query returned those counts; LeBron James resolves to NBA id 2544 and Akron |
| ESPN roster feed (no key) | Current team, jersey number, position, height, birth date, headshot and birthplace for today's players | The nightly job: keeps teams and jersey numbers fresh after trades. Matched to the spine by name and birth date. | 30 teams, 625 players; birth date on all of them |

Things no source provides, and the workaround:

- **Championship rings**: a hand-entered list of champions by season (about 80 rows), joined to that season's roster.
- **Jersey number per team stint**: not available anywhere usable. The Career Path mockup's "#21" hint is replaced by position, draft and college hints.
- **Logos for defunct teams**: text badge with the old abbreviation.
- **Headshots for older retired players**: the NBA CDN may not have them; fall back to an initials avatar.

Details to handle in the importer:

- Basketball-Reference names seasons by their end year (2026 is 2025-26); this codebase uses the start year.
- Its team codes differ from the NBA's (BRK, CHO, PHO); `Team Abbrev.csv` maps them.
- A traded player has several team rows in one season. `PlayerSeasonStat` is currently unique per player and
  season, so the key becomes player, season and team, with one combined row.
- ESPN's feed is unofficial and has had outages on one of its two hosts, so the importer tries the second host,
  then balldontlie (needs a free key), then leaves yesterday's data in place.
- The mirror has no licence file and the headshots are the NBA's. Fine for a personal project; revisit if that changes.

## 4. Milestones

Sizes are relative: S is a sitting, M is a few, L is many. A milestone starts only when the previous gate is met.

Version 1 is Milestones 0 to 4: three or four games, live on mobile. Everything under "Later" is unscheduled.

### Milestone 0: finish Foundation (L)

**Status: built. Gate not met**, because no scheduled run happens until the site is hosted. Differences from the
plan: the balldontlie fallback was not built (the importer tries ESPN's second host, then keeps yesterday's
data); the stray `.transfer.tar.gz` was kept and is ignored by git instead of deleted.

1. `git init`, delete the stray `.transfer.tar.gz`, push to GitHub, get CI green.
2. Schema changes (second migration):
   - `PlayerExternalId` (player, source, id) so one player can carry Basketball-Reference, NBA and ESPN ids.
   - `Player`: birthplace name and coordinates, Hall of Fame flag, last season.
   - `PlayerSeasonStat`: games played, minutes, new unique key (above).
   - `Team`: active-from and active-to seasons plus a franchise link, for defunct identities.
   - `Puzzle`: puzzle number, status, nullable date, and a server-only `SecretJson` next to the client-safe `PayloadJson`.
   - `UserResult`: started-at, hints used, nullable completed-at so in-progress games are tracked.
3. `BrefCsvSource`: load players, season stats, awards and All-Star picks; derive `PlayerTeamStint` rows.
4. `WikidataCrosswalk`: attach NBA ids, headshot URLs and birthplace coordinates.
5. `EspnRosterSource` behind the existing `IPlayerSource`: nightly current team and jersey number.
6. Popularity v2: career points, All-Star picks and awards, so retired stars rank properly in search and puzzle selection.
7. Search: optional `includeInactive` on the endpoint (the service already supports it) and headshots in results.

**Gate: nightly import works.** A scheduled run against live data succeeds two nights running, and the database
holds the full player history with headshots for current players.

### Milestone 1: game engine and Scouting Report (L)

The engine is built once, with Scouting Report as the first game on it.

**Status: built.** Gate met in the desktop browser at phone size (375 by 812), not yet on a real phone.
Differences from the plan: practice is `POST /api/{game}/practice`, and `GET /api/{game}/puzzles/{id}` was added
for archive links. The result, stats and help dialogs and the hint ladder live inside `GameShell` rather than as
separate components. A daily puzzle is also created on the first request of the day if the importer has not
queued one. The answer pool is the 150 best-known active players with every clue filled in.

Backend (`KnowBall.Application/Games`, endpoints moved out of `Program.cs` into `Api/Endpoints`):

- `IGame`: generate a puzzle, build the client payload, evaluate a guess.
- `GameService`: loads the puzzle, tracks progress in `UserResult`, enforces guess limits and hint unlocks, and
  only attaches the answer once the game is over.
- `PuzzleCalendar`: "today" in America/New_York and puzzle numbering.
- `PuzzleGenerationJob` in the nightly worker: keep 30 days of puzzles queued per generated game; the payload is a
  snapshot taken at generation time.
- Endpoints, as in the build plan: `GET /api/{game}/daily`, `POST /api/{game}/{puzzleId}/guess`,
  `GET /api/{game}/archive`, `GET /api/{game}/practice`, `GET /api/me/stats`.
- Identity: a random id created in the browser and sent as an `X-Anon-Id` header. Accounts come in Phase 2.

Frontend (`web/know-ball/src/app`):

- `core/`: `GameApi`, `GameStore` (signals: puzzle, guesses, status), anonymous id.
- `shared/`: `GameShell` (title bar with puzzle number, help, stats), `ResultModal`, `StatsModal`, `ShareService`,
  `HintLadder`; a bottom-docked variant of the existing `PlayerSearch` that shows headshots.
- Move the clue colours from the mockups into `styles.css`.

Scouting Report itself:

- Eight guesses. Answers come from active players above a popularity threshold, with no repeat inside 120 days.
- `AttributeComparer` as a pure class with heavy unit tests:

| Attribute | Green | Yellow | Otherwise |
| --- | --- | --- | --- |
| Team, conference, division | Same | none | Grey |
| Position | Identical | Shares a position (G and G-F) | Grey |
| Height | Same | Within 2 inches, with arrow | Grey with arrow |
| Age | Same | Within 2 years, with arrow | Grey with arrow |
| Number | Same | Within 3, with arrow | Grey with arrow |

- Share text: `Scouting Report #142 4/8` plus one row of seven squares per guess.

**Gate:** Scouting Report is playable end to end on a phone, a reload restores the game, and an integration test proves
the answer is absent from every response before the game ends.

### Milestone 1b: Spordle, the word game (done 2026-10-13)

Built to match Nerdle (`nerdle.nathanzimmerman.com`) with sports words and Know Ball's look.

- **Play**: a random word each game, six guesses, word length switchable between 4, 5 and 6. "New game" at any
  time; abandoning a game after a scored guess counts as a loss, with a confirmation first.
- **Server owns the answer**: `POST /api/spordle/games` returns only an id; `.../guesses` returns per-letter
  scores and reveals the word once the game is over; `.../hint` returns the category or one unsolved letter.
  A word outside the dictionary is refused without using a guess.
- **Hints**: the category is free to ask; the letter hint is drawn once per game from positions not yet solved
  and also appears faintly in its tile. Neither uses a guess.
- **Stats**: per word length, kept in the browser: played, win %, streaks, fastest solve, fewest guesses.
- **Limits**: games live in server memory, expire after six idle hours and are lost on restart, as in Nerdle.
  Guesses and hints are limited to 20 a minute per address, new games to 30.
- **Code**: `src/KnowBall.Application/Games/Spordle`, `src/KnowBall.Api/Endpoints/SpordleEndpoints.cs`,
  `web/know-ball/src/app/games/spordle`.

Not built, and worth deciding later:

- A **daily** Spordle that is the same word for everyone, with a share grid. That fits the shared engine from
  Milestone 1; the current game would become its practice mode.
- Stats on the server, which need accounts (Phase 2).

### Milestone 2: Career Path (M)

**Status: built; gate met.** The pool is the 250 best-known players with three or more franchises and at least
one season since 1990. A wrong guess is told which stops it shares with the answer.

- Puzzle: the ordered stops (team name, logo or badge, years, number of seasons), snapshotted.
- Eligibility: three or more stops and above the popularity threshold.
- Any player whose sequence of franchises matches is accepted as correct.
- Six guesses. Hint ladder, one step per miss: position, draft year and pick, college or country, initials.
- Search includes retired players.

**Gate:** playable on the engine with no engine changes beyond what the hint ladder needs.

### Milestone 3: admin area and Who's That Sportsman? (L)

**Status: built; gate not met (no puzzles yet).** Differences from the plan: SkiaSharp renders the images instead
of ImageSharp (see [research.md](research.md)); images are served from `/photos`; the schedule is reordered with
Earlier and Later buttons rather than by dragging; "published" is not stored but means scheduled for today or
earlier; there is no "preview as a player" view; images are stored on local disk only.

- Admin area (`/admin`, protected by a single key from config): puzzle queue with draft, scheduled and published
  states; drag to reorder the schedule; preview as a player.
- Image storage behind `IImageStore`: a local `media/` folder in development, blob storage at deploy time.
  Files get random names so a URL can't be guessed.
- Creating a puzzle: upload the photo and a transparent cut-out of the subject, pick the answer player, enter the
  year, team and a caption. The server (ImageSharp) renders the black silhouette and the blurred version.
- Play: five guesses. Ladder: silhouette, then year, then team, then blurred photo. Each image URL is released
  by the server only when its step unlocks; the full photo and caption appear at the end.
- Content: about 30 photos are needed before the daily can launch. Collecting them can start during Milestone 1.

**Gate:** 30 puzzles scheduled and one played through on a phone.

### Milestone 4: MVP polish and launch (M)

**Status: partly done.** Done: stats dialog, archive, share on every game, home page with today's state for each
game, the built games' mockups removed, a mobile pass at phone size, a web app manifest and service worker, a
Dockerfile and a `site` profile in `docker-compose.yml`. Not done: hosting, a deploy step, building the image,
checking that the site installs as an app, and Playwright tests.

- Stats modal (played, win %, streaks, guess distribution), archive of past dailies, share on every game.
- Home page shows the version 1 games and today's state for each. Remove their pages from `/mockups`.
- Mobile pass, installable PWA, hosting, deploy step in CI.

**Gate: version 1 live on mobile.** After this, replay counts decide the order of everything below.

### Later: Phase 2 (not scheduled)

- **Stat Quiz** (M): question templates over season stats ("Who led the league in {stat} in {season}?"), three
  distractors from that season's top ten, ten questions, 15-second timer, speed bonus. The client receives one
  question at a time.
- **Geo Sports** (M): Leaflet with OpenStreetMap tiles, five rounds mixing arenas and hometowns, Haversine
  distance and scoring on the server, result card with the line between guess and answer.
- **Accounts and sync** (M).
- **Second league: NFL** (L). The source is nflverse: free, updated daily, with ids, birth dates and headshots for
  24,853 players, rosters back to 1920 and season stats from 1999.
  - Import: `NflverseSource` for players, season rosters (team history), season stats and draft summaries; Wikidata
    for birthplaces; hand lists for MVPs, early Super Bowl winners and stadium coordinates.
  - League switch: the API already takes `league=`; the Angular shell gets a switcher and each game reads it.
  - Per game: Scouting Report, Career Path and Who's That Sportsman? work as they are, so NFL iconic photos become usable
    here. Stat Quiz and Sportle need per-position stat templates and start at 1999. GOAT needs a rethink, most
    likely a ranking per position.
  - Popularity for NFL players comes from Pro Bowls, All-Pro picks and games started, not points.

### Later: Phase 3 (not scheduled)

- **Sportle** (M): card with season rows and teams hidden; three hints generated from templates (awards, draft
  slot, college, championships), each costing points; teams revealed after the fourth miss.
- **Sound It Out** (S, on top of Milestone 3): admin uploads two to four images and picks the answer; the server
  builds a 14-letter bank from the answer plus decoys.
- **Who's the GOAT?** (M): pool of about 150 all-time players with six scores scaled 0 to 100 (points, rings,
  MVPs, All-NBA, peak, longevity); the browser computes the ranking from one cached payload; presets, saved
  rankings and the site average.
- **Leaderboards** (M).

## 5. Testing

- Unit tests for every comparer, generator and scoring rule; these are pure classes by design.
- One integration test per game for the daily and guess contract, including the answer-leak check.
- Component tests for the shared Angular pieces. Playwright end-to-end tests start at Milestone 4.
- `scripts/verify.sh` stays the single gate check and grows a step per milestone.

## 6. Decisions I've assumed

Each has a default so work isn't blocked; say so if you'd choose differently.

| Decision | Default |
| --- | --- |
| Curated queue runs dry | Replay the oldest puzzle rather than show nothing |
| Making cut-outs | Done by hand before upload (Preview's "Copy Subject" on a Mac); automatic background removal later if it becomes a chore |
| Career Path jersey hint | Dropped for lack of data; position, draft and college hints instead |
| Nightly source | ESPN's unofficial feed, with balldontlie as the fallback; no paid tier |
| Spordle | Play-anytime like Nerdle for now; a daily word comes with the game engine |
| Admin protection | One key in config; proper accounts in Phase 2 |
| Hosting | Still open; see section 8 |
| Champions by season | Not entered for version 1; it comes with Who's the GOAT? and Sportle, which need it |
| A different theme per game | No. One theme for the site; each game gets its own trim colour, like cards from one set |
| Which game drops if version 1 is three | Who's That Sportsman?, since it is the only one waiting on hand-collected photos |

## 7. What I need from you

1. Iconic photos for Who's That Sportsman?, about 30 to start, each with a cut-out of the player
   (on a Mac: open the photo in Preview, Tools → Remove Background, export as PNG).
2. A hosting choice (section 8, item 4).
3. Whether to rename Sportle, the Phase 3 game, since it is one letter from Spordle.

No API key is needed. A free balldontlie key is only worth getting if ESPN's feed starts failing.

## 8. What's left

Recorded 2026-10-13, when the build stopped to put the code on GitHub. Items 1 to 8 stand between the code and
version 1 being live; the rest can wait.

### Before launch

1. **Build and run the Docker image.** Done in CI (2026-10-14): the `docker` job runs `scripts/docker-smoke.sh`,
   which builds the image and checks, on an empty Postgres, that migrations apply, the importer loads seed data
   and queues puzzles, the daily games hide their answers, `/admin` needs its key, the calendar is on Eastern
   time, and a puzzle renders with SkiaSharp and saves to `/data/photos`. The image used to run as root; it now
   runs as the non-root `app` user, which owns `/data`. The full-history download the smoke test skips ran on
   the VM's first start (2026-10-16): 5416 players and 99 teams from bref, 5397 Wikidata links, 630 ESPN
   players, 30 Scouting Report and 29 Career Path puzzles queued.
2. **Postgres.** Done: the first CI run passed the integration tests and the importer against Postgres 17.
3. **Who's That Sportsman? content.** 30 puzzles through `/admin`: photo, cut-out, player, year, team, caption.
   Then play one through on a phone (the Milestone 3 gate).
4. **Hosting.** Chosen 2026-10-14: Nathan's GCP VM `nz-portfolio-vm`, behind its nginx, at
   `knowball.nathanzimmerman.com`. CI publishes the image to GHCR and deploys over SSH; the one-time setup
   (DNS, `.env`, nginx and certbot, deploy key and secrets, backups) is in `deploy/README.md`. Live since
   2026-10-16. Still to confirm: the nightly backup cron (step 7) and a GCP snapshot schedule.
5. **Rate limits behind a proxy.** Fixed: `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is gone. `X-Forwarded-For` now
   counts only from loopback and the networks in `ForwardedHeaders:KnownNetworks`; in production that is the
   Docker network nginx connects through, and nginx overwrites the header with the address it saw.
6. **A real phone.** Play each game on iOS Safari and Android Chrome, add the site to the home screen, and check
   that a new deploy reaches an installed copy. The service worker would not register in the desktop app's
   built-in browser, so it has not been seen working anywhere yet.
7. **The admin upload form.** Creating a puzzle was tested through the API and integration tests, not by
   filling in the form in a browser.
8. **`scripts/verify.sh`.** Updated (2026-10-14): ESPN for the live import, `--no-history` throughout, checks for
   the `VersionOne` migration, smoke-tests the daily games and the admin key, honours `KNOWBALL_DB_PORT`, and
   runs `scripts/docker-smoke.sh` at the end when Docker is available.

The Milestone 0 gate (two nightly imports in a row) is met after the 4am runs on 2026-10-17 and 10-18
succeed; the `ImportRuns` query in `deploy/README.md` (step 6) is the check.

### Soon after launch

- Playwright tests: one per game that starts it, guesses, reloads and finishes.
- Admin: a "preview as a player" view, and drag to reorder if the buttons get tedious.
- balldontlie as a third roster source, if both ESPN hosts fail for more than a day.
- A daily Spordle, the same word for everyone, on the shared engine; today's game becomes its practice mode.
- Housekeeping on this Mac: the stray `.transfer.tar.gz` (an old copy of the source from 10-10) can be deleted,
  and the old `sports-trivia-db` container is left over from before `docker-compose.yml` stopped naming it.
