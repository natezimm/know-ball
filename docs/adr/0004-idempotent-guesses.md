# 0004. Guesses are idempotent and safe across tabs

**Status:** accepted

## Context

Phones drop requests. A guess whose response is lost gets retried, and without care that retry spends a second
guess. Two open tabs can also guess at the same moment and overwrite each other's progress.

## Decision

Guess requests carry an optional client-generated `requestId`. The server records it with the guess; a retry with
the same id returns the original result, and the same id with a different guess returns 409. For the player
games, a guess is saved with a compare-and-swap: the update applies only if the stored guess list is unchanged
since it was read, otherwise the guess is evaluated again on the fresh state (`GameService.GuessAsync`). New
results rely on the unique `(UserKey, PuzzleId)` index. Numbers Don't Lie moves also send an expected revision.

## Consequences

- A retried request never costs a guess, and concurrent tabs cannot lose each other's guesses.
- The guard works across several API instances and needed no new columns: the request id lives inside the
  existing guess JSON.
- Older clients that omit `requestId` still work, without the retry protection.
