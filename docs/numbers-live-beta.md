# Numbers Don't Lie: playable beta

The fourth home card opens `/numbers-dont-lie`. The old hosted preview redirects here. The Angular app no longer ships the prototype question bank or its answer keys.

The API supplies a shared five-question daily round (Eastern date) and custom practice across NBA, NFL, MLB and NHL. Practice supports multiple leagues, topics and optional season-year bounds. Answers, score, current position and unacknowledged explanations persist in `NumbersPlays`; reloads and API restarts retain progress. The server scores answers and only reveals solutions for answered questions. Answer request IDs and revision checks protect retries and concurrent tabs. Continue acknowledgements cannot skip questions.

`X-Anon-Id` identifies the browser using the existing anonymous-player interceptor. Round reads and writes require that same ID; this is browser identity, not account authentication. Clearing browser storage loses access. Existing prototype progress is not imported. Practice links expire after 30 days; expired rows are not yet automatically deleted.

Deployment applies the `PersistNumbersBetaPlay` Postgres migration through the existing initializer. SQLite's development schema version is now 5. No new hosting service or environment variable is required.

## Content scope

This is explicitly a 56-question beta. Its embedded `Data/numbers-beta.json` is copied from the existing prototype catalogue, retaining source attribution and prototype review status. It uses the established selection algorithm with `requireApproved: false`. Daily snapshots freeze the selected questions and shuffled opaque option IDs in the database on first access.

The approved-content queue from the previous PR remains separate and disabled by default. This beta does not claim that bank has met the production approval, breadth or repeat-window requirements. Connecting approved queued content, enlarging the bank, repeat avoidance across practice rounds, cumulative stats/streaks and archive browsing remain follow-up work. Individual round scores, explanations and spoiler-free result sharing are available now.

## Verification

Integration tests exercise shared daily content, ownership, hidden unanswered solutions, saved feedback, scoring, answer retry after completion, practice start retries and conflicting start filters. Browser tests play all five questions, reload between feedback and continuation, resume completed results, start NBA-only practice, check mobile overflow and verify the legacy preview redirect and removed answer-bank URL.
