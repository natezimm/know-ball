# Implementation plan

Written 2026-10-12, after the design picks. It replaces the "Roadmap" section of the original build plan;
everything else in that plan (stack, architecture, API contract) still stands.

Updated 2026-10-20: Who's That Sportsman? and its authoring/image pipeline are removed from the active project.
Version 1 is three games; the fourth game is undecided. See [future games](future-games.md).
The earlier implementation history below is retained where relevant.

Historical review, 2026-10-17: reconciled launch status with main at `2cf5164`, merged PRs and GitHub Actions.
Deployment, Docker/Postgres checks and Playwright gameplay are implemented and passing; production operations,
real-device validation still need evidence. Section 8 is the single launch checklist.
Data-source research retains its dated findings in [research.md](research.md).

**Names changed on 2026-10-13.** "Spordle" is now the Wordle-style word game. The player-guessing game that the
build plan calls Spordle is now **Scouting Report**. The site itself is now called **Know Ball**, and on 2026-10-14 the code
was renamed to match (`SportsTrivia` became `KnowBall`).

## 1. Where things stand

Updated 2026-10-17. **The version 1 code is written and the site is deployed**, but launch validation is
incomplete. Current evidence and remaining gates are in [section 8](#8-whats-left).

- **Playable**: Scouting Report and Career Path have daily puzzles, practice, archives, stats and share cards.
  Spordle is a random play-anytime word game with browser-local stats; it has no daily puzzle or archive.
- **Deferred**: Who's That Sportsman? was removed after the cutout quality review. It is not a launch gate.
  Any return needs a new content-quality approach; see [future games](future-games.md).
- **Production data loaded**: the first VM import on 2026-10-16 recorded 5,416 history players, 99 team
  identities, 5,397 Wikidata links and 630 ESPN roster players. These are per-source counts, not a current total.
- **Automated verification**: the latest CI passed backend and Angular tests, integration tests on SQLite and
  Postgres, importer repeatability, Docker smoke checks, and Playwright gameplay. The historical run included
  the now-removed photo game; current tests cover the three active games. See the run linked in section 8 rather than treating older test counts as current.
- **Hosted**: GCP VM `portfolio-vm`, behind nginx, deployed from main through GHCR and SSH.
- **Still unverified**: two scheduled production imports in succession, backups and restore, and real-phone/PWA installation and update behavior. A green live-source canary is separate from
  the production nightly-import gate.
- **Design**: one option chosen per game from the `/mockups` gallery (below).
- **Theme**: Trading Card, chosen 2026-10-13 from nine candidates at `/mockups/themes` and applied to the whole
  app: cream card stock, navy ink, red and mustard trim; Rokkitt for text, Alfa Slab One for headings and tiles,
  Yellowtail for script wordmarks. The fonts are bundled. The palette and roles are in
  `web/know-ball/src/styles.css`.
- **Version 1 is three games**: Spordle, Scouting Report and Career Path. A fourth game may replace
  Who's That, but no replacement has been chosen. Other game designs remain unscheduled.

## 2. Design picks and what each one commits us to

| Game | Pick | Consequences for the build |
| --- | --- | --- |
| Scouting Report | B, Guess cards | Headshot on every guess, so players need NBA ids. Guess box docked at the bottom. The share grid is generated from the clue colours, since the cards themselves don't make one. |
| Who's That Sportsman? | Deferred | Removed from the active project on 2026-10-20; cutouts did not meet the quality bar. See [future games](future-games.md). |
| Career Path | B, Journey list | Needs every player's team history with years. Team names are shown as text, so defunct teams (Sonics, Nets in New Jersey) can use a text badge where no logo exists. |
| Stat Quiz | A, Multiple choice | Needs season stats for every season. Questions come from templates; answers are checked one question at a time on the server. |
| Geo Sports | B, Card, map, result | A real map library (Leaflet). Arena coordinates already exist; hometown rounds need birthplace coordinates. |
| Sportle | B, Trading card back | Needs season stats plus three written hints per puzzle, generated from templates with an admin override. |
| Sound It Out | B, Letter bank | Hand-made puzzles (would need a new authoring area and image storage). No typing, so no autocomplete. |
| Who's the GOAT? | B, 100-point budget | Needs career totals, awards and championships for an all-time pool of players; saved rankings on the server for the site-wide average. |
| Spordle (new) | Built like Nerdle, not from the mockups: 4, 5 or 6 letters, on-screen keyboard, a category clue and a letter hint | Needs no player stats: 555 hand-picked sports words in 26 categories, plus a general dictionary for guesses. Play-anytime random words, so it does not use the daily engine. The three mockup options at `/mockups/spordle` are superseded. |

The big shift from the original plan: three of the eight picks need **historical** data, and two need
**hand-made content**. Historical data is handled in Milestone 0; hand-made content is deferred.

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
- A traded player has several team rows in one season. The implemented `PlayerSeasonStat` key includes player,
  season and team, with one combined row.
- ESPN's feed is unofficial and has had outages on one of its two hosts. The importer tries the second host,
  then keeps yesterday's data on failure. A balldontlie fallback is not implemented.
- The mirror has no licence file and the headshots are the NBA's. Fine for a personal project; revisit if that changes.

## 4. Milestones

Sizes are relative: S is a sitting, M is a few, L is many. The original sequence below describes the build.
Implementation has reached Milestone 4; unfinished validation gates remain open in section 8.

Version 1 is Milestones 0, 1, 2 and 4: three games, live on mobile. Milestone 3 is deferred. Everything under "Later" is unscheduled.

### Milestone 0: finish Foundation (L)

**Status: built; production gate awaiting evidence.** The site is hosted and the first production import
completed. Verify two successive scheduled 04:00 America/New_York runs in the production database (section 8).
The GitHub live-import canary uses disposable SQLite and does not establish this gate. Differences from the
original plan: no balldontlie fallback; the local `.transfer.tar.gz` was ignored rather than deleted.

1. Repository pushed to GitHub; CI is green. Local archive cleanup is optional housekeeping.
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

**Status: built; automated checks pass, real-phone gate awaiting evidence.** Desktop testing at phone size
(375 by 812) and Playwright gameplay are covered; they do not replace a physical-device check.
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
  `GET /api/{game}/archive`, `POST /api/{game}/practice`, `GET /api/me/stats`.
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

### Milestone 3: deferred photo game

**Status: removed from the active project, 2026-10-20.** The prepared cutouts did not meet the visual
quality bar. The game, authoring UI/API, image renderer and content tooling are removed. Existing database
rows and the legacy media volume remain intact. A future fourth game is undecided and does not block launch.
The concept and revisit criteria are retained in [future games](future-games.md).

### Milestone 4: MVP polish and launch (M)

**Status: implemented; launch validation incomplete.** Done: stats and sharing, archives for daily player
games, home-page daily state, removal of built-game mockups, a mobile layout pass, manifest and service worker,
Docker image, GCP hosting, CI deployment and Playwright gameplay. Real-phone behavior, installed-PWA updates,
production operations remain open in section 8.

- Stats modal (played, win %, streaks, guess distribution), archive of past dailies, share on every game.
- Home page shows the version 1 games and today's state for each. Remove their pages from `/mockups`.
- Mobile pass, installable PWA, hosting, deploy step in CI.

**Gate: version 1 live on mobile.** After this, replay counts decide the order of everything below.

### Later: Phase 2 (not scheduled)

- **Fourth game — undecided**: compare the candidates in [future games](future-games.md) after the three-game launch.
- **Who's That Sportsman? — deferred**: revisit only with a stronger photo/cutout approach; no current implementation or launch requirement.
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
  - Per game: Scouting Report and Career Path can extend to NFL data; any photo game remains deferred
    here. Stat Quiz and Sportle need per-position stat templates and start at 1999. GOAT needs a rethink, most
    likely a ranking per position.
  - Popularity for NFL players comes from Pro Bowls, All-Pro picks and games started, not points.

### Later: Phase 3 (not scheduled)

- **Sportle** (M): card with season rows and teams hidden; three hints generated from templates (awards, draft
  slot, college, championships), each costing points; teams revealed after the fourth miss.
- **Sound It Out** (S, requires new authoring tooling): admin uploads two to four images and picks the answer; the server
  builds a 14-letter bank from the answer plus decoys.
- **Who's the GOAT?** (M): pool of about 150 all-time players with six scores scaled 0 to 100 (points, rings,
  MVPs, All-NBA, peak, longevity); the browser computes the ranking from one cached payload; presets, saved
  rankings and the site average.
