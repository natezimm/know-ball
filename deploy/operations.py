#!/usr/bin/env python3
"""Production evidence, paired backups, and disposable restore drills (stdlib only)."""
import argparse
import contextlib
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.request
import uuid

UTC = dt.timezone.utc
IMAGE = re.compile(r"ghcr\.io/natezimm/know-ball:(?:[0-9a-f]{40})$")
BUNDLE = re.compile(r"knowball-\d{8}T\d{6}Z-[0-9a-f]{8}$")
BEGIN = "# BEGIN KNOWBALL BACKUP"
END = "# END KNOWBALL BACKUP"
AUDIT_SQL = '''SELECT json_build_object(
 'players', (SELECT count(*) FROM "Players"),
 'activePlayers', (SELECT count(*) FROM "Players" WHERE "IsActive"),
 'activeWithHeadshots', (SELECT count(*) FROM "Players" WHERE "IsActive" AND "HeadshotUrl" IS NOT NULL),
 'historyImports', (SELECT count(*) FROM "ImportRuns" WHERE "Source" = 'bref' AND "Status" = 'Succeeded'),
 'puzzles', (SELECT count(*) FROM "Puzzles"),
 'imports', (SELECT coalesce(json_agg(r), '[]'::json) FROM
   (SELECT "Id", "Source", "Status", "StartedAt", "FinishedAt" FROM "ImportRuns"
    ORDER BY "Id" DESC LIMIT 40) r),
 'queues', (SELECT coalesce(json_agg(q), '[]'::json) FROM
   (SELECT "GameType", count(*) AS days, max("Date") AS through FROM "Puzzles"
    WHERE "Status" = 'Scheduled' AND "Date" >= (now() AT TIME ZONE 'America/New_York')::date
    GROUP BY "GameType") q));'''
MEDIA_SQL = '''SELECT DISTINCT value FROM "Puzzles", LATERAL
 jsonb_each_text("SecretJson"::jsonb) WHERE key IN ('photoUrl','silhouetteUrl','blurredUrl');'''


def run(args, *, input=None, stdout=subprocess.PIPE, check=True):
    # Do not echo commands or captured stderr: Docker config/connection strings can contain secrets.
    result = subprocess.run([str(a) for a in args], input=input, stdout=stdout,
                            stderr=subprocess.PIPE, check=False)
    if check and result.returncode:
        raise RuntimeError(f"{Path(str(args[0])).name} operation failed (exit {result.returncode}); inspect locally without publishing secrets")
    return result


def compose(root, *args, **kwargs):
    return run(["docker", "compose", "--project-directory", root, "-f", root / "docker-compose.yml", *args], **kwargs)


def query(root, sql):
    return compose(root, "exec", "-T", "db", "psql", "-XAt", "-v", "ON_ERROR_STOP=1",
                   "-U", "knowball", "-d", "know_ball", input=sql.encode()).stdout.decode().strip()


def require_stack(root):
    if not (root / "docker-compose.yml").is_file() or not (root / ".env").is_file():
        raise RuntimeError("Expected the production compose file and .env in --directory")


@contextlib.contextmanager
def lock(root):
    with (root / ".operations.lock").open("a") as handle:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield


def cron_text():
    result = run(["crontab", "-l"], check=False)
    if result.returncode == 1 and b"no crontab for" in result.stderr.lower():
        return ""
    if result.returncode:
        raise RuntimeError("Cannot read the current user's crontab")
    return result.stdout.decode()


def managed_cron(existing, root, script):
    if existing.count(BEGIN) != existing.count(END) or existing.count(BEGIN) > 1:
        raise RuntimeError("Malformed managed backup cron block; review crontab manually")
    if BEGIN in existing:
        start, stop = existing.index(BEGIN), existing.index(END) + len(END)
        if stop < start:
            raise RuntimeError("Malformed managed backup cron block")
        existing = existing[:start] + existing[stop:]
    # Cron interprets percent even inside shell quotes. Use safe known paths instead.
    if any(c in str(root) + str(script) for c in "%\n\r"):
        raise RuntimeError("Cron paths cannot contain percent or newline")
    command = shlex.join([sys.executable, str(script), "--directory", str(root), "backup"])
    line = "15 7 * * * " + command + " >> " + shlex.quote(str(root / "backup.log")) + " 2>&1"
    return existing.rstrip() + "\n" + BEGIN + "\n" + line + "\n" + END + "\n"


