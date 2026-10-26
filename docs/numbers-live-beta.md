# Numbers Don't Lie: playable beta

The fourth home card opens `/numbers-dont-lie`. The old hosted preview redirects here. The Angular app no longer ships the prototype question bank or its answer keys.

The API supplies a shared five-question daily round (Eastern date) and custom practice across NBA, NFL, MLB and NHL. Practice supports multiple leagues, topics and optional season-year bounds. Answers, score, current position and unacknowledged explanations persist in `NumbersPlays`; reloads and API restarts retain progress. The server scores answers and only reveals solutions for answered questions. Answer request IDs and revision checks protect retries and concurrent tabs. Continue acknowledgements cannot skip questions.

`X-Anon-Id` identifies the browser using the existing anonymous-player interceptor. Round reads and writes require that same ID; this is browser identity, not account authentication. Clearing browser storage loses access. Existing prototype progress is not imported. Practice links expire after 30 days; expired rows are not yet automatically deleted.

Deployment applies the `PersistNumbersBetaPlay` Postgres migration through the existing initializer. SQLite's development schema version is now 5. No new hosting service or environment variable is required.

## Content scope

This is explicitly an 88-question beta: 22 questions per league, comprising six stats, five awards, six championship teams and five season clues. The embedded `Data/numbers-beta.json` extends the original 56-question prototype bank with 32 source-checked historical questions. Each league/topic combination now has enough distinct facts for a five-question practice round without year bounds. Narrow year ranges can still exhaust the bank.

The original prototype catalogue remains a design fixture. The server bank retains source attribution and prototype review status, using the established selection algorithm with `requireApproved: false`. Daily snapshots freeze selected questions, opaque question IDs and shuffled opaque option IDs on first access. Existing saved rounds do not change when the bank expands. The new historical facts were checked on October 25, 2026 against the linked NBA/NHL histories, team championship histories, MLB postseason histories, Baseball Almanac MVP records and EDSFootball AP MVP records. NFL championship clues use the season year and explain the following calendar year's Super Bowl date.

The approved-content queue from the previous PR remains separate and disabled by default. This beta does not claim that bank has met the production approval, breadth or repeat-window requirements. Connecting approved queued content, enlarging the bank, repeat avoidance across practice rounds, cumulative stats/streaks and archive browsing remain follow-up work. Individual round scores, explanations and spoiler-free result sharing are available now.

## Verification

Content tests validate the embedded bank and all 225 nonempty league/topic filter combinations. Integration tests exercise shared daily content, ownership, hidden unanswered solutions (including opaque season-question IDs), saved feedback, scoring, answer retry after completion, practice start retries and conflicting start filters. Browser tests play all five questions, wait for each new question before selecting its answer, reload between feedback and continuation, resume completed results, start NBA-only practice, check mobile overflow and verify the legacy preview redirect and removed answer-bank URL.
