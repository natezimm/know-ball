# 0005. The nightly import refuses bad data rather than trusting it

**Status:** accepted

## Context

The games depend on free upstream feeds (a Basketball-Reference CSV mirror, Wikidata, ESPN) that can change
format, go down or return partial data without warning. A naive import of an empty roster would retire every
player and leave no puzzles.

## Decision

- Each roster import applies all of its changes in one save and writes an `ImportRun` row; a failure discards
  the pending changes and keeps only the failed run record.
- A feed that returns under half the currently active players fails the run instead of deactivating the league
  (`PlayerImportService.MinRetainedFraction`).
- Imports are repeatable: running twice with the same data changes nothing, which CI checks against Postgres.
- Runs retry, can fall back to a second source, and top up the puzzle queues even when the roster step fails,
  since yesterday's rosters still make fair puzzles. Thirty days of puzzles stay queued ahead.
- A scheduled workflow runs the import against the live sources every night on a throwaway database, so an
  upstream change is caught before the production run.

## Consequences

- A bad night upstream leaves the site on yesterday's data rather than broken.
- A genuine mass roster change (a new league season) needs the threshold in mind; it is reported, not applied.
- `GET /api/import-runs/latest` gives monitoring a single place to look.