- **Leaderboards** (M).

## 5. Testing

- Unit tests for every comparer, generator and scoring rule; these are pure classes by design.
- One integration test per game for the daily and guess contract, including the answer-leak check.
- Component tests for shared Angular pieces, plus Playwright wins/losses for the three active games against the real API.
  Photo-game tests and synthetic authoring fixtures were removed with the feature.

- `scripts/verify.sh` stays the single gate check and grows a step per milestone.

## 6. Decisions I've assumed

Each has a default so work isn't blocked; say so if you'd choose differently.

| Decision | Default |
| --- | --- |
| Making cut-outs | Done by hand before upload (Preview's "Copy Subject" on a Mac); automatic background removal later if it becomes a chore |
| Career Path jersey hint | Dropped for lack of data; position, draft and college hints instead |
| Nightly source | ESPN's unofficial feed, trying its second host on failure; keep existing data if both fail. No balldontlie fallback is implemented |
| Spordle | Play-anytime like Nerdle; daily mode is a proposed post-launch increment, not implemented |
| Hosting | GCP VM `portfolio-vm`, nginx, GHCR image and SSH deployment from CI; see deploy/README.md |
| Champions by season | Not entered for version 1; it comes with Who's the GOAT? and Sportle, which need it |
| A different theme per game | No. One theme for the site; each game gets its own trim colour, like cards from one set |
| Fourth game | Undecided; Who's That is deferred. Compare alternatives after the three-game launch. |

## 7. Inputs and deferred decisions

Launch evidence is tracked only in section 8; future game ideas are in [future games](future-games.md). Hosting is already chosen; no API key is
needed for the current sources. Renaming Sportle (one letter from Spordle) is a deferred Phase 3 decision.

## 8. What's left

Updated 2026-10-17 against main at `2cf5164`. This is the single launch checklist. Check a remaining item only
after adding dated evidence here. Code and CI establish automated behavior; they do not prove VM configuration,
production scheduling or real-device behavior. Photo content is no longer a launch gate.

### Completed work and evidence

| Work | Evidence |
| --- | --- |
| Historical Docker smoke covered the original four-game implementation; current smoke covers migrations, importer, answer hiding, Eastern calendar and retired-endpoint removal | [PR #2](https://github.com/natezimm/know-ball/pull/2), [smoke script](../scripts/docker-smoke.sh), latest CI below |
| SQLite and Postgres integration, importer repeatability, Angular tests and production build | [CI workflow](../.github/workflows/ci.yml); [successful main run, 2026-10-17](https://github.com/natezimm/know-ball/actions/runs/39035184904) |
| Hosting, proxy-aware rate limits, image publication and deployment | [PR #3](https://github.com/natezimm/know-ball/pull/3), [PR #4](https://github.com/natezimm/know-ball/pull/4), successful publish/deploy jobs in the main run above; site recorded live since 2026-10-16 |
| First production history and roster import | [PR #5](https://github.com/natezimm/know-ball/pull/5): 5,416 history players, 99 teams, 5,397 Wikidata links, 630 ESPN players; 30 Scouting Report and 29 Career Path puzzles initially queued |
| Historical four-game Playwright coverage (photo-game tests now removed) | [Merged PR #6](https://github.com/natezimm/know-ball/pull/6), [browser tests](../web/know-ball/e2e), successful e2e job in the main run above |
| Live-source import canary | [Successful run, 2026-10-17](https://github.com/natezimm/know-ball/actions/runs/39029555678); [workflow](../.github/workflows/nightly-import-check.yml) loads fresh disposable SQLite, not production Postgres |
| Version 1 verification script updated | [scripts/verify.sh](../scripts/verify.sh): live ESPN import, API checks and Docker smoke checks when Docker is available |

### Operations tooling (implementation, not production evidence)

Production audit, paired database/photo backups, a daily-backup cron installer and a disposable restore drill
are available through [deploy/operations.py](../deploy/operations.py) and the manual
[Production operations workflow](../.github/workflows/production-operations.yml). See
[deployment instructions](../deploy/README.md#7-production-evidence-backups-and-recovery) for the action choices.
The CI Docker job exercises a fixture backup/restore, with the three-game launch scope; legacy photo files are retained and verified as archived bytes when present, separately from the
production workflow. Until a production run and GCP snapshot review are recorded, the gates below remain open.
Import runs do not currently store their trigger; scheduled-run evidence requires log/timestamp correlation.

### Photo-game decision — October 20, 2026

The five prepared photos did not produce cutouts of acceptable quality. Who's That and its upload workflow
are removed from active scope. Earlier preparation is recoverable from Git history and the saved media packs;
no production data or saved packs are deleted. See [future games](future-games.md).

### Remaining launch checklist

| Done | Gate | Acceptance evidence to record |
| --- | --- | --- |
| [ ] | **Production nightly imports — Milestone 0** | Production `ImportRuns` rows and worker logs show two successive scheduled 04:00 America/New_York runs succeeding, with full history present and current-player headshots. Check October 17 and 18 first; if either fails, use the next successful consecutive pair. Distinguish scheduled runs from startup/manual imports and confirm puzzle queues were replenished. Use the query in [deploy/README.md, step 6](../deploy/README.md#6-first-deploy). |
| [ ] | **Backups configured** | Dated confirmation of the VM backup cron, a successful nonempty database dump, retention, and a GCP disk snapshot schedule covering database and photos. Setup is in [deploy/README.md, step 7](../deploy/README.md#7-nightly-backups). |
| [ ] | **Recovery verified** | Restore a database backup and matching photos into an isolated environment; record backup/snapshot timestamps, restored puzzle counts, verification of any archived legacy media and a game playthrough. Keep production untouched during the drill. |
| [ ] | **Real-phone gameplay — Milestones 1 and 4** | Record device/browser and date for iOS Safari and Android Chrome: play each available game, use player search/on-screen keyboard, reload/resume, finish and share; check scrolling and dialogs. |
| [ ] | **Installed PWA — Milestone 4** | On iOS and Android, install from the browser, launch from the home screen and verify an updated deployment reaches the installed copy. Record versions/commit and results. Shell caching does not provide offline gameplay; APIs remain network-only. |

**Release rule:** complete production operations and real-device/PWA checks for Spordle, Scouting Report and
Career Path. A fourth game and photo content are not release requirements. Desktop Chromium tests do not
close the real-phone or installed-PWA gates.

### Suggested sequence after this documentation update

1. Verify production imports, backups and recovery.
2. Complete real-phone and installed-PWA checks for all three active games.
3. Record the three-game release decision, then evaluate a fourth game separately.

### After launch (proposed, not scheduled)

- Daily Spordle on the shared engine, keeping the current random game as practice; define daily progress,
  stats and sharing before implementation.
- Use replay/return-play evidence to prioritize improvements and the next game (no fourth game is selected). The Phase 2/3 catalog remains unscheduled.
- Consider a third roster source only if both ESPN hosts prove insufficient.
- Optional local housekeeping: the old `.transfer.tar.gz` archive and `sports-trivia-db` container can be
  reviewed and removed on the development machine.
