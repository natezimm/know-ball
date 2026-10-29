# Numbers Don't Lie: source proof and review

Checked October 22, 2026 (UTC). Delivery PR 1. **Status: 24 source-checked prototype questions; not approved production content.**

October 23: the user requested hosting the existing review sample for on-site testing. The preview now
publishes these 24 questions and credits at `/preview/numbers-dont-lie/index.html`; it is labeled as a
sample and contains its answers in client assets. This is a packaging decision for the experiment,
not a new finding about source permissions or approval of the future production catalogue.

The [playable prototype](../prototypes/numbers-dont-lie/README.md) tests five untimed questions, four choices,
feedback after submission and daily versus league-selectable practice. The source values are checked;
human editorial review, difficulty calibration, physical-phone review and the four-league production
source gate remain open. This is not a claim of 24 human-approved questions or launch readiness.

## Evidence and source decisions

| League | Exact evidence used | Retrieval and coverage | Production gate |
| --- | --- | --- | --- |
| NBA | [Kings 2024–25 media guide](https://cdn.nba.com/teams/uploads/sites/1610612758/2024/10/2024-25SacramentoKingsMediaGuide.pdf), printed page 89, “2023-24 NBA Individual Leaders” | PDF downloaded; all 24 selected values checked against total columns. Six different metrics in one completed season. No multiplication of rounded per-game rates. | [NBA terms](https://www.nba.com/termsofuse): restricted uses and attribution requirements; not an open-data licence. Public-game reuse remains unresolved. |
| NFL | [2022 CSV](https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_2022.csv), [2023 CSV](https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_2023.csv), [release](https://github.com/nflverse/nflverse-data/releases/tag/player_stats) | Both downloaded. Filter season_type=REG, group by player_id, sum weekly passing/rushing/receiving yards. Independent script checks all 24 selected values and duplicate weeks. | [nflverse licence](https://github.com/nflverse/nflverse-data/blob/main/LICENSE.md): CC BY 4.0. Credit contributors, link dataset/licence, describe aggregation and retain notices. No warranty or endorsement. This proof uses player_stats, not FTN. |
| MLB | [2022 season hitting API](https://statsapi.mlb.com/api/v1/stats?stats=season&group=hitting&season=2022&sportIds=1&limit=300), [2023](https://statsapi.mlb.com/api/v1/stats?stats=season&group=hitting&season=2023&sportIds=1&limit=300) | Both downloaded. All 24 selected integer values checked against player season rows; matching rows unique. The 300-row response is not a completeness proof. | [MLB terms](https://www.mlb.com/official-information/terms-of-use): no open-data licence established. Resolve production retention and reuse before ingestion. |
| NHL | [2022–23 final statistics](https://media.d3.nhle.com/image/private/t_document/prd/vl7oourkyjfv0ms2f5w4.pdf), PDF pages 14–15; [2023–24 recap](https://media.nhl.com/site/vasset/public/attachments/2024/04/17943/RegularSeasonRecap.pdf), PDF pages 1, 4–6 | Retrieved/read through web research; all 24 chosen values checked. Direct downloads and both stats API hosts returned 403 in this runner. No reproducible binary snapshot obtained. | [NHL terms](https://www.nhl.com/info/terms-of-service): no open-data licence established. Repeatable ingestion and production reuse remain open. |

The prototype bundles only the small, manually selected factual comparisons and original question text,
not full CSVs, API responses, report prose, photos, logos or PDF files. Credits are shown with every revealed
answer and retained in question JSON. Attribution is not a substitute for resolving the production gate.
Before public release, establish a source arrangement that supports the intended stored catalogue for every league.
No supplier requests are made while playing.

### Reproducibility

Files below are transient research inputs, deliberately excluded from git. Download to a separate folder
using the exact URLs above, preserving these filenames. Run:

```bash
python docs/prototypes/numbers-dont-lie/verify-source-values.py /path/to/snapshots
```

| Local filename | Bytes | SHA-256 observed |
| --- | ---: | --- |
| nbaguide.pdf | 12675472 | `037541fa9ced1a0aabef7df628f2db856e0a49f22a8c757bb25552a4c9a5c65c` |
| nfl22.txt | 1689929 | `42f3a4da856546bd9606dde29eecf54e8dbbd6627264bbd8e153dd34ecd0e535` |
| nfl23.txt | 1695570 | `fc731277c81c7ee25fb6af50cebd29a2adf3f8aadddeec1c485231aa9f98bdcc` |
| mlb22.txt | 135568 | `2db3c2fb6c7370028dd2e753a45fcbe1973387cff3fab6ba6d6eb1692f8ac742` |
| mlb23.txt | 139804 | `83e490eba827078a620216a5af087c2ce0bc4f8b5ac8ec97640e62c823c835d1` |

These are observed response checksums, not a promise that a mutable URL always returns those bytes.
NFL CSVs include postseason rows: summing without REG filtering is incorrect. MLB responses include a
copyright/terms notice; keep it with any permitted retained raw snapshot. NBA review used the PDF's exact
total columns, including three-pointers made (not three-point percentage). NHL totals were read in the reports,
not guessed from search snippets. That manual verification does not establish an automated NHL feed.

### Rejected or incomplete leads

- NBA HTML leader pages returned identical shell HTML for two different season queries in this runner;
  do not parse that as historical stats. The official team media guide supplied verifiable totals.
- NHL `api.nhle.com/stats/rest/en/skater/summary` and
  `api-web.nhle.com/v1/skater-stats-leaders/{season}/2` returned 403. A search-indexed report is useful
  for manual verification, but not evidence that production requests will work.
- The existing Basketball-Reference mirror is not a four-sport source. No assumption that the mirror's
  availability resolves upstream rights.
- Lahman historical baseball data remains a possible alternative; its current download, coverage and
  licence were not verified here. It is not the source of this sample.

## Question review ledger

Every question explicitly says **“Among these four”** and specifies league, completed season and regular-season
scope. Each has four distinct names and one valid answer. Ties between distractors are allowed;
ties involving the correct maximum or target value are rejected. Source links, dates, values, revision,
fact-family ID, template and provisional difficulty are in [questions.json](../prototypes/numbers-dont-lie/questions.json).
No question has production-approved status.

| Question | Template | Correct choice | Checked supporting values |\n| --- | --- | --- | --- |\n| `nba-points-2024` | compare-four | Luka Dončić | Luka Dončić: 2,370; Shai Gilgeous-Alexander: 2,254; Giannis Antetokounmpo: 2,222; Jalen Brunson: 2,212 |
| `nba-rebounds-2024` | match-value | Domantas Sabonis | Nikola Jokić: 976; Domantas Sabonis: 1,120; Rudy Gobert: 982; Anthony Davis: 961 |
| `nba-assists-2024` | compare-four | Tyrese Haliburton | Nikola Jokić: 708; Domantas Sabonis: 673; Tyrese Haliburton: 752; Luka Dončić: 686 |
| `nba-blocks-2024` | compare-four | Victor Wembanyama | Walker Kessler: 154; Brook Lopez: 189; Chet Holmgren: 190; Victor Wembanyama: 254 |
| `nba-steals-2024` | match-value | De’Aaron Fox | De’Aaron Fox: 150; Alex Caruso: 120; Matisse Thybulle: 113; Kawhi Leonard: 111 |
| `nba-threes-2024` | compare-four | CJ McCollum | Mike Conley: 179; CJ McCollum: 239; Grayson Allen: 205; Luke Kennard: 107 |
| `nfl-passing-2023` | compare-four | Tua Tagovailoa | Dak Prescott: 4,516; Josh Allen: 4,306; Tua Tagovailoa: 4,624; Jared Goff: 4,575 |
| `nfl-rushing-2023` | match-value | Christian McCaffrey | Derrick Henry: 1,167; Kyren Williams: 1,144; James Cook: 1,122; Christian McCaffrey: 1,459 |
| `nfl-receiving-2023` | compare-four | Tyreek Hill | Tyreek Hill: 1,799; CeeDee Lamb: 1,749; Amon-Ra St. Brown: 1,515; Puka Nacua: 1,486 |
| `nfl-passing-2022` | compare-four | Patrick Mahomes | Kirk Cousins: 4,547; Patrick Mahomes: 5,250; Justin Herbert: 4,739; Tom Brady: 4,694 |
| `nfl-rushing-2022` | match-value | Josh Jacobs | Nick Chubb: 1,525; Saquon Barkley: 1,312; Josh Jacobs: 1,653; Derrick Henry: 1,538 |
| `nfl-receiving-2022` | compare-four | Justin Jefferson | Tyreek Hill: 1,710; Davante Adams: 1,516; A.J. Brown: 1,496; Justin Jefferson: 1,809 |
| `mlb-homeRuns-2023` | compare-four | Matt Olson | Matt Olson: 54; Kyle Schwarber: 47; Pete Alonso: 46; Shohei Ohtani: 44 |
| `mlb-hits-2023` | match-value | Ronald Acuña Jr. | Marcus Semien: 185; Ronald Acuña Jr.: 217; Freddie Freeman: 211; Luis Arraez: 203 |
| `mlb-rbi-2023` | compare-four | Pete Alonso | Ozzie Albies: 109; Juan Soto: 109; Pete Alonso: 118; Kyle Tucker: 112 |
| `mlb-homeRuns-2022` | compare-four | Aaron Judge | Kyle Schwarber: 46; Pete Alonso: 40; Austin Riley: 38; Aaron Judge: 62 |
| `mlb-hits-2022` | match-value | Freddie Freeman | Freddie Freeman: 199; Trea Turner: 194; Bo Bichette: 189; José Abreu: 183 |
| `mlb-rbi-2022` | compare-four | Pete Alonso | Francisco Lindor: 107; Pete Alonso: 131; José Ramírez: 126; Paul Goldschmidt: 115 |
| `nhl-points-2024` | compare-four | Nikita Kucherov | Connor McDavid: 132; Artemi Panarin: 120; Nikita Kucherov: 144; Nathan MacKinnon: 140 |
| `nhl-goals-2024` | match-value | Auston Matthews | Sam Reinhart: 57; Zach Hyman: 54; Nathan MacKinnon: 51; Auston Matthews: 69 |
| `nhl-assists-2024` | compare-four | Nathan MacKinnon | Nathan MacKinnon: 89; Quinn Hughes: 75; Cale Makar: 69; Artemi Panarin: 71 |
| `nhl-points-2023` | compare-four | Connor McDavid | Nathan MacKinnon: 111; Connor McDavid: 153; Leon Draisaitl: 128; David Pastrnak: 113 |
| `nhl-goals-2023` | match-value | Connor McDavid | Mikko Rantanen: 55; Leon Draisaitl: 52; Connor McDavid: 64; David Pastrnak: 61 |
| `nhl-assists-2023` | compare-four | Nikita Kucherov | Leon Draisaitl: 76; Erik Karlsson: 76; Nathan MacKinnon: 69; Nikita Kucherov: 83 |

The NBA sample broadens the proposed metrics to include total blocks, steals and made threes. These remain
integer totals with the same comparison rules. The exact-value template is a one-metric clue in this proof;
multi-metric stat lines can be evaluated later.

### Traps caught

- MLB 2022 RBI: Judge and Alonso tied; only Alonso is among this question's choices. It does not claim a sole MLB leader.
- NHL 2023–24 assists: McDavid and Kucherov tied above the chosen group; MacKinnon is highest only among these four.
- NBA 2023–24 steals: Fox and Gilgeous-Alexander tied in totals; the selected group includes Fox but not Gilgeous-Alexander.
- NBA rebound and assist order differs between totals and per-game rates. The prompts and explanations consistently use totals.
- Three made-threes distractors come from a percentage-leader table. The comparison uses its **3FG total** column and makes no league-wide ranking claim.

## Product proof and remaining decisions

The daily sample has one question per league plus NBA, with five different correct athletes.
Difficulty labels are tentative; a five-question 2/2/1 mix is only a design target until people calibrate it.
Practice covers all 15 combinations with balanced league counts. This small pool cannot prove a 30-day
daily reservation plus five eligible questions for every single-league practice mode; it deliberately allows
overlap for review. Do not reuse this selector as the production scheduler.

Timing evidence: the first successful MLB response was saved at 03:44:53 UTC and the final question
file at 03:53:43 UTC on October 22, an elapsed interval of 8 minutes 50 seconds. That interval includes
agent-assisted source review, assembly and overlapping implementation work; earlier discovery and later
verification are outside it. It is not isolated authoring time, human editorial throughput or a per-question estimate.
Human review time and play-session duration have not yet been measured. Track both in the review worksheet
before estimating the 240-family catalogue effort.

Automated evidence: 24 question invariants, all 15 league combinations, unchanged daily selection,
blocked/malformed preferences, source-value comparison for 48 NFL/MLB values, and Chromium gameplay/layout checks.
NBA/NHL source checks were manual comparisons against retrieved reports; a second human review is pending.

**Next gate:** review this prototype and source ledger, resolve the NBA/MLB/NHL production source arrangement,
and demonstrate repeatable NHL retrieval. Then proceed to PR 2's server-only catalogue, validation and quiz snapshots.
Do not mark the first delivery stage fully accepted solely because the implementation PR is merged.

