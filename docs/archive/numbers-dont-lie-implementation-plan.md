# Numbers Don't Lie — implementation plan

Written October 21, 2026 against main at `a7ab2676300e9a815d923a7fc0b542a89949121f`.
Status: proposed implementation, with a chosen name and cross-sport scope. Updated October 21, 2026:
the user approved a shared daily challenge plus practice with multi-select league preferences.
The active site still has three games. This document is the implementation reference for the former Stat Quiz concept.

## 1. Product and first-release decisions

**Numbers Don't Lie** is a daily sports-statistics quiz within Know Ball. Answer a short set of
multiple-choice questions, learn the numbers behind each answer, and share a spoiler-free result.
The public route will be `/numbers-dont-lie`; the subtitle is **Daily sports stats trivia**.

The user's decisions are the name, cross-sport scope, a shared mixed-league daily challenge and
customizable practice with any combination of leagues. Other rules below remain recommended defaults:

| Decision | Recommended first release |
| --- | --- |
| Daily challenge | Five questions, identical for everyone, changing at midnight America/New_York |
| Sports | NBA basketball, NFL football, MLB baseball and NHL hockey from the first public release |
| Daily mix | One question from each league, plus one rotating bonus league; equal bonus turns over four days |
| Answers | Four choices and one submission per question; every question can still be played after a miss |
| Feedback | Immediately explain the answer and show the relevant values and source after submission |
| Score | One point per correct answer; final result 0–5 |
| Difficulty | Two approachable, two medium and one harder question, with varied positions in the sequence |
| Timing | Untimed; players can pause, reload and resume |
| Modes | Shared daily, customizable practice, past daily quizzes, results, statistics and sharing |
| Practice leagues | Select any nonempty combination of NBA, NFL, MLB and NHL; All leagues by default; remember preferences |
| Daily comparability | League preferences affect practice only; every player receives the same daily set |
| Images | Text, numbers and sport labels; photographs and headshots are unnecessary |

Five questions should take roughly two to three minutes in a prototype; measure this with people
rather than treating it as a demonstrated duration. An untimed release makes interruptions,
accessibility and mobile connection failures easier to handle. The earlier mockup's ten questions,
15-second clock and speed bonus are superseded by this recommendation. Timed play can be evaluated later.

The quiz is its own format: players choose among four answers instead of repeatedly guessing one
athlete. Keep higher/lower, ranking, free-text numerical answers and league-specific daily modes
for later. League-specific daily challenges remain deferred: personalization belongs in practice.
Multi-sport does not yet mean every sport or global competition.

## 2. Experience from home page to result

1. Open the game's fourth card after public release. It shows today's quiz number and saved state.
2. Read the short rules: five questions, four sports, one answer each, no timer.
3. See the current question, league, season, scope, progress and four large answer buttons.
4. Select an option, then press **Lock in answer**. Selection remains editable until submission.
5. While saving, disable repeat submission without clearing the selected answer.
6. Show correct/incorrect text, the correct choice, useful numbers and a short explanation.
   Include a source link. A **Next question** button advances after the player has read it.
7. After five submissions, show the score, five result markers, per-question review, Share,
   Statistics and Past quizzes.

Feedback remains visible after a reload until Next is pressed. A missed question does not end the quiz.
There is no scored Skip action in the first release. Previously answered questions are read-only.
The summary must use **completed**, **correct answers** and **perfect scores**, rather than calling
a 3/5 result a loss or reusing the player games' guess-distribution labels.

Suggested share output, generated from saved server results:

```text
Know Ball · Numbers Don't Lie #42
4/5
🟩🟩⬜🟩🟩
knowball.nathanzimmerman.com/numbers-dont-lie
```

Include the date or an archive link so a result remains meaningful after midnight. Do not include
questions, player names, answer values or league ordering that could spoil the quiz.

### Customizable practice

Add a **Practice** entry on the quiz page and result screen, with its own setup view:

- Four labelled checkboxes for NBA, NFL, MLB and NHL. **All leagues** selects all four; it is a shortcut,
  not a fifth league. Support all 15 nonempty combinations, including a single league.
