# Numbers content storage

Approved catalogues can now be persisted with the existing importer:

```sh
dotnet run --project src/KnowBall.Importer -- --import-numbers /path/to/approved-questions.json
```

This is an explicit one-shot command. It does not run roster jobs or the nightly
worker. The normal database configuration applies. Validation rejects prototype-only
entries. No current preview questions have been relabeled approved by this change.

Catalogue IDs are SHA-256 hashes of canonical JSON (object keys sorted, array order
preserved). Whitespace/property-order changes reuse a row; content changes create a
new row. Imports are atomic. A concurrent identical import returns the committed row.

NumbersContentStore.StageDailyAsync is an internal staging primitive: it selects
five approved questions and stores their full private JSON once per date. Retrying
the date returns the saved row even if a newer catalogue exists. A database unique
index arbitrates competing writes. EF tracked updates to catalogues and snapshots
are rejected. Direct SQL remains an administrative responsibility.

These rows are not published gameplay. No endpoint or home card exposes them.
Before publication, add queue policy (rotating league bonus, difficulty, 30-day
fact exclusions), stable numbering, withdrawal workflow and the supply simulation.
Practice ownership/retention and attempt receipts belong to the saved gameplay
schema that follows. This first storage schema intentionally holds only catalogues
and staged daily snapshots; it does not claim the complete planned game model.

## Deployment and rollback

Postgres migration PersistNumbersContent adds two tables, a foreign key and a unique
daily-date index. Existing game data is untouched. Normal backups include both.
The prior app can run with the additional unused tables, so an application rollback
does not require dropping content. A migration downgrade deletes these new tables.

SQLite schema is now 4. Following the existing local-development policy, older local
SQLite files require a rebuild: stop the local app, preserve anything needed, remove
the throwaway database and rerun the importer. Never apply that procedure to Postgres.

Tests cover import replay, changed revisions, fresh-context recovery, immutable
content, insufficient supply and duplicate dates. The integration test exercises
the real API database initialization on SQLite and the existing Postgres CI job.
