# Data source research

A log of what was looked at, what was actually tested, and what was decided. Last updated 2026-10-13, end of day.

Each finding is marked by how it is known:

- **Tested**: I called it from this machine on the date shown and saw the result.
- **Docs**: read on the provider's own page.
- **Reported**: a third-party page or forum said so; not confirmed.

## Decisions

| League | Player history (bulk) | Nightly refresh | Id links and hometowns | Hand-entered |
| --- | --- | --- | --- | --- |
| NBA | Basketball-Reference CSV mirror | ESPN roster feed, on either of its two hosts (balldontlie as a later fallback, not built) | Wikidata | Champions by season (not entered yet) |
| NFL | nflverse | nflverse (it updates daily); ESPN roster feed as a cross-check | nflverse's own id columns, plus Wikidata for birthplaces | MVPs, Super Bowl winners before 1999, stadium coordinates |

No paid API is needed for either league. No API key is needed either.

## NBA

### Sources tested

| Source | Result | How known |
| --- | --- | --- |
| stats.nba.com (the importer's original source) | No response: 60s timeout in the importer, 25s and 20s with curl | Tested 10-10 and 10-12 |
| stats.nba.com | Every request has returned an Akamai error page since about 2026-10-07, on any IP | Reported ([nba_api issue 696](https://github.com/swar/nba_api/issues/696)) |
| nba.com player pages and `cdn.nba.com/static/json` feeds | 403 | Tested 10-10 |
| NBA headshot CDN, `cdn.nba.com/headshots/nba/latest/{size}/{nbaId}.png` | Works, including retired players (Jordan 893, Bryant 977, Abdul-Jabbar 76003) | Tested 10-10 and 10-13 |
| NBA logo CDN, `cdn.nba.com/logos/nba/{teamId}/primary/L/logo.svg` | Works for the 30 current teams | Tested 10-12 |
| ESPN roster feed, `site.api.espn.com/apis/site/v2/sports/basketball/nba/teams/{id}/roster` | 30 teams, 625 players, no key. Birth date 100%, height 100%, position 100%, jersey 97%, headshot 96%, birthplace 96% | Tested 10-13 |
| ESPN alternate host `site.web.api.espn.com` | Same data, 200 | Tested 10-13 |
| balldontlie | API is up; returns 401 without a key | Tested 10-10 |

### Basketball-Reference CSV mirror (the NBA backbone)

[sumitrodatta/bball-reference-datasets](https://github.com/sumitrodatta/bball-reference-datasets), folder `Data/`, also published as
[NBA Stats (1947-present)](https://www.kaggle.com/datasets/sumitrodatta/nba-aba-baa-stats) on Kaggle. Tested 10-12: files download
directly from GitHub with no account. Last push 2026-04-13; includes the 2025-26 season. The repo has no licence file.

| File | Columns that matter |
| --- | --- |
| `Player Career Info.csv` | `player_id` (the Basketball-Reference slug), position, height in inches, weight, `birth_date`, colleges, first and last season, Hall of Fame flag |
| `Player Per Game.csv`, `Player Totals.csv` | One row per player, season and team: games, minutes, points, rebounds, assists and the rest |
| `Player Season Info.csv` | Age, team, position, years of experience by season |
| `All-Star Selections.csv` | Player, season, team |
| `End of Season Teams.csv` | All-NBA, All-Defense, All-Rookie, with first, second or third team |
| `Player Award Shares.csv` | MVP, DPOY, ROY and other votes, with a `winner` flag |
| `Draft Pick History.csv`, `Team Summaries.csv`, `Team Abbrev.csv` | Draft slots; arenas and playoff flags; team code mapping |

Quirks: seasons are named by end year (2026 means 2025-26); team codes differ from the NBA's (BRK, CHO, PHO); a traded player
has several team rows per season.

### Wikidata (the NBA crosswalk)

Tested 10-12 and 10-13 with SPARQL against `query.wikidata.org`.

- 5,518 people have an NBA.com player id (property P3647). Of those, 5,517 also have a Basketball-Reference id (P2685), all
  have a birth date, and 5,321 have a birthplace with coordinates.
- 9,718 people have a Basketball-Reference id; 5,384 of them have an ESPN NBA id (P3685).
- Spot check: LeBron James returns NBA id 2544, Basketball-Reference id `j/jamesle01`, born 1984-12-30 in Akron with coordinates.

### balldontlie NBA

Docs, read 10-12 at [nba.balldontlie.io](https://nba.balldontlie.io/); prices Reported on 10-10.

| Tier | Price | Rate | NBA endpoints |
| --- | --- | --- | --- |
| Free | $0 | 5 requests/min | Teams, players, games |
| ALL-STAR | $9.99/month per sport | 60/min | Adds active players, per-game player stats, injuries |
| GOAT | $39.99/month per sport | 600/min | Adds season averages, standings, leaders, box scores and more |
| All-Access | $299.99/month | 600/min | Everything, all sports |

- Player fields: id, names, position, height, weight, jersey number, college, country, draft year, round and number, team.
  No birth date, no NBA.com id, no headshot.
- No endpoints for awards, team history or images.
- Terms summary says the data may be used for any lawful personal or commercial purpose
  ([terms](https://www.balldontlie.io/terms.html)).

Why it is now only a fallback: ESPN's feed gives the same roster facts plus birth date, headshot and birthplace, with no key.

### Paid providers considered and set aside

All Reported, from searches on 10-10.

| Provider | Cost | Notes |
| --- | --- | --- |
| API-Sports | Free at 100 requests/day; players appear to need a paid plan at roughly $15 to $35/month | Sources disagree on the prices |
| SportsDataIO | Not public; sales process | Its own docs say the free trial covers only one soccer competition; the cheap Discovery Lab tier is not licensed for commercial use |
| Sportradar | Not public; estimates of $10,000+/month | Trial is non-commercial. Its terms say it does not own headshots or logos and cannot grant rights to them |

### NBA gaps

| Missing | Workaround |
| --- | --- |
| Championship rings per player | Hand list of champions by season, joined to that season's roster |
| Jersey number for each past team | None found; Career Path uses other hints |
| Logos for defunct teams (Sonics, New Jersey Nets) | Text badge |
| Current-season stats during the season | The mirror updates a few times a year; fine for history, not for "this week" questions |

## NFL

### nflverse (the NFL backbone)

[nflverse/nflverse-data](https://github.com/nflverse/nflverse-data/releases), published as release files. Tested 10-12 and 10-13.
The repo's licence is CC-BY 4.0. Most files were updated on 2026-10-12 or 10-13, so it works as the nightly source too.

| File | Coverage | What it holds |
| --- | --- | --- |
| `players/players.csv` | 24,853 players, rookie seasons 1974 to 2026 | Names, position, height, weight, birth date (99%), college (99%), jersey number (95%), headshot URL (99%), first and last season, latest team, draft slot. Ids: NFL `gsis_id` (100%), Pro-Football-Reference `pfr_id` (91%), ESPN `espn_id` (66%) |
| `rosters/roster_{year}.csv` | 1920 to 2026 | Every team's roster by season: the source for Career Path. Rows before 1974 have name, birth date, height, weight and headshot but no ids (Jim Brown and Johnny Unitas checked in 1960) |
| `stats_player/stats_player_reg_{year}.csv` and `_post_` | 1999 to 2026 | Season totals: passing, rushing, receiving and defensive stats, with team and position |
| `draft_picks/draft_picks.csv` | Drafts from 1980 | Draft slot plus career summary: Hall of Fame flag, All-Pro count, Pro Bowl count, Approximate Value, career passing, rushing and receiving totals |
| `teams/teams_colors_logos.csv` | Current teams | Conference, division, colours, logo URLs |
| `schedules/games.csv` | 1999 to 2025 seasons | Every game with stadium name; Super Bowl rows give champions from the 1999 season on |
| Others | Varies | Trades, contracts, combine results, depth charts, injuries, snap counts |

Counts: 2,621 players have 2026 as their last season, and 2,601 of them have a headshot.

### Other NFL sources tested

| Source | Result | How known |
| --- | --- | --- |
| ESPN roster feed, `.../sports/football/nfl/teams/{id}/roster` | 32 teams, 2,552 players, no key. Height 100%, position 100%, birth date 99%, headshot 99%, birthplace 99%, jersey 94% | Tested 10-13 |
| Wikidata | 28,904 people have a Pro-Football-Reference id (P3561): 28,429 with a birth date, 24,009 with birthplace coordinates. Only 2,849 have an ESPN NFL id | Tested 10-13 |

### NFL APIs considered and set aside

| Provider | Notes | How known |
| --- | --- | --- |
| balldontlie NFL | Free: teams, players, games. $9.99/month adds active players and season stats; $39.99 adds rosters and advanced stats. Data from 2002. Player object has an integer age but no birth date, headshot or outside ids | Docs, 10-13 ([nfl.balldontlie.io](https://nfl.balldontlie.io/)) |
| MySportsFeeds | Describes itself as free for non-commercial use, but its pricing page lists NFL from $5/month for personal use | Reported |
| API-Sports American football | Free at 100 requests/day | Reported |
| TheSportsDB | Free key, crowd-sourced artwork; whether it has NFL player cut-outs was not confirmed | Reported |
| ESPN feed reliability | In August 2026 some users got 403s on `site.api.espn.com` and switched to `site.web.api.espn.com`. Both worked here on 10-13 | Reported, then Tested |

nflverse covers more than any of these, for free, so none is needed.

### NFL gaps

| Missing | Workaround |
| --- | --- |
| Awards (MVP, Offensive and Defensive Player of the Year, Super Bowl MVP) | Hand lists; MVP is about 70 rows |
| All-Pro and Pro Bowl counts for undrafted players and anyone drafted before 1980 | Not available; affects GOAT and popularity for older players |
| Season stats before 1999 | Not available; Stat Quiz and Sportle start at 1999 |
| Ids for players before 1974 | Match on name plus birth date |
| Super Bowl winners before the 1999 season | Hand list, 33 rows |
| Stadium coordinates | Hand list, 30 stadiums |

### What each game needs for the NFL

| Game | Ready from nflverse? | Design change from the NBA version |
| --- | --- | --- |
| Scouting Report | Yes | Conference is AFC or NFC; position has ten values. The answer pool should be limited to well-known players, since rosters hold about 1,700 |
| Who's That Sportsman? | Yes; legends before 1974 come from season rosters | None |
| Career Path | Yes, back to 1920 | None |
| Stat Quiz | Yes, from 1999 | Question templates per stat family (passing, rushing, receiving, defence) |
| Geo Sports | After the stadium list is entered; hometowns from Wikidata | None |
| Sportle | Yes, from 1999 | A different stat line per position group |
| Sound It Out, Spordle | No player stats needed | None |
| Who's the GOAT? | Partly | Comparing a quarterback with a linebacker needs a position-neutral number. Approximate Value is the only one available, and only for players drafted since 1980. Likely a per-position ranking instead |

## Images

- **Who's That photo catalog**: [selected photos, attribution text and iconic-moment search backlog](whos-that-photo-catalog.md),
  researched 2026-10-18. Prepare LeBron's chalk toss first, then Dirk's fadeaway; other exact moments remain sourcing leads.
- **Headshots**: NBA CDN by NBA id (current and retired), ESPN by ESPN id (current), NFL.com URLs in nflverse (99% of players).
- **Logos**: NBA CDN for the 30 current teams; ESPN logo URLs for NFL teams are listed in nflverse.
- **Iconic photos**: no feed exists; they are collected and cut out by hand. Licensed photo libraries (AP, Getty, Imagn) are the
  commercial route, per Sportradar's own terms.
- For curated Who's That photos, track each file's license and attribution in the catalog above; the selected
  LeBron and Dirk sources have explicit adaptation licenses. Other image sources have separate terms.

## Spordle word lists

Built 2026-10-13.

- **Answers**: 555 sports words written by hand in 26 categories: 152 of four letters, 207 of five and 196 of six.
  The categories double as the game's category clue, so they are deliberately broad ("Golf", "NFL team",
  "Scores, results & standings"). File: `src/KnowBall.Application/Games/Spordle/Data/sports-words.json`.
- **Allowed guesses**: the 40,389 words of four to six letters from the `word-list` npm package, version 4.1.0
  (MIT licence), the same dictionary Nerdle validates against. Copied from Nerdle's installed copy into
  `guess-words.txt` beside the answers.
- 13 answers are not in that dictionary (armbar, astros, bucs, cavs, habs, mavs, nascar, niners, padel, popup,
  preds, tapout, texans). They are still accepted as guesses, because every answer is.
- Nerdle itself (`../NathanZimmerman.com/nerdle`) was the reference for rules and API shape: 81, 99 and 85 tech
  words of four, five and six letters, six attempts, in-memory games with a six-hour lifetime.

## Theme references

Looked at on 2026-10-13 for the theme comparison at `/mockups/themes`. Values read from each page's computed styles.

| Site | Page and ink | Accents | Type | Shape language |
| --- | --- | --- | --- | --- |
| boringcashcow.com | Teal page `#43c1b3`, ink `#221e26`, white cards | Yellow `#ffd43c`, pastel pink and yellow badges | Baloo 2 (800) for headings, DM Sans for text | Rounded corners, pills, 2px dark outlines, small hard offset shadows, tilted sticker cards |
| sudoku.nathanzimmerman.com | Paper `#efefe6`, black ink, white panels | Yellow `#ffdb3b`, mint `#3bff9d`, lime `#c2ff66`, error red `#ff3333` | Space Grotesk, with IBM Plex Mono for labels | Square corners, 2px black rules, hard black offset shadows; has a dark mode that inverts paper and ink |

Outcome: Trading Card (I) was chosen on 2026-10-13 and applied.

Both sit in the same family (flat colour, dark outlines, hard shadows); one is playful and round, the other strict
and square. Themes B (Sticker Pop) and C (Paper Grid) follow them. The other candidates were chosen to be as far
from those two, and from each other, as possible: Scoreboard, Chalk Talk, Back Page, Arcade, Broadcast, Trading Card.

## Naming the site

"Spordle" became the name of the word game on 2026-10-13, so the site needed its own name. **Decided 2026-10-13: Know
Ball.** The shortlist below was checked 10-13 by searching for existing sports trivia products and by DNS lookup. A search finding
nothing is not proof a name is free, and "no record" means the registry returned no such domain.

| Name | Idea | Existing sports trivia product | `.com` | `.app` | `.gg` |
| --- | --- | --- | --- | --- | --- |
| Know Ball | "You know ball"; already the home page headline | None found. One 2018 trademark filing, "KnowBall.us Sports Trivia League", with no live product found | Taken | No record | Taken |
| Full Slate | "A full slate of games": many games, every day | None found | Taken | Taken | No record |
| Triviathlon | Trivia plus triathlon: several events | None found | Taken | Taken | No record |
| Deep Bench | A deep bench of games | None found | Taken | No record | No record |
| Daily Reps | Getting your reps in | None found | Taken | Taken | No record |
| Beat the Buzzer | Trivia buzzer plus buzzer-beater | No daily game; "Buzzer Beater Trivia" is a corporate team-event product | Taken | No record | No record |
| The Back Page | Sports on the back page, beside the puzzles | None found | Taken | No record | No record |
| On the Clock | The draft phrase; a daily timed puzzle | None found | Taken | Taken | No record |

Ruled out because the name is in use for sports trivia:

- **Ball Knowledge**: two NBA trivia apps and a soccer one.
- **Ball IQ**: a soccer trivia site at balliq.app, and the subtitle of an NBA app.
- **Film Room**: "The Film Room: NFL Trivia".
- **Heat Check**: a 2026 sports trivia show from Whistle.

## Findings while building version 1

All Tested on 2026-10-13 unless marked otherwise.

### Live import, end to end

- A fresh SQLite database filled in 30 to 48 seconds. The Basketball-Reference mirror gave 5,416 players and 99
  team identities; Wikidata updated 5,397 of them; ESPN gave 629 current players, of whom 511 matched a player
  in the history, 117 were new (mostly rookies and two-way players) and 97 history players were marked inactive.
  A second run changed nothing.
- Only 6 ESPN players with NBA experience failed to match the history, all G League and two-way types.
- Matching order: a known source id, then the Basketball-Reference id, then name plus birth date (tolerating
  suffixes like "Jr." and initials), then a unique name when a birth date is missing.

### Wikidata

- One query for ids, birth dates and birthplace names together ran past the 60-second limit. The service still
  answered HTTP 200, with about 7,400 of 9,700 rows followed by a Java stack trace, so a CSV reader would have
  taken the truncated list as complete.
- Fix: two queries, ids (about 5 seconds) and birthplaces (about 28 seconds), both read as JSON so a cut-off reply
  fails to parse instead of loading silently. A failed birthplace query does not fail the import.

### Basketball-Reference team codes

- Former team identities (Seattle SuperSonics, New Jersey Nets, ABA teams and so on) are kept as their own teams,
  69 of them, each linked to the franchise that exists today or existed last. The list is
  `src/KnowBall.Infrastructure/Data/nba-team-history.json`.
- Some franchises changed code without changing identity; these are treated as one: INA and IND, DNA and DEN,
  SAA and SAS, NYN and NYA, CHH and CHO.
- Codes that differ from the NBA's: BRK is BKN, CHO is CHA, PHO is PHX.

### ESPN

- The roster feed's season type tells whether the season is on (2 regular season, 3 playoffs). Out of season,
  when a player changes team, the old stint ends with the previous season rather than the current one.
- A player who sits out a season and comes back to the same team gets the old stint reopened, not a second one.

### Image library for Who's That Sportsman?

- ImageSharp 3.1.12 showed high-severity vulnerability warnings from NuGet at restore (Tested). Its version 3
  licence, the Six Labors Split License, also puts conditions on commercial use (from memory, not re-checked).
- SkiaSharp 3.119 (MIT) is used instead, with `SkiaSharp.NativeAssets.Linux.NoDependencies` for the Linux image.
  It loads on macOS; on Linux it is not yet tested (see the plan's section 8).
- The silhouette keeps the cut-out's transparency and paints every visible pixel black. The blurred version
  shrinks the photo to a 20-pixel grid and stretches it back, which hides the face but keeps colours and shapes.

### Service worker

- In the Claude desktop app's built-in browser, the Angular service worker failed to register (error NG05604,
  "An unknown error occurred when fetching the script") even though `ngsw-worker.js` itself loads with status 200
  and a JavaScript content type, and manual registration failed the same way. This looks like a limit of the
  embedded browser; whether the site installs as an app is unconfirmed until it is tried in Safari or Chrome.

## Re-running the checks

```bash
# ESPN rosters (swap football/nfl for the NFL)
curl -s "https://site.api.espn.com/apis/site/v2/sports/basketball/nba/teams/1/roster" | head -c 400

# nflverse file list and one file's columns
curl -s "https://api.github.com/repos/nflverse/nflverse-data/releases?per_page=100" | grep '"tag_name"'
curl -sL "https://github.com/nflverse/nflverse-data/releases/download/players/players.csv" | head -1

# Basketball-Reference mirror columns
curl -s "https://raw.githubusercontent.com/sumitrodatta/bball-reference-datasets/master/Data/Player%20Career%20Info.csv" | head -2

# Wikidata: how many NBA players have ids, birth dates and birthplaces
curl -s -G "https://query.wikidata.org/sparql" -H "Accept: text/csv" \
  --data-urlencode 'query=SELECT (COUNT(DISTINCT ?p) AS ?n) WHERE { ?p wdt:P3647 ?nba ; wdt:P2685 ?bref ; wdt:P569 ?dob . }'

# Is stats.nba.com back?
curl -s -m 20 -o /dev/null -w "%{http_code}\n" -H "Referer: https://www.nba.com/" \
  "https://stats.nba.com/stats/playerindex?LeagueID=00&Season=2026-27"
```

## Sources

- [sumitrodatta/bball-reference-datasets](https://github.com/sumitrodatta/bball-reference-datasets) and its [Kaggle page](https://www.kaggle.com/datasets/sumitrodatta/nba-aba-baa-stats)
- [nflverse-data releases](https://github.com/nflverse/nflverse-data/releases), [nflreadr docs](https://nflreadr.nflverse.com/), [nflverse-players](https://github.com/nflverse/nflverse-players)
- [balldontlie NBA docs](https://nba.balldontlie.io/), [NFL docs](https://nfl.balldontlie.io/), [terms](https://www.balldontlie.io/terms.html)
- [nba_api issue 696](https://github.com/swar/nba_api/issues/696), [issue 498](https://github.com/swar/nba_api/issues/498), [static players docs](https://github.com/swar/nba_api/blob/master/docs/nba_api/stats/static/players.md)
- [ESPN hidden API notes](https://gist.github.com/akeaswaran/b48b02f1c94f873c6655e7129910fc3b), [2026 guide](https://sportsapis.dev/espn-api)
- [API-Basketball pricing](https://www.api-basketball.com/pricing), [API-Sports](https://api-sports.io/)
- [SportsDataIO NBA docs](https://sportsdata.io/developers/api-documentation/nba), [Discovery Lab](https://discoverylab.sportsdata.io/personal-use-apis/nba)
- [Sportradar terms](https://developer.sportradar.com/sportradar-updates/page/terms-and-conditions), [NBA addendum](https://developer.sportradar.com/sportradar-updates/page/nba-addendum), [images FAQ](https://developer.sportradar.com/images-and-editorials/reference/images-faqs)
- [MySportsFeeds pricing](https://www.mysportsfeeds.com/feed-pricing/), [TheSportsDB API](https://www.thesportsdb.com/free_sports_api)