- Require at least one league and explain an empty selection before Start. Do not auto-start a round
  as checkboxes change. Show the selected leagues next to **Start practice**.
- Remember the canonical selection locally. Default to all leagues on first visit; blocked storage
  keeps the preference for that visit. Ignore obsolete stored codes safely, explain unavailable
  selections and never submit an empty list.
- Start a five-question round using only the chosen leagues. Allocate questions as evenly as possible:
  one league gets five; two get three/two; three get two/two/one; four get two/one/one/one.
  Vary which selected league gets each extra question between rounds. Persist the round's order.
- Label the page and result **Practice**, with the league names. A practice share uses
  **Practice · NBA + NFL · 4/5**, without a daily quiz number. Link shares to practice setup, not the
  private round URL that another visitor cannot open.
- Practice never affects daily scores, completion streaks, perfect-daily counts or the daily archive.
  The first release needs a round result, not another practice analytics dashboard.
- Freeze the round's league selection after Start. Changing leagues starts a new round after confirmation
  if answers have been submitted; leaving practice does not record a daily loss.
- Use explicit practice-round URLs so reloads and API restarts restore the same leagues, answers
  and pending explanation. Practice is a mode within this game, not another home-page game card.

## 3. Content is the first gate

The codebase currently imports NBA data. It has no demonstrated NFL, MLB or NHL statistics pipeline.
`PlayerSeasonStat` includes basketball columns and a `StatsJson` escape hatch; a four-sport quiz
must not depend on assuming those other leagues already work.

Start with a small, reviewed question catalogue stored in the repository. Use an offline CLI to
validate and import it into immutable daily snapshots. Where an existing dataset is suitable,
templates may produce candidate questions for review. This avoids authoring a new quiz every day
without requiring three full roster/history import systems before a prototype can be tested.

### Source proof before building the game

Produce at least six independently verified sample questions per launch league, 24 total.
Each sample must include its source, all four choices, the exact supporting values and a written
explanation. Test repeatable retrieval or an allowed snapshot, field definitions, season coverage,
completeness, attribution and permission to retain/redistribute the material being bundled.

Candidate source leads, not promises of licensed production coverage:

| League | Existing capability or lead to investigate | Proof required |
| --- | --- | --- |
| NBA | Existing Basketball-Reference mirror import; official league statistics for verification | Combined season rows, games played, rate precision, completed-season coverage and source terms |
| NFL | nflverse season statistics, already identified in `research.md` | Exact dataset/release, licence and attribution, player IDs, season type and position-specific fields |
| MLB | Official MLB historical statistics, or a documented reusable historical dataset | Source terms, stable retrieval/snapshot, hitting totals and season identifiers |
| NHL | Official NHL historical statistics, or a documented reusable historical dataset | Source terms, stable retrieval/snapshot, skater totals and season identifiers |

Record the outcome in `docs/numbers-dont-lie-data-sources.md` during the first implementation PR,
including dated links and rejected options. Public accessibility alone does not establish reuse terms.
Do not copy provider prose, images or large raw datasets into the repository as a shortcut.
If one league cannot meet the gate, report it and revise the proposed scope before presenting an NBA-only
game as the cross-sport release. No paid provider or new hosting service is assumed.

### Question templates and accuracy

Begin with two templates over completed regular seasons:

- **Compare four players:** which of these four recorded the most of a named statistic in a named season?
- **Match a stat line:** which of these four recorded a precisely scoped line? Validate uniqueness within
  the choices, and include all named metrics in the explanation.

Possible initial metrics are NBA points/rebounds/assists per game, NFL passing/rushing/receiving totals,
MLB hits/home runs/RBI and NHL goals/assists/points. Their availability is subject to source proof.
NFL prompts must name the statistical category; use suitable peers rather than arbitrary position mixtures.
Use clear season labels: NBA/NHL span years, NFL/MLB use their documented season identifier.

Rules enforced by content validation:

- Every prompt names league, season and regular-season scope; rate metrics include any minimum-games
  requirement in both eligibility and wording. An editorial threshold must not be described as an
  official qualification rule.
