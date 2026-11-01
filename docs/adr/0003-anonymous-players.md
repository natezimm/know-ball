# 0003. Anonymous players instead of accounts

**Status:** accepted

## Context

Stats, streaks, the archive and resumable rounds need to know which browser is playing. Sign-up would add
friction to a daily game played in a minute or two, and storing accounts brings privacy and security work that
a personal project does not need yet.

## Decision

The browser makes up a random id on its first visit, keeps it in `localStorage` and sends it as `X-Anon-Id`
(`web/know-ball/src/app/core/anon-id.ts`). The API keys player-game results and Numbers Don't Lie plays by it.
Spordle rounds are not tied to it: a round is reached by its random round id, which the browser keeps, and expires
after six idle hours.

## Consequences

- No sign-up and no personal data stored.
- Clearing site data or switching devices starts a player over. This is identity, not authentication: anyone who
  copies an id can play as it, which is acceptable for game progress and nothing else.
- Accounts can be added later by linking an account to one or more anonymous ids.
