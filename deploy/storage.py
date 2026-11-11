#!/usr/bin/env python3
"""Remove only unused Know Ball commit images; preserve backup recovery images."""
import argparse
from datetime import datetime, timedelta, timezone
import fcntl
import json
import os
from pathlib import Path
import re
import subprocess

REPO = 'ghcr.io/natezimm/know-ball'
TAG = re.compile(r'[0-9a-f]{40}')
BUNDLE = re.compile(r'knowball-\d{8}T\d{6}Z-[0-9a-f]{8}')


def command(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout


def read(path):
    if path.is_symlink():
        raise ValueError('Refusing symlinked recovery metadata')
    return path.read_text()


def protected_tags(root):
    tags = set()
    def protect(image):
        if not isinstance(image, str) or not image.startswith(REPO + ':') or not TAG.fullmatch(image[len(REPO) + 1:]):
            raise ValueError('Cannot identify a recovery image; cleanup stopped')
        tags.add(image[len(REPO) + 1:])
    envs = [root / '.env']
    migration = root / '.postgres-upgrade-backups'
    backups = root / 'backups'
    if migration.is_symlink() or backups.is_symlink():
        raise ValueError('Refusing symlinked backup directory')
    for directory in migration.iterdir() if migration.exists() else []:
        if directory.is_symlink():
            raise ValueError('Refusing symlinked migration backup')
        env = directory / 'instance.env'
        if env.exists():
            envs.append(env)
        recovery = directory / 'rollback-compose.json'
        if recovery.exists():
            for service in json.loads(read(recovery))['services'].values():
                image = service.get('image', '')
                if image.startswith(REPO + ':'):
                    protect(image)
    for env in envs:
        found = False
        for line in read(env).splitlines():
            if line.startswith('KNOWBALL_TAG='):
                protect(REPO + ':' + line.split('=', 1)[1])
                found = True
        if not found:
            raise ValueError('Missing recovery image tag; cleanup stopped')
    for directory in backups.iterdir() if backups.exists() else []:
        if not BUNDLE.fullmatch(directory.name):
            continue
        if directory.is_symlink():
            raise ValueError('Refusing symlinked backup bundle')
        manifest = json.loads(read(directory / 'manifest.json'))
        if manifest.get('version') != 1:
            raise ValueError('Unknown backup manifest version')
        protect(manifest.get('image'))
    return tags


def candidates(root):
    tags = protected_tags(root)
    containers = command('docker', 'ps', '-aq').split()
    protected = set(command('docker', 'inspect', '--format', '{{.Image}}', *containers).split()) if containers else set()
    images = [json.loads(line) for line in command('docker', 'image', 'ls', '--no-trunc', '--format', '{{json .}}').splitlines() if line]
    # Protect aliases of images retained for recovery as well.
    protected.update(image['ID'] for image in images if image['Repository'] == REPO and image['Tag'] in tags)
    eligible_ids = {image['ID'] for image in images if image['Repository'] == REPO and TAG.fullmatch(image['Tag']) and image['ID'] not in protected}
    old_ids = set()
    if eligible_ids:
        cutoff = datetime.now(timezone.utc) - timedelta(days=1)
        for line in command('docker', 'image', 'inspect', '--format', '{{.Id}} {{.Created}}', *sorted(eligible_ids)).splitlines():
            image_id, created = line.split()
            if datetime.fromisoformat(re.sub(r'(\.\d{6})\d+', r'\1', created).replace('Z', '+00:00')) < cutoff:
                old_ids.add(image_id)
    return sorted({REPO + ':' + image['Tag'] for image in images
                   if image['Repository'] == REPO and TAG.fullmatch(image['Tag'])
                   and image['Tag'] not in tags and image['ID'] not in protected and image['ID'] in old_ids})


def cleanup(root, dry_run=False):
    os.chdir(root)
    with (root / '.operations.lock').open('a') as handle:
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('Deployment or backup active; cleanup deferred')
            return 0
        images = candidates(root)
        if not dry_run:
            for image in images:
                command('docker', 'image', 'rm', image)
        print(f'{"Eligible" if dry_run else "Removed"} unused Know Ball image tags: {len(images)}')
        return len(images)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['plan', 'cleanup'])
    parser.add_argument('--root', type=Path, default=Path.home() / 'know-ball')
    args = parser.parse_args()
    cleanup(args.root.resolve(), args.action == 'plan')


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError):
        # Never echo Docker output or metadata: it can contain production secrets.
        print('Storage maintenance failed; inspect private metadata and Docker state', file=__import__('sys').stderr)
        raise SystemExit(1)