- Use whole-season combined rows for traded players and avoid counting both combined and team rows.
- Do not reconstruct exact totals from rounded per-game values.
- Do not claim a league-wide leader from an incomplete list. **Among these four** is explicit scope.
  True leader/record questions are deferred until complete populations and qualification rules are proven.
- Reject tied winners, ambiguity after display rounding, duplicate choices, missing values, mismatched
  units, and questions whose wording gives away the answer.
- Keep sports and statistical units explicit. Future baseball pitching-rate work must account for
  innings measured in outs rather than treating .1/.2 as ordinary decimal fractions.
- Use frozen, completed seasons. Active career totals and live-season leader questions are deferred.
- A question is not an approved fact merely because a generator or language model produced it.

### Catalogue contract and supply

Proposed location: `content/numbers-dont-lie/questions.v1.json`, with a JSON schema and small test fixtures.
Each entry contains:

- Stable question ID, content revision, underlying fact-family key and schema version.
- League/sport, season label, regular-season scope, statistic key/unit, template and difficulty.
- Plain-text prompt and four options with stable, question-local IDs and distinct display labels.
- Correct option ID, supporting values, original explanation and source references.
- Source URL/provider, retrieval date, dataset version/checksum when applicable, and review status/date.

Correctness metadata is server-only. The catalogue must not live in Angular public assets or be imported
into the browser bundle. Render explanations as text, with validated HTTPS source links.

Launch target: at least 240 approved independent question families, at least 60 per league, with a
workable difficulty mix. Variants of the same fact do not count as additional families. This is a proposed
content gate, not a claim that the questions have been sourced. Batch generation can help build supply,
but the time needed to verify it must be measured in the 24-question proof.

Generate at least 30 complete daily quizzes before launch. Enforce no repeated fact family within
30 calendar days, no repeated athlete focus within one quiz, balanced leagues and varied answer positions.
Simulate 120 scheduled days with the same rules to prove the pool remains schedulable; report shortages
by league and difficulty. Do not silently relax the rules or create a partial quiz when supply is short.

Practice uses the same approved catalogue with a separate selection policy. Filter by the canonical
requested league set before choosing questions or distractors. Exclude fact families used in today's
daily and already queued future dailies so practice does not reveal their answers. Prefer variety
between practice rounds without a strict per-player no-repeat rule that exhausts a small selection.
This does not guarantee against overlap with future quizzes that have not yet been scheduled.

After reserving the daily queue, prove that all 15 league combinations can still produce five eligible
practice questions with the target difficulty mix. If a requested selection cannot, return a clear
unavailable response before creating a partial round. Never borrow from an unselected league.

## 4. Backend architecture

Reuse `PuzzleCalendar`, `TimeProvider`, the anonymous ID, EF/Postgres infrastructure, rate-limit
conventions, import-worker hosting, CI and backup tools. Introduce a dedicated Numbers Don't Lie module.

The existing `IGame` contract requires a player answer and evaluates `Player` guesses.
`PuzzleScheduler` requires one league per puzzle, and `GameService` builds won/lost player-game DTOs.
A mixed quiz with five independent answers does not fit those contracts. Avoid broad changes to them
as a prerequisite. A small dedicated service and two tables are the recommended first implementation:

| Model | Responsibility and constraints |
| --- | --- |
| `NumbersQuiz` | ID, mode (daily/practice), nullable date/number, canonical selected leagues, content/schema version, immutable five-question snapshot, created time; unique daily date; practice owner and start-request receipt |
| `NumbersQuizAttempt` | ID, quiz/user key with a unique composite index, bounded answer receipts, correct count, feedback-acknowledged position, start/completion times, revision concurrency token |

