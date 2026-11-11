"""Storage retention protects recovery images and never prunes unrelated data."""
import fcntl
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('storage', Path(__file__).parents[1] / 'deploy/storage.py')
storage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(storage)


class StorageTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        (self.root / '.env').write_text('KNOWBALL_TAG=' + 'a' * 40 + '\n')
        self.bundle = self.root / 'backups/knowball-20261111T000000Z-12345678'
        self.bundle.mkdir(parents=True)
        (self.bundle / 'manifest.json').write_text(json.dumps({'version': 1, 'image': storage.REPO + ':' + 'b' * 40}))

    def test_candidates_preserve_current_backup_migration_container_and_alias(self):
        migration = self.root / '.postgres-upgrade-backups/snapshot'
        migration.mkdir(parents=True)
        (migration / 'instance.env').write_text('KNOWBALL_TAG=' + 'c' * 40 + '\n')
        images = [{'Repository': storage.REPO, 'Tag': tag * 40, 'ID': image_id}
                  for tag, image_id in [('a','current'), ('b','backup'), ('c','migration'),
                                       ('d','stopped'), ('e','backup'), ('f','unused')]]
        images += [{'Repository': 'other', 'Tag': 'f' * 40, 'ID': 'other'},
                   {'Repository': storage.REPO, 'Tag': 'latest', 'ID': 'unknown'}]
        with patch.object(storage, 'command', side_effect=['container', 'stopped', '\n'.join(map(json.dumps, images)), 'unused 2020-01-01T00:00:00.123456789Z']):
            self.assertEqual(storage.candidates(self.root), [storage.REPO + ':' + 'f' * 40])

    def test_recent_images_are_left_for_in_flight_deployments(self):
        from datetime import datetime, timezone
        image = {'Repository':storage.REPO, 'Tag':'f'*40, 'ID':'recent'}
        outputs = ['', json.dumps(image), 'recent ' + datetime.now(timezone.utc).isoformat()]
        with patch.object(storage, 'command', side_effect=outputs):
            self.assertEqual(storage.candidates(self.root), [])

    def test_corrupt_missing_or_symlinked_metadata_stops_cleanup_before_docker(self):
        manifest = self.bundle / 'manifest.json'
        for data in ['invalid', '{}', '{"version":1,"image":"other:latest"}']:
            manifest.write_text(data)
            with patch.object(storage, 'command') as command:
                with self.assertRaises((ValueError, KeyError)):
                    storage.candidates(self.root)
                command.assert_not_called()
        manifest.unlink()
        with self.assertRaises(OSError):
            storage.candidates(self.root)
        manifest.symlink_to(self.root / '.env')
        with self.assertRaises(ValueError):
            storage.candidates(self.root)

    def test_missing_configured_tag_stops_cleanup(self):
        (self.root / '.env').write_text('OTHER=1\n')
        with self.assertRaises(ValueError):
            storage.protected_tags(self.root)

    def test_busy_operation_defers_cleanup(self):
        cwd = Path.cwd()
        self.addCleanup(__import__('os').chdir, cwd)
        with (self.root / '.operations.lock').open('a') as handle:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            with patch.object(storage, 'candidates') as candidates:
                self.assertEqual(storage.cleanup(self.root), 0)
                candidates.assert_not_called()

    def test_plan_does_not_remove_and_cleanup_never_forces(self):
        cwd = Path.cwd()
        self.addCleanup(__import__('os').chdir, cwd)
        image = storage.REPO + ':' + 'f' * 40
        with patch.object(storage, 'candidates', return_value=[image]), patch.object(storage, 'command') as command:
            self.assertEqual(storage.cleanup(self.root, True), 1)
            command.assert_not_called()
            self.assertEqual(storage.cleanup(self.root), 1)
            command.assert_called_once_with('docker', 'image', 'rm', image)


if __name__ == '__main__':
    unittest.main()