def audit(root):
    data = json.loads(query(root, AUDIT_SQL))
    importer_id = compose(root, "ps", "-q", "importer").stdout.decode().strip()
    importer = {"running": False}
    if importer_id:
        inspected = json.loads(run(["docker", "inspect", importer_id]).stdout)[0]
        importer = {"running": inspected["State"]["Running"], "startedAt": inspected["State"]["StartedAt"],
                    "restartCount": inspected["RestartCount"]}
    logs = compose(root, "logs", "--no-color", "--since", "72h", "importer").stdout.decode()
    # Only retain scheduler and queue messages, never raw log lines or errors.
    scheduling = []
    for line in logs.splitlines():
        match = re.search(r"Next import at .+|Queued \d+ (?:Scouting Report|Career Path) puzzles", line)
        if match:
            scheduling.append(match.group(0))
    return {"checkedAt": dt.datetime.now(UTC).isoformat(), "database": data,
            "importer": importer,
            "schedulerMessages": scheduling[-20:],
            "hostTimeZone": dt.datetime.now().astimezone().tzname(),
            "managedBackupCron": BEGIN in cron_text(),
            "nightlyGate": "REVIEW_REQUIRED: ImportRuns has no trigger field; correlate timestamps with worker logs, restarts and manual runs",
            "gcpSnapshots": "NOT_CHECKED: verify schedule attachment and a recent successful snapshot in GCP"}


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def validate_bundle(bundle):
    manifest = json.loads((bundle / "manifest.json").read_text())
    if manifest.get("version") != 1 or not IMAGE.fullmatch(manifest.get("image", "")):
        raise RuntimeError("Unsupported bundle or image is not a pinned Know Ball commit")
    for name in ("database.dump", "photos.tar.gz"):
        if (bundle / name).is_symlink() or digest(bundle / name) != manifest["sha256"][name]:
            raise RuntimeError("Backup checksum mismatch")
    return manifest


def extract_photos(archive, destination):
    with tarfile.open(archive, "r:gz") as source:
        for member in source.getmembers():
            target = (destination / member.name).resolve()
            if not target.is_relative_to(destination.resolve()) or not (member.isdir() or member.isfile()):
                raise RuntimeError("Unsafe media archive member")
            if not member.name.startswith("photos/") and member.name != "photos":
                raise RuntimeError("Unexpected media archive layout")
        source.extractall(destination, filter="data")


def prune(backups, now):
    for path in backups.iterdir():
        if path.is_symlink() or not path.is_dir() or not BUNDLE.fullmatch(path.name):
            continue
        if now.timestamp() - path.stat().st_mtime > 14 * 86400:
            # Incomplete/unrecognized directories are never retention targets.
            if all((path / name).is_file() for name in ("manifest.json", "database.dump", "photos.tar.gz")):
                shutil.rmtree(path)