The daily or practice snapshot includes server-only correct answers, values and explanations; API DTOs expose only
what that particular player has unlocked. Store option order once so everyone gets the same choices
and a reload cannot reshuffle them. Quiz numbering must use a stable configured launch date, not change
when old rows are deleted. Add date/completion indexes appropriate to archive and statistics queries.
Enforce daily date/number versus practice owner/no-date invariants in validation and database constraints.
A unique nullable daily-date index allows undated practice rows on Postgres and SQLite. Only the owning
anonymous user may read or mutate a practice round. Retain practice for a configurable 30 days, then
delete its snapshot, attempt and receipts together; never apply that cleanup to daily quizzes.
Document an unavailable/restart action for expired practice URLs.

No `LeagueId = nba` placeholder or fabricated multi-sport League row is needed in this separate table.
Leave existing `GameType` values intact, including reserved photo-game value 2. The old StatQuiz value 4
does not require reuse just to launch this module. Existing puzzle enums are stored as strings in EF:
a numeric value remaining unchanged does not make an enum rename a safe data migration.

Additive Postgres migration and EF snapshot, plus a SQLite schema-version bump, follow the repository's
existing policy. Older local SQLite files need a documented rebuild; production data is migrated in place.
Avoid changing player-game rows, Spordle sessions or stored photo-game history.

### Daily generation and content correction

- Import approved catalogue revisions through an explicit idempotent CLI command. Validation failures
  must be actionable and fail the command.
- Queue 30 daily sets from stored approved content using deterministic inputs, algorithm version and
  date order. The database's unique date constraint arbitrates simultaneous generation.
- Reuse the existing nightly worker host to top up this queue. Report Numbers Don't Lie queue failures
  separately so a roster-import failure and a quiz-content shortage remain distinguishable.
- A missed nightly run must not prevent playing an already queued quiz. A request-time fallback may
  assemble the complete set from approved local content; concurrent requests return the same committed row.
- Playing never calls an external statistics provider. A source outage does not change a published quiz.
- Before a quiz is published, corrections create a new catalogue revision and may regenerate its snapshot.
  After publication, freeze questions, option order, answers and scoring.
- If a published answer is demonstrably wrong, withdraw the quiz, show a correction notice, exclude it
  from aggregates and rebuild streaks using explicitly neutral withdrawn days. Retain attempts for audit;
  never silently switch a correct option under an existing result. Prove this path before launch.

### Answer processing, retries and reloads

For each submission, verify anonymous identity, playable date, current question, supplied choice,
request ID and attempt revision. The answer and progress advance atomically in one database write.

Persist a receipt keyed by attempt/request ID, including its question, choice and original outcome.
On a retry with the same payload, return that outcome without a second answer or score increment.
A receipt lookup precedes current-question, completion and revision checks, so a successful final answer
can still be retried. Include the latest authoritative state alongside a replayed outcome so an old
retry cannot rewind a tab after progress elsewhere.
A reused ID with different content returns 409. Simultaneous tabs choosing different options for
the same question can commit only one; the other receives 409 and refreshes saved progress.
Handle concurrent creation of the first attempt using the unique quiz/user constraint.

The current-question endpoint shows a saved explanation until Next has been acknowledged.
Next acknowledgement is idempotent and persisted, so a reload restores the same screen. An answer to
an unrevealed or future question is rejected. Completed attempts remain read-only; retrying their final
accepted request still succeeds. Store everything needed to resume across API process restarts.

Practice creation also accepts a UUID request ID. Retrying the same start request and canonical league
set returns the same round; reusing the ID for another set returns 409. Scope that receipt to the owning
user and retain it for the round's lifetime. Enforce league filtering and answer/progress rules on the
server in both modes; frontend filtering alone is insufficient.

Only submitted questions reveal their correct choices and explanations. No future answer,
correctness flag, source value or explanation may leak through daily, state, archive, home status,
error responses or generated frontend assets. Source attribution can be revealed alongside the answer.
Anonymous IDs retain the existing casual-play trust model; this is not a secure competitive leaderboard.

At midnight Eastern, new visits receive the new quiz. An existing attempt may finish the old quiz
through its explicit ID. Its result counts against that quiz's date, never tomorrow's.

## 5. API and integration boundaries

Proposed endpoints, all under `/api/numbers-dont-lie`:

