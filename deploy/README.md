# Deploying Know Ball

Know Ball runs on `portfolio-vm` (GCP, us-east1) at **https://knowball.nathanzimmerman.com**, next to the
other apps on that machine. The host's nginx owns ports 80 and 443 and forwards this domain to the site on
`127.0.0.1:8090`. Postgres and the importer run beside it in Docker and are not reachable from outside.

Every push to `main` that passes CI publishes the image to `ghcr.io/natezimm/know-ball` and then the `deploy`
job copies `deploy/docker-compose.yml` to `~/know-ball` on the VM, pulls that commit's image and restarts.
The VM never builds anything.

## One-time setup

Run these on the VM (`SSH` from the GCP console) unless a step says otherwise.

### 1. DNS

Add an A record `knowball` pointing to `203.0.113.10` at the DNS host for `nathanzimmerman.com`. Check that
the VM's external IP is **static** (GCP console, VPC network, IP addresses); an ephemeral one changes when
the VM is stopped, for example to resize it.

### 2. Docker access

```sh
sudo usermod -aG docker $USER   # then log out and back in
docker compose version
```

### 3. Settings

One database secret in `~/know-ball/.env`. The photo-game admin was removed; an existing
`KNOWBALL_ADMIN_KEY` entry is ignored and does not need to be changed for deployment.

```sh
mkdir -p ~/know-ball/backups
printf 'KNOWBALL_DB_PASSWORD=%s\n' "$(openssl rand -hex 24)" > ~/know-ball/.env
chmod 600 ~/know-ball/.env
```

Never change `KNOWBALL_DB_PASSWORD` after the first start: Postgres keeps the one it was created with.

### 4. nginx and HTTPS

```sh
sudo tee /etc/nginx/sites-available/knowball.nathanzimmerman.com.conf > /dev/null <<'NGINX'
# paste the contents of deploy/nginx/knowball.nathanzimmerman.com.conf here
NGINX
sudo ln -s /etc/nginx/sites-available/knowball.nathanzimmerman.com.conf /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d knowball.nathanzimmerman.com
```

Certbot needs the DNS record from step 1 to be live. It adds the HTTPS server and the redirect from HTTP, and
renews the certificate with the others.

### 5. A deploy key for GitHub Actions

```sh
ssh-keygen -t ed25519 -f ~/.ssh/knowball_deploy -N "" -C knowball-deploy
cat ~/.ssh/knowball_deploy.pub >> ~/.ssh/authorized_keys
cat ~/.ssh/knowball_deploy                                          # → secret GCP_SSH_KEY
awk '{print "203.0.113.10", $1, $2}' /etc/ssh/ssh_host_ed25519_key.pub   # → secret GCP_KNOWN_HOSTS
rm ~/.ssh/knowball_deploy                                           # once the secret is saved
```

Then in GitHub, **Settings → Secrets and variables → Actions → New repository secret**, add:

| Secret | Value |
| --- | --- |
| `GCP_HOST` | `203.0.113.10` |
| `GCP_USER` | your user on the VM (`deploy-user`) |
| `GCP_SSH_KEY` | the private key printed above, all of it |
| `GCP_KNOWN_HOSTS` | the line the `awk` command printed. It pins the VM's identity, and the deploy refuses to run without it |

If the deploy job says `Permission denied (publickey)`, the VM probably uses OS Login, which ignores
`~/.ssh/authorized_keys`. Turn it off for this VM (metadata `enable-oslogin=FALSE`) or ask Claude to switch the
job to a service account. GCP's default firewall rule already lets SSH in from anywhere.

### 6. First deploy

Re-run the latest CI run on `main` (Actions → CI → Re-run all jobs) or merge anything. The `deploy` job
skips with a notice until `GCP_HOST` is set.

On its first start the importer downloads the full player history, which takes a while; follow it with
`cd ~/know-ball && docker compose logs -f importer`, and watch memory with `docker stats`. It is done when
the log says `Next import at ...`. To see every run, history included (`/api/import-runs/latest` shows only
the newest, which is the ESPN roster run):

```sh
cd ~/know-ball && docker compose exec db psql -U knowball -d know_ball -c \
  'select "Id", "Source", "Status", "StartedAt", "FinishedAt", "PlayersInserted", "PlayersUpdated", "Error" from "ImportRuns" order by "Id";'
```

Every `docker compose` command needs to run from `~/know-ball`, where the compose file is.

### 7. Production evidence, backups and recovery

