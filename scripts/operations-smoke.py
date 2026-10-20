#!/usr/bin/env python3
"""Exercise real backup/restore against offline fixtures using the already-built know-ball image."""
import importlib.util
import json
from pathlib import Path
import tempfile
import time
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
                "ConnectionStrings__Trivia": "Host=db;Database=know_ball;Username=knowball;Password=test-only"}, "ports": ["127.0.0.1::8080"]},
        },
    }
    # Compose accepts JSON, which avoids a YAML dependency in this stdlib smoke test.
    (root / "docker-compose.yml").write_text(json.dumps(compose))
    try:
        ops.compose(root, "up", "-d", "db")
        for attempt in range(60):
            if ops.compose(root, "exec", "-T", "db", "pg_isready", "-h", "127.0.0.1", "-U", "knowball", "-d", "know_ball", check=False).returncode == 0:
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
        bundle = ops.backup(root)
        report = ops.restore(bundle)
        assert report["photosVerified"] == 0, report
        assert len(report["completedGames"]) == 2, report
        # The source stack still serves its own database after the disposable drill is removed.
        assert ops.request(base, "/health")
        print(json.dumps(report, indent=2))
    finally:
        ops.compose(root, "down", "-v", "--remove-orphans", check=False)