| Method and path | Contract |
| --- | --- |
| `GET /availability` | Enabled/available state and supported practice league codes/labels/availability; lets the home page omit the unreleased game |
| `GET /daily` | Today's quiz metadata, answered progress and current question or pending explanation |
| `GET /quizzes/{id}` | Resume a playable daily or the visitor's own practice round; future, withdrawn and other users' practice rounds cannot expose questions |
| `POST /practice` | `{ leagues: ["nba", "nfl"], requestId: "UUID" }`; validate, generate and persist a private five-question round |
| `POST /quizzes/{id}/answers` | `questionId`, `optionId`, UUID `requestId`, `expectedRevision`; score on the server |
| `POST /quizzes/{id}/continue` | Idempotently acknowledge an answered question's explanation |
| `GET /archive` | Published past/current dates and that visitor's saved progress/result |
| `GET /stats` | Quiz-specific completed count, average correct, score distribution, perfect count and streaks |

Validate `X-Anon-Id` with the existing rules for personalized endpoints; availability has no player data.
Apply bounded request sizes, pagination and existing per-address rate-limit conventions.
Use 400 for invalid fields, 404 for unknown/future quizzes, 409 for state conflicts and 429 for rate limits.
A withdrawn quiz returns a safe unavailable response with its correction notice, without a question payload.

For practice starts, accept supported league codes only, deduplicate and canonicalize them, and reject
empty/unknown lists with 400. Insufficient eligible content returns 503 with a safe retryable explanation,
without creating a partial round or including another league. Rate-limit practice starts as well as
answers. Other users' practice IDs return 404.

Use a dedicated `NumbersQuizState`: quiz ID, mode, selected leagues, nullable date/number, status (new/inProgress/completed),
question count, answered count, correct count, revision, current public question, and unlocked review.
Completed state includes the five answered items. Practice has no daily date or quiz number.
It is not a player `GameState`.

Retain the existing `/api/me/today` and `/api/me/stats` contracts for the player games.
Compose the new game's separate availability/state in Home rather than fabricating `maxGuesses`,
win rates or NBA league fields. Extract a neutral shared DTO only if implementation demonstrates a need.

Viewing a quiz without answering does not count as played. Statistics count completed daily sets once;
partially answered daily sets appear as in progress. Exclude practice in the underlying stats/archive
queries, not only in the UI. Track completion streak and perfect-score count separately.
Compute streaks from quiz dates, with a defined current-day grace and neutral unavailable/withdrawn days.
An unanswered available past date breaks a completion streak. Repeated submission cannot duplicate a result.
Archives use explicit quiz IDs; opening an archive entry must not navigate back to an ambiguous daily URL.

## 6. Frontend and visual direction

Use the existing Trading Card theme, bundled typography, header/footer and dialog conventions.
Introduce one distinct trim colour with checked contrast. Build a quiz-specific shell/store rather than
forcing a player-search dock, guess dots and won/lost dialogs into the new format.

Recommended files:

- `games/numbers-dont-lie/numbers-dont-lie.ts`: page and route integration.
- Practice setup: native league checkboxes, preference storage and an explicit Start action.
- Routes `/numbers-dont-lie/practice` for setup and `/numbers-dont-lie/practice/:quizId` for saved rounds.
- `numbers-quiz-api.ts` and `numbers-quiz-store.ts`: typed HTTP contract, signals and saved-state handling.
- Small question/choice and explanation/review components where they improve readability.
- Reuse `core/share.ts` transport with a quiz-specific result formatter.

Layout: title/quiz number, sport and season label, question number out of five, prompt card,
four stacked choices, Lock in answer, then an explanation card and Next. Put values in readable text;
neither correctness nor selection may rely on colour alone.

Use native radio controls and a form with a separate submit button, visible focus, labelled groups,
large touch targets and logical tab order. Announce saving and answer feedback with restrained live
regions. After Next, focus the next question heading; after completion, focus the result heading.
Respect reduced motion, text zoom and safe-area spacing. Layout must work at 320px without clipped choices.
League selection uses labelled checkbox groups and a visible nonempty-selection error. Clearly label daily
and practice controls; remembered practice preferences never change the daily quiz.

