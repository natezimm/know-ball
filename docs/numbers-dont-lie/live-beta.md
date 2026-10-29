# Numbers Don't Lie: playable beta

The fourth home card opens `/numbers-dont-lie`. The old hosted preview redirects here. The Angular app no longer ships the prototype question bank or its answer keys.

The API supplies a shared five-question daily round (Eastern date) and custom practice across NBA, NFL, MLB and NHL. Practice supports multiple leagues, topics and optional season-year bounds. Answers, score, current position and unacknowledged explanations persist in `NumbersPlays`; reloads and API restarts retain progress. The server scores answers and only reveals solutions for answered questions. Answer request IDs and revision checks protect retries and concurrent tabs. Continue acknowledgements cannot skip questions.

`X-Anon-Id` identifies the browser using the existing anonymous-player interceptor. Round reads and writes require that same ID; this is browser identity, not account authentication. Clearing browser storage loses access. Existing prototype progress is not imported. Practice links expire after 30 days; expired rows are not yet automatically deleted.

Deployment applies the `PersistNumbersBetaPlay` Postgres migration through the existing initializer. SQLite's development schema version is now 5. No new hosting service or environment variable is required.

## Content scope

This is explicitly an 88-question beta: 22 questions per league, comprising six stats, five awards, six championship teams and five season clues. The embedded `Data/numbers-beta.json` extends the original 56-question prototype bank with 32 source-checked historical questions. Each league/topic combination now has enough distinct facts for a five-question practice round without year bounds. Narrow year ranges can still exhaust the bank.

The original prototype catalogue remains a design fixture. The server bank retains source attribution and prototype review status, using the established selection algorithm with `requireApproved: false`. Daily snapshots freeze selected questions, opaque question IDs and shuffled opaque option IDs on first access. Existing saved rounds do not change when the bank expands. The new historical facts were checked on October 25, 2026 against the linked NBA/NHL histories, team championship histories, MLB postseason histories, Baseball Almanac MVP records and EDSFootball AP MVP records. NFL championship clues use the season year and explain the following calendar year's Super Bowl date.

The approved-content queue from the previous PR remains separate and disabled by default. This beta does not claim that bank has met the production approval, breadth or repeat-window requirements. Connecting approved queued content, enlarging the bank, repeat avoidance across practice rounds, cumulative stats/streaks and archive browsing remain follow-up work. Individual round scores, explanations and spoiler-free result sharing are available now.

## Question quality and variety

The October 26 content pass revises 48 of the 88 slots without increasing the bank size:

- All 24 stat questions get clearer wording and explanations comparing actual totals. Eight now ask which player finished closest to a round-number target (two per league). The validator calculates absolute distances and rejects tied winners or incorrect answer keys.
- Twelve championship questions now include the Finals, World Series or Super Bowl opponent. Their explanations add the series result or clarify the NFL season/calendar-year distinction. The named opponent is removed from the choices so it cannot serve as an obviously impossible distractor.
- Two repeated Tom Brady MVP questions become Troy Polamalu and Aaron Donald Defensive Player of the Year questions, with team sources and defenders as distractors. Ten other award explanations lose generic filler.

For newly created beta rounds, selection first favors topics used least often, then numerical/trivia templates used least often, with the seeded ordering breaking ties. This operates within each league's eligible pool, preserving league balance, selected topics, year bounds and fact-family exclusions. It does not guarantee every style when filters restrict the pool, or prevent repeats between separate practice rounds. The approved queue retains its existing selection behavior.

Existing saved daily/practice snapshots keep their original wording and answers. After deployment, start a new practice round to test this pass; the next newly generated daily round uses it too. No migration is needed.

Editorial standard for future additions:

- Give players something to recognize, compare or reason about. Mix statistical judgments, award winners, championship matchups and historical seasons; avoid a run of near-identical MVP recall prompts.
- State the season, award and statistical scope precisely. Say "among these four" for comparisons that do not establish a league-wide leader. Keep NFL season years distinct from Super Bowl calendar years.
- Use plausible alternatives from the same sport and era. Avoid joke options, answers repeated in the clue, and distractors ruled out by the clue itself.
- Explain the answer with a useful comparison or historical detail. Source every added factual claim; retain source credit and verification status. Editing wording alone does not constitute new source verification.
- Prefer fewer good questions to padding the bank. Difficulty remains uncalibrated until playtesting; this pass does not certify the entire bank for approved publication.

## Verification

Content tests validate the embedded bank and all 225 nonempty league/topic filter combinations. Integration tests exercise shared daily content, ownership, hidden unanswered solutions (including opaque season-question IDs), saved feedback, scoring, answer retry after completion, practice start retries and conflicting start filters. Browser tests play all five questions, wait for each new question before selecting its answer, reload between feedback and continuation, resume completed results, start NBA-only practice, check mobile overflow and verify the legacy preview redirect and removed answer-bank URL.