After the operations workflow is merged to main, open **Actions → Production operations → Run workflow**.
It reuses `GCP_HOST`, `GCP_USER`, `GCP_SSH_KEY` and `GCP_KNOWN_HOSTS`; no new credentials are needed.
It serializes with CI deployments, and only runs from main.

| Action | What it does |
| --- | --- |
| `audit` (default) | Copies the operations helper to the VM, then reads production import timestamps/status, player/headshot counts, puzzle queues, selected scheduler messages and the managed backup cron state. It does not run an import or change application data. |
| `backup-and-restore` | Audits, saves a paired database/photos bundle on the VM, and restores it on the disposable GitHub runner. The drill completes Scouting Report and Career Path through the HTTP API and checks all stored legacy photo references against archived files. |
| `install-backups` | Also installs/updates a managed cron block for daily paired backups, then takes a backup and runs the restore drill immediately. Other cron jobs are preserved. |

The VM needs Python 3.12+ (Ubuntu 24.04 provides it), Docker Compose and `crontab`. The helper remains at
`~/know-ball/operations.py` for cron. The restore runs on GitHub's runner to avoid loading another Postgres/API
pair onto the shared VM. Only sanitized JSON reports become workflow artifacts (14-day retention). Database
dumps, photos and credentials are not uploaded as artifacts; the runner's temporary backup copy is removed.
The VM retains bundles under `~/know-ball/backups/knowball-<UTC timestamp>-<suffix>/`.

A bundle contains `database.dump` (Postgres custom format), `photos.tar.gz`, and a manifest with checksums,
timestamps and the exact deployed commit-tagged image. A failed dump never publishes a completed bundle or
prunes old backups. Retention removes only complete, tool-named bundles older than 14 days, after a new backup
succeeds. Keep an off-VM copy through snapshots as described below.

**Before installing:** remove the old example cron line that starts with
`15 3 * * * docker exec knowball-db-1 pg_dump`, if present. It backed up only the database, and its shell pipeline
could hide a failed dump. The new helper does not delete unrelated cron entries. Its schedule is **07:15 in the
VM host's timezone**; inspect `hostTimeZone` in the audit report and adjust the managed cron block if needed.
Each install restores the documented 07:15 host-time schedule.

The backup still pairs the database dump with the legacy photo volume. Existing archived photo references
are checked against restored files, even though the retired game no longer serves them. A database with no
photo puzzles is a valid three-game deployment; no photo inventory is required for launch.

To use the helper directly after it has been copied to the VM:

```sh
python3 ~/know-ball/operations.py audit
python3 ~/know-ball/operations.py install-backups
python3 ~/know-ball/operations.py backup
```

Do not run the restore drill on the shared VM by default. On an isolated Docker host with registry access and
Python 3.12+, copy one complete bundle and run:

```sh
python3 deploy/operations.py restore --backup /absolute/path/to/backup-bundle
```

The drill checks checksums before starting Docker, creates a private temporary network and new database/API
containers, mounts only extracted backup photos, binds the API to a random loopback port and removes only its
own resources. It never restores into production or starts an importer. The cloned database contains anonymous
game history and puzzle answers: handle the bundle as private data.

**Record gate evidence:** attach the workflow run URL and timestamps to
[the launch checklist](../docs/implementation-plan.md#8-whats-left). `ImportRuns` has no trigger field, so
timestamps near 04:00 Eastern are candidates, not proof of a scheduled run. Correlate them with the worker's
scheduler messages, VM/container restarts and any manual imports. Check two consecutive nights; on October 17,
the planned October 18 run has not happened yet. Neither a green canary nor a restore drill closes that gate.

### 8. GCP disk snapshots (still a separate check)

In GCP Compute Engine, inspect the VM's disks and confirm that the disk holding Docker's volumes has a snapshot
schedule attached. Record its retention, a recent successful snapshot and the VM/disk name in the launch
checklist. Do not assume a snapshot policy exists because Docker volumes are persistent.

The workflow has SSH credentials, not GCP API credentials, and does not create or verify snapshot schedules.
Keep the backup and recovery gates open until the reports and this GCP evidence have been reviewed.

## Everyday

```sh
cd ~/know-ball
docker compose ps                 # what is running
docker compose logs --tail 100 site
docker compose restart site
```

To roll back, set `KNOWBALL_TAG` in `.env` to an earlier commit's full SHA and run `docker compose up -d`.
The deploy job only has a registry login while it runs, so before a manual `docker compose pull`, log in with
a GitHub token that can read packages: `docker login ghcr.io -u natezimm`.
