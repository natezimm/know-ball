# 0001. A layered backend with a narrow database interface

**Status:** accepted

## Context

Know Ball has two hosts, the web API and the nightly importer, that need the same game rules, import logic and
database model. The games also need to be tested quickly, without a network or a database server.

## Decision

Split the backend into `Domain`, `Application`, `Infrastructure`, `Api` and `Importer`, with references pointing
inward. Game rules, the puzzle scheduler and the import services live in `Application` and reach the database
only through `ITriviaDb`, the slice of the EF Core context they use. Data sources sit behind interfaces
(`IHistorySource`, `ICrosswalkSource`, player sources) implemented in `Infrastructure`. Time and randomness are
injected (`TimeProvider`, `PuzzleScheduler.Random`) so tests can fix the date and the draw.

## Consequences

- Both hosts share one implementation; the importer and API cannot drift apart.
- Unit tests run the real services on SQLite with fixture CSVs, and integration tests run the whole API on SQLite
  and on Postgres from the same test code.
- `ITriviaDb` exposes EF Core `DbSet`s, so `Application` depends on EF Core's abstractions though not on a
  database provider. That is a deliberate trade: full repository interfaces would add code without adding a
  second implementation.
- Numbers Don't Lie's play service sits in `Infrastructure` because it works on JSON snapshots through the context
  directly; it is the one exception to keep in mind when moving code.
