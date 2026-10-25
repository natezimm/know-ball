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
unchanged. This offline policy does not yet replace database staging or run nightly.

Remaining release checks: 240 approved families / 60 per league, populated focus
metadata, practice difficulty mix and topic/era combinations, answer-position
balancing, transactional queue integration and nightly top-up. The current preview
remains prototype-only and is deliberately rejected by the approved-content check.
