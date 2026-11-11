# Team Photo: plan

Status: built in PRs #95 (data) and #96 (game), 2026-11. This page is the plan as written on 2026-11-09; where the
build differs, these decisions from Nathan win: launch with option A (starting five only, no bench row), and the
daily pool is all 100 NBA Finals teams since the 1976 merger, champions and runners-up, kept in
`Application/Games/TeamPhoto/Data/finals-teams.json`. The sketches have left the lab; `/mockups/team-photo` now opens
the game.

Team Photo shows a blank team photo of one NBA team-season, such as the 2015–16 Golden State team that went 73–9.
The player names the people in it. Each correct name puts a face in the photo. It is the first Know Ball game with
more than one answer per puzzle.

## The short answers

| Question | Recommendation |
| --- | --- |
| Starting lineup or more? | **The starting five is the puzzle.** These are the five players with the most games started for that team that season. Bench players are a bonus and never count as a miss. A bench row of three (option B in the mockups) can be added later if five turns out to be too easy. |
| Coach? | **Not in the first version.** We don't import coaches. The guess box searches players, and many coaches were players too (Steve Kerr, Doc Rivers), so a coach slot would need its own search and its own list. Later, the coach's name can go in the caption of the finished photo without being guessed. It would come from the same hand-made list as the puzzles. |
| NBA, NFL or both? | **NBA only at launch.** An NFL edition would be an "offense photo" (QB, RB, two WRs, TE, chosen by volume since 1999) as a follow-up, using the league tabs the other games already have. |
| How many misses? | **Four misses.** Correct names and bench names are free. The counter just reads "Misses 1 of 4", with no extra warning on the last one. |

## How a round plays

1. The header plaque shows the season, the city and the record: "2015–16 Golden State · 73–9". The team is the
   prompt, so it isn't a secret.
