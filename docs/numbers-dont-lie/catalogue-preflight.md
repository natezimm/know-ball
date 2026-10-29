# Numbers Don't Lie backend: catalogue preflight

The expanded preview was accepted on October 24, 2026. Its awards, teams, seasons,
league multiselect and optional topic/era filters are part of the full-game direction.

This is the first backend slice of delivery milestone 2. It adds a pure .NET content
validator and deterministic private round selector, without exposing answers through
an API or changing the preview.

Run offline validation without starting the worker or touching a database:

```sh
dotnet run --project src/KnowBall.Importer -- --validate-numbers docs/prototypes/numbers-dont-lie/questions.json
```

Add `--require-approved` for publication preflight. The current 56-question preview
must fail that gate: visual acceptance does not mark its facts as approved. Review
status is editorial metadata, not something the validator can independently prove.

The validator supports comparison, exact-value and historical trivia. It checks
identity, revision, sources, dates, scope, options, correctness and numeric uniqueness.
The selector canonicalizes league order, enforces optional year/topic restrictions,
excludes reserved daily fact families, balances five questions across selected
leagues, and returns cloned snapshots. An explicit seed makes selection repeatable.
It fails rather than borrowing another league or returning a partial round.

Still required in milestone 2: production catalogue/schema, durable import and immutable
snapshots, daily scheduling with rotating bonus/difficulty/repeat policies, database
migrations and the 120-day supply simulation. This selector is a reusable selection
primitive, not the complete daily scheduler. The 56-question preview does not establish
the 240-family release gate. Saved gameplay API and Angular integration follow those
steps. No live home card or database migration is added in this slice.
