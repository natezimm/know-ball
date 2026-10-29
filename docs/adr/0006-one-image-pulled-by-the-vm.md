# 0006. One image, built in CI and pulled by the VM

**Status:** accepted

## Context

The site runs on a small shared GCP VM next to other apps. Building on the server would be slow, would need the
SDKs installed there, and would make each deploy a little different.

## Decision

One Docker image holds the API, the built Angular app and the importer, running as a non-root user. CI builds it
once, smoke-tests it on a fresh Postgres, publishes it to GitHub's registry tagged with the commit, and the deploy
job tells the VM to pull that tag and restart with Docker Compose. The SSH step pins the VM's host key, and the job
fails unless `/health` answers within the retry window. Postgres runs in production; tests and quick local runs
use SQLite through the same EF Core model.

## Consequences

- What was tested is exactly what runs, and rolling back is pulling an earlier tag.
- The VM needs only Docker and nginx.
- SQLite and Postgres differ in places (for example date comparisons), so the integration tests run on both.