2. The photo has five blank figures. The back row holds the forwards and the center, and the front row holds the
   guards. Each blank shows its position (from that season's listing) and that season's stat line, such as
   "PF · 14.0 pts · 9.5 reb · 7.4 ast".
3. The player types into the same player search as the other games. It includes retired players and is not
   narrowed to the season, because narrowing it would give too much away. Each guess gets one of three results:
   - **A starter.** The face fills the slot and the nameplate turns gold.
   - **On the team, not a starter.** A note says "Andre Iguodala came off the bench". This is not a miss.
   - **Not on the team.** It counts as a miss: "Kevin Durant · not on this team".
4. The round is **won** when all five starters are named. It is **lost** at the fourth miss. Either way the full
   photo is shown, with a caption that lists the players left to right.
5. Share text uses Know Ball's own marks, one per slot in photo order, with no team name so nothing is spoiled:

   ```
   Know Ball · Team Photo #12
   ●●●○● · 2 misses
   ```

A guessed player can't be guessed again (the engine already rejects this). Guesses are idempotent through
`requestId` like every other game.

## Data: what we have and what's missing

What we have today (Basketball-Reference mirror, `BrefCsvSource`):

- One `PlayerSeasonStat` row per player, team and season. A traded player gets a row for each team, so Rasheed
  Wallace counts for Detroit in 2003–04 with the 21 games he started there.
- Games started (`gs` in `StatsJson`), games played, minutes, points, rebounds and assists.
- Headshots, already hot-linked from the NBA's CDN like the other games, with the initials fallback.

Checked against the mirror's `Player Per Game.csv` on 2026-11-09:

- `gs` is recorded from 1981–82 onward. Before that, about 15% of rows have it. **The pool starts at 1981–82.**
- There are 1,269 NBA team-seasons from 1981–82 to 2025–26. 774 of them (61%) have a **clear** starting five,
  meaning the fifth starter started at least 30 games and at least 10 more than the sixth.
- Most starts matches the lineup people remember for most famous teams: the 1995–96 Bulls, the 2015–16 Warriors,
  the 1985–86 Celtics, the 2007–08 Celtics and the 2013–14 Spurs. It misses where a lineup changed during the season:
  the 2003–04 Pistons come out with Mehmet Okur instead of Rasheed Wallace. That is why the curated list below can
  override a lineup.

Gaps to fill:

| Gap | Fix | Size |
| --- | --- | --- |
| Position for that season (slot labels, front or back row) | Keep the mirror's `pos` column in `StatsJson` (`"pos": "PF"`). Needs a history reload in production, because the history refresh runs every 30 days. | Small |
| Team record (73–9) and playoff flag | Read the mirror's `Team Summaries.csv` (w, l, playoffs) into a new `TeamSeason` table. Needs a Postgres migration and a SQLite schema version bump. | Small |
| Coach | Not imported. Leave it out of version 1. Later, add it to the curated list for the caption only. | Later |
| Team colours for the backdrop | Not needed. The backdrop uses site colours, so no team colours are needed. | None |

## Choosing puzzles

**Daily puzzles come from a curated list. Practice comes from the clear lineups.**

- `Infrastructure/Data/team-photo-seasons.json` lists about 150 team-seasons: every champion since 1981–82 (45),
  the 60-win teams, and memorable runs (the 2006–07 Golden State "We Believe" team, the 2011–12 Clippers, the
  2018–19 Raptors and so on). Each entry is a team code and season, plus an optional `starters` override with
  a one-line reason ("Wallace started every playoff game"). That covers about five months of dailies before
  anything repeats. The list can grow over time.
- On every import, a check flags any list entry whose computed lineup is unclear and has no override. A unit test
  fails if the shipped list contains one, so the list stays honest.
- Practice draws from any clear-lineup team-season since 1981–82 whose five starters have a combined popularity
  above a set cutoff, so nobody gets the 1988–89 Heat (15–67) by chance.
- Repeats: `AnswerPlayerId` is set to the most popular starter. The scheduler's existing 120-day no-repeat rule
  then keeps the same star from showing up two weeks in a row. The same team-season is never used twice in a
  year, and the same franchise isn't used two days in a row. Both are checked from the puzzle's payload key.
- Difficulty is stored like the other games (older seasons and less popular starters are harder). The scheduler
  doesn't use it yet.

## Server design

Team Photo can stay inside the shared engine instead of becoming a separate service like Numbers Don't Lie or
Letter Jacket. That way it gets the daily calendar, streaks, stats, the archive, practice and idempotent guesses
for free. The engine needs two small, general changes:

1. `IGame.MaxMisses`, an optional value (null means today's behaviour, where the limit counts every guess). Team
   Photo returns 4 with no guess cap.
2. `GuessEvaluation` gains `Solves` (default: same as `Correct`) and `Counts` (default true; false for a bench
   name, so it isn't a miss). `GameService.GuessAsync` sets `Solved` when an evaluation solves the puzzle, and
   sets `CompletedAt` when solved or out of misses. Today's games keep their behaviour exactly, and the existing
   tests prove it.

The game itself:

- `GameType.TeamPhoto = 10`, slug `team-photo`. 2 is never reused, and 4 to 7 are old working names.
- `PayloadJson` (public) holds the team, season, record, logo, and five slots with position, row and stat line,
  plus the bench count. It holds no names or ids.
- `SecretJson` holds the starter ids by slot and the bench ids.
- `EvaluateAsync` finds the guess among the slots or the bench and returns feedback `{ "slot": 2 }` or
  `{ "bench": true }`. A slot is filled only after its guess is evaluated, so the photo's filled faces come from
  guesses already made. That keeps [ADR 0002](../adr/0002-answers-stay-on-the-server.md) intact: no unguessed
  name leaves the server before the round ends.
- `Reveal` gives all five plus the bench, for the finished photo.
- The share result is the dots above. `Score` is the number of starters named.

## Web

- Route `/team-photo`, a new folder `games/team-photo`, and a home card with its own simple icon. That makes
  seven games, so the home grid's last row is centred, which follows the existing grid rules.
- The layout follows mockup option A: the guess box on top (as in Scouting Report), then the photo, the
  "3 of 5 · Misses 1 of 4" line and the latest feedback. The photo is a framed card with a bleacher backdrop and
  dashed figures that turn into headshots. It fits a 320px phone with no sideways scroll (checked on the
  mockup).
- The finished photo is option C: every face, the caption, and Share and Stats.
- Copy explains it in one line: "Name the five who started the most games for this team."
- `core/page-meta.ts` data, `public/sitemap.xml` and `docs/api.md` gain the new route.

The sketches are at `/mockups/team-photo` on any build (unlinked and noindex; source in
`web/know-ball/src/app/mockups/games/team-photo.ts`). Option A is the launch layout,
option B adds the bench row, and option C is the finished photo.

## Name and originality

- **Team Photo**: on 2026-11-09 a web search found no game or app by that name. The trademark mirrors show no
  TEAM PHOTO mark. The nearest are photography services (TEAMFOTOG, MY TEAM PHOTOGRAPHER). It describes what you
  see rather than borrowing anything. Recommended, and to be recorded in
  [research.md](../research.md#naming-the-games).
- Naming starting lineups is a common quiz format (user quizzes on Sporcle and JetPunk, a "lineup guesser" on a 2K
  fan site). A format is free to use. We avoid "Starting Five", "Starting 5" and "Lineup" in the name because
  those are crowded, and we don't name any of those sites in copy.
- No real team photos: the photo is drawn, and only the official headshots fill it. Logos stay hot-linked as on
  the other games. The footer's non-affiliation line already covers this.

## Tests

- **Unit** (`KnowBall.UnitTests`): lineup by most starts, including a traded player's per-team row; the
  clear-lineup rule; overrides; a check that the shipped curated list is all clear or overridden; slot evaluation
  (starter, bench, miss, repeat); the engine changes (`MaxMisses`, `Solves`, `Counts`), plus checks that the
  other games behave the same; payload carries no names or ids; scheduler repeat rules; `TeamSeason` import and
  the `pos` key, with a second run changing nothing.
- **Integration** (SQLite and Postgres): daily and practice over HTTP, an unfinished response has no answer, a
  win, a loss at four misses, a bench guess doesn't count, and a repeated `requestId` returns the same state.
- **Fixtures**: add a fake team-season to `tests/fixtures/bref` with eight players and clear starts, plus a
  `Team Summaries.csv`, so e2e can load it offline.
- **E2E** (Playwright): win and lose a round, check the bench note and check no sideways scroll at 320px.

## Pull requests, in order

1. **Data**: keep `pos` in `StatsJson`; import `Team Summaries.csv` into `TeamSeason` (migration, SQLite version
   bump); fixtures; importer docs. After it deploys, Nathan runs one history reload on the VM.
2. **Engine**: `MaxMisses`, `Solves` and `Counts` in `GameService`, with tests showing the other games don't
   change. No new game yet.
3. **Game and API**: `GameType.TeamPhoto`, the curated list, the lineup builder, evaluation and reveal, practice
   pool, unit and integration tests, and `docs/api.md`.
4. **Web**: the page, the home card, routes, the sitemap, the e2e tests, and removing the sketch from
   `/mockups` (its old address redirects to the game, as Back of the Card's does).
5. **Later, if wanted**: the bench row (option B), the coach in the caption, and the NFL offense photo.

## Decisions for Nathan

1. **Mockup**: option A (five only, bench names are free) or B (five plus a bench row of three)? The
   recommendation is A, with B later if it's too easy.
2. **Coach**: leave it out for now and maybe show it in the caption later? The recommendation is yes.
3. **Misses**: four?
4. **Name**: Team Photo?
5. **NFL**: NBA only at launch?