def backup(root):
    with lock(root):
        backups = root / "backups"
        backups.mkdir(mode=0o700, exist_ok=True)
        os.chmod(backups, 0o700)
        now = dt.datetime.now(UTC)
        name = "knowball-" + now.strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
        stage = Path(tempfile.mkdtemp(prefix=".partial-", dir=backups))
        try:
            cid = compose(root, "ps", "-q", "site").stdout.decode().strip()
            if not cid:
                raise RuntimeError("Production site is not running")
            image = json.loads(run(["docker", "inspect", cid]).stdout)[0]["Config"]["Image"]
            if not IMAGE.fullmatch(image):
                raise RuntimeError("Deploy a commit-tagged image before backing up (latest is not reproducible)")
            with (stage / "database.dump").open("wb") as out:
                compose(root, "exec", "-T", "db", "pg_dump", "-U", "knowball", "-d", "know_ball", "-Fc", stdout=out)
            if not (stage / "database.dump").stat().st_size:
                raise RuntimeError("Empty database dump")
            run(["docker", "cp", cid + ":/data/photos", stage / "photos"])
            with tarfile.open(stage / "photos.tar.gz", "w:gz") as out:
                out.add(stage / "photos", arcname="photos")
            shutil.rmtree(stage / "photos")
            manifest = {"version": 1, "startedAt": now.isoformat(), "finishedAt": dt.datetime.now(UTC).isoformat(),
                        "image": image, "sha256": {n: digest(stage / n) for n in ("database.dump", "photos.tar.gz")}}
            (stage / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
            validate_bundle(stage)
            final = backups / name
            stage.rename(final)
            prune(backups, now)  # Only after a new complete bundle exists.
            return final
        finally:
            if stage.exists():
                shutil.rmtree(stage)


def request(base, path, payload=None):
    req = urllib.request.Request(base + path, data=None if payload is None else json.dumps(payload).encode(),
                                 headers={"X-Anon-Id": "restore-drill-" + DRILL_ID, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=15) as response:
        body = response.read()
        return json.loads(body) if path.startswith("/api/") else body


DRILL_ID = uuid.uuid4().hex[:12]


def restore(bundle):
    manifest = validate_bundle(bundle)
    prefix = "knowball-restore-" + DRILL_ID
    network, db, site = prefix + "-net", prefix + "-db", prefix + "-site"
    # All created resources have fresh names. No production compose commands or volumes are used here.
    with tempfile.TemporaryDirectory(prefix=prefix) as temp:
        temp = Path(temp)
        extract_photos(bundle / "photos.tar.gz", temp)
        os.chmod(temp, 0o755)
        for path in (temp / "photos").rglob("*"):
            os.chmod(path, 0o755 if path.is_dir() else 0o644)
        os.chmod(temp / "photos", 0o755)
        password = uuid.uuid4().hex
        try:
            # A separate bridge keeps the drill apart from production while supporting
            # the loopback-only published API port used by the host-side HTTP checks.
            run(["docker", "network", "create", network])
            run(["docker", "run", "-d", "--name", db, "--network", network,
                 "-e", "POSTGRES_USER=knowball", "-e", "POSTGRES_DB=know_ball", "-e", "POSTGRES_PASSWORD=" + password, "postgres:17"])
            for attempt in range(60):
                # The entrypoint's bootstrap server accepts Unix sockets before initdb finishes.
                # TCP readiness waits for the final server and its created database.
                if run(["docker", "exec", db, "pg_isready", "-h", "127.0.0.1", "-U", "knowball", "-d", "know_ball"], check=False).returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError("Restore database did not become ready")
            with (bundle / "database.dump").open("rb") as source:
                result = subprocess.run(["docker", "exec", "-i", db, "pg_restore", "-U", "knowball", "-d", "know_ball",
                                         "--no-owner", "--no-privileges", "--exit-on-error"], stdin=source,
                                        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
                if result.returncode:
                    raise RuntimeError("Restoring the database failed")
            def sql(text):
                return run(["docker", "exec", "-i", db, "psql", "-XAt", "-v", "ON_ERROR_STOP=1", "-U", "knowball", "-d", "know_ball"], input=text.encode()).stdout.decode().strip()
            counts = json.loads(sql(AUDIT_SQL))
            if counts["players"] == 0 or counts["puzzles"] == 0:
                raise RuntimeError("Restored database is missing players or puzzles")
            urls = sql(MEDIA_SQL).splitlines()
            for url in urls:
                if not re.fullmatch(r"/photos/[0-9a-f]{32}\.(jpg|png)", url) or not (temp / url.lstrip("/")).is_file():
                    raise RuntimeError("Restored database references a missing or invalid photo")
            run(["docker", "run", "-d", "--name", site, "--network", network, "-p", "127.0.0.1::8080",
                 "-e", f"ConnectionStrings__Trivia=Host={db};Database=know_ball;Username=knowball;Password={password}",
                 "-e", "Database__ApplyMigrationsOnStartup=false", "-v", f"{temp / 'photos'}:/data/photos:ro", manifest["image"]])
            port = run(["docker", "port", site, "8080/tcp"]).stdout.decode().strip().rsplit(":", 1)[1]
            base = "http://127.0.0.1:" + port
            for attempt in range(60):
                try:
                    request(base, "/health")
                    break
                except (urllib.error.URLError, TimeoutError):
                    time.sleep(1)
            else:
                raise RuntimeError("Restored API did not become healthy")
            played = []
            pool = [int(value) for value in sql('SELECT "Id" FROM "Players" WHERE "IsActive" ORDER BY "Id" LIMIT 8;').splitlines()]
            for slug in ("scouting-report", "career-path"):
                state = request(base, f"/api/{slug}/daily")
                for player in pool:
                    if state["status"] in ("won", "lost"):
                        break
                    state = request(base, f"/api/{slug}/{state['puzzleId']}/guess", {"playerId": player})
                if state["status"] not in ("won", "lost") or not state.get("answer"):
                    raise RuntimeError("Restored game did not reach a result")
                played.append(slug)
            for url in urls:
                if not request(base, url):
                    raise RuntimeError("Restored image is empty")
            return {"restoredAt": dt.datetime.now(UTC).isoformat(), "bundle": bundle.name, "image": manifest["image"],
                    "players": counts["players"], "puzzles": counts["puzzles"], "photosVerified": len(urls),
                    "completedGames": played, "curatedPhotoCheck": "passed" if urls else "PENDING: no curated images in backup",
                    "productionModified": False}
        finally:
            run(["docker", "rm", "-fv", site, db], check=False)
            run(["docker", "network", "rm", network], check=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=Path.home() / "know-ball")
    parser.add_argument("command", choices=["audit", "backup", "install-backups", "restore"])
    parser.add_argument("--backup", type=Path)
    args = parser.parse_args()
    os.umask(0o077)
    root = args.directory.resolve()
    if args.command == "restore":
        if args.backup is None:
            parser.error("restore requires --backup")
        result = restore(args.backup.resolve())
    else:
        require_stack(root)
        if args.command == "audit":
            result = audit(root)
        elif args.command == "backup":
            result = {"backup": str(backup(root))}
        else:
            script = Path(__file__).resolve()
            if script.parent != root:
                raise RuntimeError("Install operations.py in the production directory first")
            with lock(root):
                updated = managed_cron(cron_text(), root, script)
                (root / "backup.log").touch(mode=0o600, exist_ok=True)
                os.chmod(root / "backup.log", 0o600)
                run(["crontab", "-"], input=updated.encode())
            result = {"backupCron": "installed: 07:15 host time daily", "retentionDays": 14,
                      "note": "Run backup and the restore drill now; verify host timezone and GCP snapshots separately"}
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError, KeyError, tarfile.TarError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)
