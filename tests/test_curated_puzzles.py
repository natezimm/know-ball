import contextlib
import importlib.util
import io
import json
import tempfile
import threading
import unittest
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("curated_puzzles", ROOT / "scripts/curated-puzzles.py")
batch = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(batch)


class FakeClient:
    def __init__(self, items, existing=None, ambiguous=False, fail_post=None):
        self.items = items
        self.existing = existing or []
        self.ambiguous = ambiguous
        self.fail_post = fail_post
        self.calls = []

    def request(self, method, path, body=None, content_type=None, *, admin=False):
        self.calls.append((method, path, body, content_type, admin))
        if method == "GET" and path == batch.API:
            return self.existing
        if method == "GET":
            from urllib.parse import parse_qs, urlsplit
            name = parse_qs(urlsplit(path).query)["q"][0]
            row = {"id": 7, "fullName": name}
            return [row, row] if self.ambiguous else [row]
        if self.fail_post is not None and len([x for x in self.calls if x[0] == "POST"]) == self.fail_post:
            raise batch.BatchError("Simulated request failure")
        return {"id": 42, "state": "draft"}


class CuratedBatchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.items = batch.load_manifest(ROOT / "content/whos-that/starter-five.json")
        # These are separately reviewed upload fixtures, not approval of the retired batch.
        for item in self.items:
            item["iconicReview"] = {"status": "approved", "moment": "Reviewed test moment",
                                   "reason": "Test fixture only", "evidenceUrl": item["sourceUrl"]}

    def assets(self, items=None):
        for item in items or self.items:
            for field in ("photo", "cutout"):
                path = self.root / item[field]
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"local test bytes; image decoding is checked by the API")

    def upload(self, client, items=None):
        with contextlib.redirect_stdout(io.StringIO()):
            return batch.upload(items or self.items, self.root, client)

    def test_starter_manifest_has_five_distinct_sources_and_photo_dates(self):
        self.assertEqual(5, len(self.items))
        self.assertEqual(5, len({p["sourceUrl"] for p in self.items}))
        for item in self.items:
            self.assertEqual(item["year"], int(item["photoDate"][:4]))
            self.assertIn(item["licenseUrl"], item["license"])
            self.assertEqual("rejected-non-iconic", item["reviewStatus"])

    def test_check_reports_all_missing_assets_without_network_or_key(self):
        with patch.dict("os.environ", {}, clear=True), patch.object(batch, "Client", side_effect=AssertionError("No network for check")), contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(1, batch.main(["check", "--assets", str(self.root)]))
        rows = json.loads(output.getvalue())["candidates"]
        self.assertEqual(5, len(rows))
        self.assertTrue(all(len(p["issues"]) == 3 for p in rows))

    def test_retired_batch_stays_blocked_even_with_all_media_present(self):
        retired = batch.load_manifest(ROOT / "content/whos-that/starter-five.json")
        self.assets(retired)
        client = FakeClient(retired)
        self.assertTrue(all(not row["readyForDraft"] for row in batch.check_assets(retired, self.root)))
        with self.assertRaises(batch.BatchError):
            self.upload(client, retired)
        self.assertEqual([], client.calls)

    def test_iconic_approval_requires_evidence_and_named_moment_before_network(self):
        self.assets()
        for review in (None, {}, {"status": "pending"}, {"status": "approved"},
                       {"status": "approved", "moment": "Moment", "reason": "Famous", "evidenceUrl": "javascript:bad"}):
            items = [dict(self.items[0], iconicReview=review)]
            client = FakeClient(items)
            with self.subTest(review=review), self.assertRaises(batch.BatchError):
                self.upload(client, items)
            self.assertEqual([], client.calls)

    def test_metadata_rejects_unsafe_sources_traversal_and_duplicate_keys(self):
        for change in ({"sourceUrl": "javascript:alert(1)"}, {"cutout": "../outside.png"}, {"year": True}, {"team": "x" * 61}, {"license": "missing URL"}):
            data = {"schemaVersion": 1, "puzzles": [dict(self.items[0], **change)]}
            path = self.root / "bad.json"
            path.write_text(json.dumps(data))
            with self.subTest(change=change), self.assertRaises(batch.BatchError):
                batch.load_manifest(path)
        data = {"schemaVersion": 1, "puzzles": [self.items[0], self.items[0]]}
        path.write_text(json.dumps(data))
        with self.assertRaises(batch.BatchError):
            batch.load_manifest(path)

    def test_selection_rejects_unknown_keys(self):
        selected = batch.load_manifest(ROOT / "content/whos-that/starter-five.json", ["curry-jumper-2011"])
        self.assertEqual(["Stephen Curry"], [p["player"] for p in selected])
        with self.assertRaises(batch.BatchError):
            batch.load_manifest(ROOT / "content/whos-that/starter-five.json", ["typo"])

    def test_missing_or_oversized_assets_stop_before_any_request(self):
        client = FakeClient(self.items)
        with self.assertRaises(batch.BatchError):
            self.upload(client)
        self.assertEqual([], client.calls)
        self.assets()
        with (self.root / self.items[0]["photo"]).open("wb") as stream:
            stream.truncate(batch.MAX_IMAGE_BYTES + 1)
        with self.assertRaises(batch.BatchError):
            self.upload(client)
        self.assertEqual([], client.calls)

    def test_asset_symlink_cannot_leave_the_assets_directory(self):
        outside = self.root.parent / (self.root.name + "-outside.png")
        outside.write_bytes(b"not an allowed asset")
        self.addCleanup(outside.unlink)
        (self.root / "escape.png").symlink_to(outside)
        with self.assertRaises(batch.BatchError):
            batch.asset_path(self.root, "escape.png")

    def test_upload_sends_metadata_and_pairs_only_to_draft_endpoint(self):
        self.assets()
        client = FakeClient(self.items)
        result = self.upload(client)
        posts = [c for c in client.calls if c[0] == "POST"]
        self.assertEqual(5, len(posts))
        self.assertTrue(all(c[1] == batch.API and c[4] for c in posts))
        self.assertTrue(all(row["state"] == "draft" for row in result))
        for (_, _, body, content_type, _), item in zip(posts, self.items):
            self.assertIn(item["credit"].encode(), body)
            self.assertIn(item["license"].encode(), body)
            self.assertIn(b'name="cutout"; filename="cutout.png"', body)
            self.assertIn("multipart/form-data; boundary=", content_type)
        self.assertTrue(all(not c[4] for c in client.calls if "/players/search" in c[1]))

    def test_rerun_skips_existing_puzzles_without_editing_or_rescheduling(self):
        self.assets()
        item = self.items[0]
        old = {"id": 9, "state": "published", "playerName": item["player"], "year": item["year"], "team": item["team"], "attribution": {"sourceUrl": item["sourceUrl"]}}
        client = FakeClient(self.items, [old])
        result = self.upload(client)
        self.assertEqual("already-exists", result[0]["status"])
        self.assertEqual(4, len([c for c in client.calls if c[0] == "POST"]))
        self.assertFalse(any(c[0] in {"PUT", "DELETE"} or "schedule" in c[1] for c in client.calls))

    def test_ambiguous_player_or_conflicting_source_stops_before_creating_any_drafts(self):
        self.assets()
        old = {"id": 1, "state": "draft", "playerName": "Someone else", "year": 2000, "team": "Other", "attribution": {"sourceUrl": self.items[-1]["sourceUrl"]}}
        for client in (FakeClient(self.items, ambiguous=True), FakeClient(self.items, [old])):
            with self.assertRaises(batch.BatchError):
                self.upload(client)
            self.assertFalse(any(c[0] == "POST" for c in client.calls))

    def test_partial_failure_does_not_retry_a_post_and_reports_prior_success(self):
        self.assets()
        client = FakeClient(self.items, fail_post=2)
        with contextlib.redirect_stdout(io.StringIO()) as output, self.assertRaises(batch.BatchError):
            batch.upload(self.items, self.root, client)
        self.assertEqual(2, len([c for c in client.calls if c[0] == "POST"]))
        self.assertEqual("lebron-finals-2016", json.loads(output.getvalue())["key"])

    def test_target_urls_keys_and_redirects_cannot_leak_the_admin_key(self):
        for url in ("http://example.com", "https://user:pass@example.com", "https://example.com/?query=yes", "https://example.com/#fragment"):
            with self.subTest(url=url), self.assertRaises(batch.BatchError):
                batch.Client(url, "secret")
        client = batch.Client("http://127.0.0.1:5080", "secret")
        self.assertIsNone(batch.NoRedirects().redirect_request(None, None, 302, "", {}, "https://elsewhere.example"))
        with self.assertRaises(batch.BatchError) as error:
            batch.Client("https://example.com", "secret\nheader")
        self.assertNotIn("secret", str(error.exception))
        client.opener.open = lambda *args, **kwargs: (_ for _ in ()).throw(HTTPError("https://example.com", 302, "redirect secret", {}, None))
        with self.assertRaises(batch.BatchError) as error:
            client.request("GET", batch.API, admin=True)
        self.assertNotIn("secret", str(error.exception))

    def test_real_http_multipart_and_redirect_handling(self):
        events = []

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                events.append(("GET", self.path, self.headers.get("X-Admin-Key")))
                if self.path == "/redirect":
                    self.send_response(302)
                    self.send_header("Location", "/must-not-receive-the-key")
                    self.end_headers()
                    return
                body = [] if self.path == batch.API else [{"id": 7, "fullName": "LeBron James"}]
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(json.dumps(body).encode())

            def do_POST(self):
                body = self.rfile.read(int(self.headers["Content-Length"]))
                message = BytesParser(policy=policy.default).parsebytes(
                    f'Content-Type: {self.headers["Content-Type"]}\r\nMIME-Version: 1.0\r\n\r\n'.encode() + body)
                fields = {part.get_param("name", header="content-disposition"): part.get_payload(decode=True) for part in message.iter_parts()}
                events.append(("POST", self.path, self.headers.get("X-Admin-Key"), fields))
                self.send_response(201)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(b'{"id": 91, "state": "draft"}')

        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            self.assets([self.items[0]])
            client = batch.Client(f"http://127.0.0.1:{server.server_port}", "test-key")
            self.upload(client, [self.items[0]])
            self.assertEqual("test-key", events[0][2])
            self.assertIsNone(events[1][2])
            self.assertEqual(batch.API, events[2][1])
            self.assertEqual("test-key", events[2][2])
            fields = events[2][3]
            self.assertEqual(b"7", fields["playerId"])
            self.assertEqual(self.items[0]["license"].encode(), fields["license"])
            self.assertEqual((self.root / self.items[0]["cutout"]).read_bytes(), fields["cutout"])
            with self.assertRaises(batch.BatchError):
                client.request("GET", "/redirect", admin=True)
            self.assertEqual("/redirect", events[-1][1])
            self.assertFalse(any("must-not-receive" in row[1] for row in events))
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
