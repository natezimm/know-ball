# 0002. Answers stay on the server until a game is over

**Status:** accepted

## Context

A daily puzzle is only fun if nobody can read the answer out of the network tab. Anything sent to the browser,
including fields the page does not show, is public.

## Decision

Each `Puzzle` stores what a player may see (`PayloadJson`) separately from what they may not (`SecretJson`).
Guesses are evaluated on the server against the secret. `GameService.BuildState` attaches the answer only when
the result is won or lost, and a daily puzzle cannot be fetched before its Eastern date. Spordle keeps its word
in the session row and Numbers Don't Lie returns a solution only for questions already answered, with opaque
question and option ids.

## Consequences

- The answer cannot leak through a forgotten field, because the public payload never contains it.
- Every guess is a round trip. Guess endpoints are rate limited separately from page loads to keep that cheap
  to defend (see [api.md](../api.md#rate-limits)).
- The integration and Docker smoke tests assert that unfinished responses carry no answer.