Network behavior:

- Keep the selected choice after a failed save and retain its request ID for a safe retry.
- After a stale-tab conflict, reload authoritative state and explain that an answer was already saved.
- If offline, show a retryable notice without guessing whether an answer was accepted or awarding points.
- Prevent duplicate clicks while requests are in flight; a pending Next can be retried safely.
- Restore state from the API after reload; browser memory or localStorage is not the progress authority.
- Degraded source/queue state must never show a playable card that leads to an empty quiz.

## 7. Delivery sequence

Keep each PR reviewable and leave the three-game public site intact until the release gate is met.
Do not implement deferred higher/lower, ranking, timed rounds or the removed photo game in these PRs.

| PR | Deliverable | Exit evidence |
| --- | --- | --- |
| 1. Rules and data proof | Confirm recommended rules, verify six real questions per league, document sources/terms, measure authoring effort and produce a static Trading Card prototype | Review all 24 questions for accuracy, ambiguity and quality; test daily/practice flows and league selection on a real phone; source proof for all four sports |
| 2. Catalogue and quiz sets | Schema/validator, explicit import CLI, daily/practice selectors, two tables/migration and immutable snapshots | Invalid content rejected; repeatable import; 120-day daily simulation; all 15 practice selections; date uniqueness on Postgres and SQLite |
| 3. Saved gameplay API | Daily/practice state, private practice starts, answer/continue endpoints, receipts, revision conflicts, restart recovery and reveal filtering | HTTP quizzes, server-enforced leagues/ownership, safe start retries, adversarial reveal checks and restart/resume tests |
| 4. Playable interface | Daily/practice navigation, remembered multi-select league setup, accessible quiz flow, durable reloads, retries and mobile layout | Component/browser tests against the real API; phone review; preferences never alter daily content |
| 5. Daily product integration | Quiz-specific stats/archive/share, midnight behavior, feature gate, home card and rename of the Stat Quiz design references | Correct counts/streaks, explicit archive links, unavailable states and three-game regressions |
| 6. Content and release | At least 240 approved families, 30 queued sets, operational/backup checks, documentation and enablement | All release gates below; public four-game card/metadata appears only when enabled |

The first PR is a content and product proof, not a launch. If 24 questions or the prototype do not meet
the quality bar, revise the plan before building the full backend and catalogue.

## 8. Verification and release gate

Unit coverage should target decisions: eligibility, trade-row aggregation, display-rounded ties,
distinct options, fact-repeat rules, league balance, deterministic selection, scoring, quiz-date
numbering and completion streaks. Use controlled clocks and small explicit catalogues.

Integration tests run against SQLite and Postgres:

- Import, generate, replay the import, and ensure the existing published set does not change.
- Two requests create the same daily set; two first answers create only one attempt.
- Duplicate request IDs, mismatched retry payloads, stale revisions and conflicting concurrent answers.
- Invalid choice, wrong question, out-of-order/future answers, missing identity, unknown ID and rate limits.
- A fresh API host restores score, pending feedback, continuation position and final result.
- Correct answers remain absent before submission and future answers stay private throughout.
- Midnight/DST, archive bounds, withdrawn days, final-attempt replay and stats counted once.
- All 15 practice league combinations return only selected leagues with balanced question counts.
- Empty/unknown league lists, duplicate codes, unavailable pools and tampered league preferences.
- Practice start retries create one round; mismatched retry payloads conflict; ownership is enforced.
- Practice excludes today's/queued daily fact families, never changes the daily set and never enters
  daily statistics, streaks, perfect counts or archives. Retention deletes practice data only.
- Existing player-game and Spordle API contracts continue to pass.

Browser coverage should play mixed correct/incorrect and perfect quizzes, reload at each state, drop
a committed response and retry it, use two tabs, navigate an archive, share a result and exercise
keyboard/touch layouts. Include single/multiple/all-league selection, remembered preferences and blocked
storage, empty selection, changing leagues mid-round, practice reload and clearly labelled practice
sharing. Verify the daily quiz is identical for visitors with different practice preferences.
Production assets must contain no catalogue answer data.
Keep ordinary browser tests independent of live sports APIs using approved, attributed fixtures.

