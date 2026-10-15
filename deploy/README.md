# Deploying Know Ball

Know Ball runs on `nz-portfolio-vm` (GCP, us-east1) at **https://knowball.nathanzimmerman.com**, next to the
other apps on that machine. The host's nginx owns ports 80 and 443 and forwards this domain to the site on
`127.0.0.1:8090`. Postgres and the importer run beside it in Docker and are not reachable from outside.

Every push to `main` that passes CI publishes the image to `ghcr.io/natezimm/know-ball` and then the `deploy`
job copies `deploy/docker-compose.yml` to `~/know-ball` on the VM, pulls that commit's image and restarts.
The VM never builds anything.

## One-time setup

Run these on the VM (`SSH` from the GCP console) unless a step says otherwise.

### 1. DNS

Add an A record `knowball` pointing to `104.196.14.53` at the DNS host for `nathanzimmerman.com`. Check that
the VM's external IP is **static** (GCP console, VPC network, IP addresses); an ephemeral one changes when
the VM is stopped, for example to resize it.

### 2. Docker access

```sh
sudo usermod -aG docker $USER   # then log out and back in
docker compose version
```

### 3. Settings

Two random secrets in `~/know-ball/.env`. Keep a copy of the admin key in your password manager; it is what
you type into `/admin`.

```sh
mkdir -p ~/know-ball/backups
printf 'KNOWBALL_DB_PASSWORD=%s\nKNOWBALL_ADMIN_KEY=%s\n' "$(openssl rand -hex 24)" "$(openssl rand -hex 24)" > ~/know-ball/.env
chmod 600 ~/know-ball/.env
grep ADMIN ~/know-ball/.env
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
cat ~/.ssh/knowball_deploy                                          # → secret DEPLOY_SSH_KEY
awk '{print "104.196.14.53", $1, $2}' /etc/ssh/ssh_host_ed25519_key.pub   # → secret DEPLOY_KNOWN_HOSTS
rm ~/.ssh/knowball_deploy                                           # once the secret is saved
```

Then in GitHub, **Settings → Secrets and variables → Actions → New repository secret**, add:

| Secret | Value |
| --- | --- |
| `DEPLOY_HOST` | `104.196.14.53` |
| `DEPLOY_USER` | your user on the VM (`nathan_a_zimmerman`) |
| `DEPLOY_SSH_KEY` | the private key printed above, all of it |
| `DEPLOY_KNOWN_HOSTS` | the line the `awk` command printed |

If the deploy job says `Permission denied (publickey)`, the VM probably uses OS Login, which ignores
`~/.ssh/authorized_keys`. Turn it off for this VM (metadata `enable-oslogin=FALSE`) or ask Claude to switch the
job to a service account. GCP's default firewall rule already lets SSH in from anywhere.

### 6. First deploy

Re-run the latest CI run on `main` (Actions → CI → Re-run all jobs) or merge anything. The `deploy` job
skips with a notice until `DEPLOY_HOST` is set.

On its first start the importer downloads the full player history, which takes a while; follow it with
`cd ~/know-ball && docker compose logs -f importer`, and watch memory with `docker stats`. When it is done,
`curl -s https://knowball.nathanzimmerman.com/api/import-runs/latest` reports `Succeeded`.

### 7. Nightly backups

`crontab -e` and add:

```
15 3 * * * docker exec knowball-db-1 pg_dump -U knowball know_ball | gzip > ~/know-ball/backups/know_ball-$(date +\%F).sql.gz && find ~/know-ball/backups -mtime +14 -delete
```

Photos live in the `knowball_photos` volume; a GCP snapshot schedule on the VM's disk covers both.

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
