# Daily policy and offline supply check

Run before queue publication:

```sh
dotnet run --project src/KnowBall.Importer -- --simulate-numbers approved.json --start 2026-11-01
```

This command writes no database rows. It validates approved content, simulates 120
days, and checks all 15 practice league selections after reserving the first 30 daily
sets. Output reports completed days, independent fact count and the first shortage;
failure exits 1. Synthetic tests prove the algorithm, not real editorial supply.

Daily policy v1 requires one question per league plus a bonus rotating NBA/NFL/MLB/NHL
from the supplied launch date. Difficulty is exactly two approachable, two medium,
one harder. Fact families cannot repeat at date distances under 30 days. Optional
focusKey metadata prevents the same athlete focus within a round; editorial content
must provide that metadata to enforce athlete identity across different facts.
Selection and question ordering are deterministic. A bounded search reports failure
instead of hanging or relaxing constraints. Existing dated snapshots are returned
unchanged.

## Persistent queue

The existing nightly importer now also runs an independent Numbers queue job.
Configure NumbersDontLie:QueueEnabled=true, NumbersDontLie:CatalogueId to an imported
approved content hash, and NumbersDontLie:LaunchDate to a stable ISO date. It is
disabled by default. The job fills 30 dates starting today in Eastern time (or the
launch date if later). Roster failures do not skip the Numbers job.

The queue loads neighboring snapshots, preserves all existing dates, and generates
missing dates in order. One serializable transaction commits the entire requested
range; content shortages or concurrent serialization failures commit no partial
range. The next invocation retries against current saved rows. Database uniqueness
still protects each date. Failures are logged separately and never replace existing
questions. No migration or public gameplay endpoint is added by this integration.

Remaining release checks: 240 approved families / 60 per league, populated focus
metadata, practice difficulty mix and topic/era combinations, answer-position
balancing and saved gameplay APIs. The current preview
remains prototype-only and is deliberately rejected by the approved-content check.
