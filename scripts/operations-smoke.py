#!/usr/bin/env python3
"""Exercise real backup/restore against offline fixtures using the already-built know-ball image."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("operations", ROOT / "deploy/operations.py")
ops = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ops)
image = "ghcr.io/natezimm/know-ball:" + "0" * 40
ops.run(["docker", "tag", "know-ball", image])

with tempfile.TemporaryDirectory(prefix="knowball-ops-smoke-") as directory:
    root = Path(directory)
    (root / ".env").write_text("# offline test stack\n")
    compose = {
        "name": "knowball-ops-test-" + uuid.uuid4().hex[:8],
        "services": {
            "db": {"image": "postgres:17", "environment": {
                "POSTGRES_USER": "knowball", "POSTGRES_DB": "know_ball", "POSTGRES_PASSWORD": "test-only"}},
            "site": {"image": image, "environment": {
                "ConnectionStrings__Trivia": "Host=db;Database=know_ball;Username=knowball;Password=test-only",
                "Admin__Key": "test-only-admin-key"}, "ports": ["127.0.0.1::8080"]},
        },
    }
    # Compose accepts JSON, which avoids a YAML dependency in this stdlib smoke test.
    (root / "docker-compose.yml").write_text(json.dumps(compose))
    try:
        ops.compose(root, "up", "-d", "db")
        for attempt in range(60):
            if ops.compose(root, "exec", "-T", "db", "pg_isready", "-U", "knowball", check=False).returncode == 0:
                break
            time.sleep(1)
        else:
            raise RuntimeError("Fixture database did not become ready")
        ops.compose(root, "up", "-d", "site")
        port = ops.compose(root, "port", "site", "8080").stdout.decode().strip().rsplit(":", 1)[1]
        base = "http://127.0.0.1:" + port
        for attempt in range(60):
            try:
                ops.request(base, "/health")
                break
            except OSError:
                time.sleep(1)
        else:
            raise RuntimeError("Fixture API did not become healthy")
        ops.compose(root, "run", "--rm", "--no-deps", "-T", "--entrypoint", "dotnet",
                    "-v", str(ROOT / "tests/fixtures/bref") + ":/fixtures:ro",
                    "-e", "Sources__Bref__Directory=/fixtures", "-e", "Sources__Wikidata__Endpoint=http://127.0.0.1:9/sparql",
                    "site", "/app/importer/KnowBall.Importer.dll", "--once", "--source", "seed", "--history")
        # Reuse the generated photo/cut-out PNGs from the existing smoke test's drawing algorithm.
        import struct
        import zlib
        def png(clear):
            rows = b"".join(b"\x00" + b"".join(bytes((200, 80, 40, 255)) if 15 <= x < 45 else
                              bytes((0, 0, 0, 0) if clear else (70, 130, 180, 255)) for x in range(60)) for _ in range(60))
            def chunk(kind, data):
                return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
            return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 60, 60, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
        player = ops.request(base, "/api/players/search?q=lebron")[0]["id"]
        boundary = uuid.uuid4().hex
        parts = []
        for name, value in {"playerId": str(player), "year": "2016", "team": "Cleveland Cavaliers", "caption": "Restore test"}.items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
        for name, clear in [("photo", False), ("cutout", True)]:
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"; filename="{name}.png"\r\nContent-Type: image/png\r\n\r\n'.encode() + png(clear) + b"\r\n")
        body = b"".join(parts) + f"--{boundary}--\r\n".encode()
        req = urllib.request.Request(base + "/api/admin/whos-that/puzzles", data=body,
                                     headers={"X-Admin-Key": "test-only-admin-key", "Content-Type": "multipart/form-data; boundary=" + boundary})
        with urllib.request.urlopen(req, timeout=30) as response:
            assert response.status == 201
        bundle = ops.backup(root)
        report = ops.restore(bundle)
        assert report["photosVerified"] == 3, report
        assert len(report["completedGames"]) == 2, report
        # The source stack still serves its own DB and media after the disposable drill is removed.
        assert ops.request(base, "/health")
        print(json.dumps(report, indent=2))
    finally:
        ops.compose(root, "down", "-v", "--remove-orphans", check=False)