Before enabling `NumbersDontLie:Enabled` in production:

- [ ] Rules and first 24 questions have passed human review.
- [ ] All four league sources have passed the data/terms and repeatable-snapshot gate.
- [ ] At least 240 approved independent families and a successful 120-day scheduling simulation.
- [ ] At least 30 complete daily sets queued; shortage/withdrawal behavior tested.
- [ ] All 15 league selections support complete practice rounds without spoiling queued dailies;
      practice ownership, retries, retention and exclusion from daily statistics are verified.
- [ ] Migration, API/component/browser suites and existing CI checks pass.
- [ ] Backup and restore retain daily snapshots and attempts; verify additive deployment/rollback behavior.
- [ ] Real iPhone/Safari and Android/Chrome play, text zoom, focus, dialogs and installed-PWA behavior checked.
- [ ] Test an update from an older cached app version. Existing users must be able to reach the current
      three/four-game UI; stale service-worker behavior must not be mistaken for a failed game removal.
      If an update-notification fix is needed, track and verify it explicitly before enablement.
- [ ] Feature disabled hides the home card and blocks direct gameplay without deleting saved data.
- [ ] Feature enabled adds exactly one new card and updates hero counts, description, social card,
      help text and applicable navigation; no Who's That Sportsman placeholder returns.
- [ ] Queue and error logs are useful without storing anonymous IDs or answer payloads in routine logs.

Use the existing .NET, Angular, Postgres and shared VM deployment. There is no new service, image-hosting
pipeline or provider call on a play request. Measure storage and query cost during the prototype;
do not claim a measured hosting cost before that evidence exists.

## 9. Deferred improvements

Evaluate after real replay feedback: league-specific daily quizzes, timed challenge mode,
higher/lower, ranking, records/milestones, full automatic multi-sport imports, more sports/competitions,
accounts, leaderboards and richer per-sport analytics. Each needs its own content and scoring decision.
Who's That Sportsman remains a separate deferred idea and is not a dependency of Numbers Don't Lie.

## 10. Starting point

October 25 storage update: [approved catalogue imports and staged immutable daily
snapshots](numbers-content-storage.md) are implemented as the next milestone-2
slice. Full queue policy/supply simulation, publication and saved gameplay remain
separate follow-ups. The hosted preview is unchanged.

October 24 update: the user accepted the expanded 56-question preview. Awards,
championship teams and season identification, plus topic/era practice filters, are
now part of the intended product scope. The older two-template-only guidance above
applies to the initial source proof, not the accepted full-game scope.
Backend milestone 2 begins with [catalogue preflight](numbers-catalogue-preflight.md):
offline validation and a deterministic selection primitive. Durable imports,
immutable database snapshots, scheduling rules and supply simulation remain the
next slice of that milestone; the saved gameplay API follows.

October 23 update: the user requested a hosted, reversible preview to test the design on the site before
committing to the full implementation. The existing sample is now packaged at
`/preview/numbers-dont-lie/index.html?ngsw-bypass=true`, linked from the footer and marked as a preview.
This explicitly supersedes the local-only packaging restriction for these 24 review questions.
The sample contains answers in browser assets, resets on reload and makes no database changes.
Full-game content approval and backend/release gates below remain future work; preview hosting does not complete them.

Delivery PR 1 now has a [private static prototype](prototypes/numbers-dont-lie/README.md) and
[24-question source proof](numbers-dont-lie-data-sources.md). Source values have been checked; human review,
physical-phone testing, repeatable NHL retrieval and production source approval remain open.
The preview does not implement the saved gameplay API.

Begin with PR 1: the 24-question four-league source proof and a five-question Trading Card prototype
covering both shared daily play and practice with league selection.
Resolve rules and content feasibility there. This planning PR adds documentation only; it does not
register a route, change the home page, import a new league or re-enable any removed game.

