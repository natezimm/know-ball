"""Failure-path and safety checks for production backup tooling; no external services."""
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("operations", Path(__file__).parents[1] / "deploy/operations.py")
ops = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ops)


class OperationsTests(unittest.TestCase):
    def test_cron_install_is_idempotent_and_preserves_other_jobs(self):
        other = "0 2 * * * /usr/local/bin/other-backup\n"
        root = Path("/home/deploy/know-ball")
        first = ops.managed_cron(other, root, root / "operations.py")
        self.assertEqual(first, ops.managed_cron(first, root, root / "operations.py"))
        self.assertIn(other.strip(), first)
        self.assertEqual(first.count(ops.BEGIN), 1)

    def test_cron_refuses_malformed_blocks_and_percent_paths(self):
        for existing, root in [(ops.BEGIN, Path("/tmp/a")), ("", Path("/tmp/a%b"))]:
            with self.assertRaises(RuntimeError):
                ops.managed_cron(existing, root, root / "operations.py")

    def test_failed_dump_never_publishes_or_prunes_a_backup(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            def compose(*args, **kwargs):
                if "pg_dump" in args:
                    kwargs["stdout"].write(b"partial")
                    raise RuntimeError("dump failed")
                return subprocess.CompletedProcess([], 0, b"site-id", b"")
            inspected = [{"Config": {"Image": "ghcr.io/natezimm/know-ball:" + "a" * 40}}]
            with patch.object(ops, "compose", side_effect=compose), patch.object(ops, "run", return_value=subprocess.CompletedProcess([], 0, json.dumps(inspected).encode(), b"")), patch.object(ops, "prune") as prune:
                with self.assertRaisesRegex(RuntimeError, "dump failed"):
                    ops.backup(root)
                prune.assert_not_called()
            self.assertEqual(list((root / "backups").iterdir()), [])

    def test_restore_rejects_tampering_before_starting_docker(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ("database.dump", "photos.tar.gz"):
                (root / name).write_bytes(b"backup")
            manifest = {"version": 1, "image": "ghcr.io/natezimm/know-ball:" + "a" * 40,
                        "sha256": {n: ops.digest(root / n) for n in ("database.dump", "photos.tar.gz")}}
            (root / "manifest.json").write_text(json.dumps(manifest))
            (root / "database.dump").write_bytes(b"tampered")
            with patch.object(ops, "run") as run:
                with self.assertRaisesRegex(RuntimeError, "checksum"):
                    ops.restore(root)
                run.assert_not_called()

    def test_media_extract_rejects_traversal_and_links(self):
        for name, kind in [("photos/../../escape", tarfile.REGTYPE), ("photos/link", tarfile.SYMTYPE)]:
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                archive = root / "photos.tar.gz"
                with tarfile.open(archive, "w:gz") as out:
                    item = tarfile.TarInfo(name)
                    item.type = kind
                    item.linkname = "/etc/passwd" if kind == tarfile.SYMTYPE else ""
                    out.addfile(item, io.BytesIO(b""))
                with self.assertRaises(RuntimeError):
                    ops.extract_photos(archive, root / "restore")

    def test_retention_leaves_unrelated_and_incomplete_directories(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ("unrelated", "knowball-20200101T000000Z-12345678"):
                (root / name).mkdir()
            ops.prune(root, ops.dt.datetime(2030, 1, 1, tzinfo=ops.UTC))
            self.assertEqual(len(list(root.iterdir())), 2)


if __name__ == "__main__":
    unittest.main()
