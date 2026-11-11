# PostgreSQL 17 → 18

Development, production, CI and restore drills use `postgres:18.6-bookworm`.
Major PostgreSQL versions require dump/restore. The 18 image mounts its volume
at `/var/lib/postgresql` and stores data under `18/docker`. The default volume
name remains the project's existing `pgdata`, so an unmigrated 17 volume fails
instead of silently opening an empty database. `KNOWBALL_DB_VOLUME` selects the
migrated volume.

## Local migration

Stop host-side API/importer processes. From the repository root, with the old
database container still present:

```sh
python3 deploy/upgrade-postgres.py --check --project-name sports-trivia
python3 deploy/upgrade-postgres.py --project-name sports-trivia
```

Use the existing Compose project name if different. Retain
`COMPOSE_PROJECT_NAME=<name>` in `.env` if it differs from the directory name.
The command initializes a separate volume, stops this project's running
writers, dumps/restores the database and compares every public table's row count.
Only then does it update `.env`, replace the database container, verify version
and counts again, and restart the writers that were running. Photos and the old
17 volume are retained. Failed cutover restores the old database and `.env`.

Private snapshots live in `.postgres-upgrade-backups/<timestamp>/`: the dump,
SHA-256 digest, counts, original `.env` when present, and `rollback-compose.json`.
Keep them private and copy the verified snapshot off the machine. Failed
attempts retain their new volume for inspection; retry with a fresh
`--target-volume <name>`.

## Production migration before the next deploy

Schedule maintenance, suspend deployments, and take and verify a normal complete
backup using `operations.py backup`, including legacy photos. Copy the new
`upgrade-postgres.py` to `~/know-ball/upgrade-postgres.py` and production Compose
file to `~/know-ball/docker-compose.pg18.yml`, retaining the active Compose file.

```sh
python3 ~/know-ball/upgrade-postgres.py \
  --directory ~/know-ball \
  --compose-file docker-compose.pg18.yml \
  --project-name knowball \
  --operations-lock ~/know-ball/.operations.lock
```

The operations lock prevents overlap with scheduled backups. Check `/health`,
search and daily games, then install the new file as
`~/know-ball/docker-compose.yml`. Take a fresh complete backup and run its normal
restore drill before resuming deployments. CI refuses to replace the active
Compose file while its database is still PostgreSQL 17.

Later manual rollback requires stopping writers and using the saved
`rollback-compose.json` and original `.env`. This returns to data as of the
upgrade; reconcile later writes first. Retain the 17 volume until application
and backup/restore checks pass.

## Rehearsal

`python3 deploy/test-postgres-upgrade.py` checks migration of seeded data, JSON,
foreign keys and a view, a deliberately failed cutover, and writer restart on
disposable Docker resources. CI runs it in the Docker job. Keep both scripts
aligned with their copies in Wheelhouse. Run integration tests on an isolated
database: they import fixtures and make gameplay writes.
