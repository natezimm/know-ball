# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue: use **Report a vulnerability** on this repository's [Security tab](https://github.com/natezimm/know-ball/security/advisories/new). Include what you found, how to reproduce it and what it lets someone do. You should hear back within a week.

You are welcome to test [knowball.nathanzimmerman.com](https://knowball.nathanzimmerman.com) gently; it is rate limited. Please do not run load tests or try to reach the server itself.

Players are anonymous by design: the browser's `X-Anon-Id` is an identity, not a secret ([ADR 0003](docs/adr/0003-anonymous-players.md)), so changing it to someone else's is not a vulnerability. Seeing a puzzle's answer before it is won or lost, or playing a daily puzzle before its date, is ([ADR 0002](docs/adr/0002-answers-stay-on-the-server.md)).

## Supported versions

Only what is on `main` is supported; every merge to `main` deploys.
