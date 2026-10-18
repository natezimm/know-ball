#!/usr/bin/env python3
"""Preflight a local curated-photo batch and optionally create drafts; never schedule puzzles."""

import argparse
import json
import os
import re
import sys
import uuid
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

MAX_IMAGE_BYTES = 20 * 1024 * 1024
MIME = {".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".png": "image/png", ".webp": "image/webp"}
TEXT_LIMITS = {"player": 200, "team": 60, "caption": 160, "sourceUrl": 2048, "credit": 200, "license": 200}
API = "/api/admin/whos-that/puzzles"


class BatchError(Exception):
    pass


def web_url(value, *, local_http=False):
    if not isinstance(value, str) or any(c.isspace() for c in value):
        raise BatchError("Expected an absolute web URL without whitespace.")
    try:
        url = urlsplit(value)
        url.port  # validate a malformed port before making any request
    except ValueError as exc:
        raise BatchError("Invalid URL.") from exc
    allowed = url.scheme == "https" or (local_http and url.scheme == "http" and url.hostname in {"localhost", "127.0.0.1", "::1"})
    if not allowed or not url.hostname or url.username is not None or url.password is not None:
        raise BatchError("Use HTTPS without credentials, or HTTP on localhost for development.")
    return value


def load_manifest(path, only=None):
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(data, dict) or data.get("schemaVersion") != 1 or not isinstance(data.get("puzzles"), list) or not data["puzzles"]:
        raise BatchError("Expected schemaVersion 1 and a nonempty puzzles array.")
    keys, sources = set(), set()
    for item in data["puzzles"]:
        if not isinstance(item, dict):
            raise BatchError("Each puzzle must be an object.")
        key = item.get("key", "")
        if not isinstance(key, str) or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", key) or key in keys:
            raise BatchError("Each puzzle needs a unique lowercase key.")
        keys.add(key)
        for field, limit in TEXT_LIMITS.items():
            value = item.get(field)
            if not isinstance(value, str) or not value.strip() or len(value) > limit:
                raise BatchError(f"{key}: {field} must contain 1–{limit} characters.")
        web_url(item["sourceUrl"])
        web_url(item.get("licenseUrl"))
        if item["licenseUrl"] not in item["license"]:
            raise BatchError(f"{key}: include the license URL in the displayed license field.")
        if item["sourceUrl"] in sources:
            raise BatchError("Duplicate source URL in this batch.")
        sources.add(item["sourceUrl"])
        year = item.get("year")
        if type(year) is not int or not 1940 <= year <= 2100:
            raise BatchError(f"{key}: invalid photo year.")
        for field in ("photo", "cutout"):
            value = item.get(field)
            if not isinstance(value, str) or not value or Path(value).is_absolute() or ".." in Path(value).parts:
                raise BatchError(f"{key}: {field} must be a relative path within the assets directory.")
            extensions = {".png", ".webp"} if field == "cutout" else set(MIME)
            if Path(value).suffix.lower() not in extensions:
                raise BatchError(f"{key}: unsupported {field} extension.")
    if only and set(only) - keys:
        raise BatchError("An --only key was not found in the manifest.")
    return [p for p in data["puzzles"] if not only or p["key"] in only]


def asset_path(root, relative):
    root = Path(root).resolve()
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise BatchError("An asset path or symlink leaves the assets directory.")
    return path


def check_assets(items, root):
    report = []
    for item in items:
        issues = []
        review = item.get("iconicReview")
        if not isinstance(review, dict) or review.get("status") != "approved":
            issues.append("iconic photo review is not approved; select a famous photo or signature moment first")
        else:
            for field in ("moment", "reason"):
                if not isinstance(review.get(field), str) or not review[field].strip():
                    issues.append(f"iconic review needs {field}")
            try:
                web_url(review.get("evidenceUrl"))
            except BatchError:
                issues.append("iconic review needs a valid evidenceUrl for the selected photograph/moment")
        for field in ("photo", "cutout"):
            path = asset_path(root, item[field])
            if not path.is_file():
                issues.append(f"missing {field}: {item[field]}")
            elif not 0 < path.stat().st_size <= MAX_IMAGE_BYTES:
                issues.append(f"{field} must contain 1–{MAX_IMAGE_BYTES} bytes")
        report.append({"key": item["key"], "readyForDraft": not issues, "issues": issues})
    return report


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # Never forward an admin key to a redirect target, even on the same host.
        return None


class Client:
    def __init__(self, base_url, key):
        self.base_url = web_url(base_url.rstrip("/"), local_http=True)
        parsed = urlsplit(self.base_url)
        if parsed.query or parsed.fragment:
            raise BatchError("The site URL must not contain a query or fragment.")
        if not isinstance(key, str) or not key or any(not 33 <= ord(c) <= 126 for c in key):
            raise BatchError("Use the printable ASCII admin key without spaces or control characters.")
        self.key = key
        self.opener = build_opener(NoRedirects())

    def request(self, method, path, body=None, content_type=None, *, admin=False):
        headers = {"Accept": "application/json"}
        if admin:
            headers["X-Admin-Key"] = self.key
        if content_type:
            headers["Content-Type"] = content_type
        request = Request(self.base_url + path, data=body, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=60) as response:
                return json.load(response)
        except HTTPError as exc:
            # Do not echo a server response that could contain credentials or puzzle secrets.
            raise BatchError(f"{method} {path.split('?')[0]} returned HTTP {exc.code}; no automatic retry was attempted.") from exc
        except URLError as exc:
            raise BatchError("Could not reach the site; no automatic retry was attempted.") from exc


def multipart(item, player_id, root):
    boundary = "knowball-" + uuid.uuid4().hex
    parts = []
    values = {k: item[k] for k in ("year", "team", "caption", "sourceUrl", "credit", "license")}
    values["playerId"] = player_id
    for key, value in values.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode())
    for field in ("photo", "cutout"):
        path = asset_path(root, item[field])
        with path.open("rb") as stream:
            content = stream.read(MAX_IMAGE_BYTES + 1)
        if not 0 < len(content) <= MAX_IMAGE_BYTES:
            raise BatchError(f"{item['key']}: an image changed size after preflight.")
        extension = path.suffix.lower()
        # Use our own filename, not any user-controlled path in the multipart headers.
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{field}"; filename="{field}{extension}"\r\nContent-Type: {MIME[extension]}\r\n\r\n'.encode() + content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    return b"".join(parts), f"multipart/form-data; boundary={boundary}"


def upload(items, root, client):
    report = check_assets(items, root)
    if any(not row["readyForDraft"] for row in report):
        raise BatchError("Selected content is not ready for drafts; run check for image and iconic-review issues. No requests were sent.")
    existing = client.request("GET", API, admin=True)
    planned = []
    for item in items:
        matches = [p for p in existing if (p.get("attribution") or {}).get("sourceUrl") == item["sourceUrl"]]
        if matches:
            if len(matches) != 1 or (matches[0]["playerName"], matches[0]["year"], matches[0]["team"]) != (item["player"], item["year"], item["team"]):
                raise BatchError(f"{item['key']}: source is already used with different metadata; review it in /admin. No drafts were created.")
            planned.append((item, None, matches[0]))
            continue
        rows = client.request("GET", "/api/players/search?" + urlencode({"q": item["player"], "includeInactive": "true", "limit": 20}))
        exact = [p for p in rows if p["fullName"].casefold() == item["player"].casefold()]
        if len(exact) != 1:
            raise BatchError(f"{item['key']}: expected one exact player match; review the imported player data. No drafts were created.")
        planned.append((item, exact[0]["id"], None))
    result = []
    for item, player_id, old in planned:
        if old is not None:
            row = {"key": item["key"], "status": "already-exists", "puzzleId": old["id"], "state": old["state"]}
        else:
            body, content_type = multipart(item, player_id, root)
            created = client.request("POST", API, body, content_type, admin=True)
            row = {"key": item["key"], "status": "created", "puzzleId": created["id"], "state": created["state"]}
            if row["state"] != "draft":
                raise BatchError("The API did not return a draft; stop and review /admin before continuing.")
        result.append(row)
        print(json.dumps(row), flush=True)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["check", "upload"])
    parser.add_argument("--manifest", type=Path, default=Path(__file__).resolve().parents[1] / "content/whos-that/starter-five.json")
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--only", action="append", help="Select a candidate key; repeat to select several.")
    parser.add_argument("--base-url", help="Site origin; required only for upload.")
    args = parser.parse_args(argv)
    try:
        items = load_manifest(args.manifest, args.only)
        if args.action == "check":
            report = check_assets(items, args.assets)
            print(json.dumps({"metadataValid": True, "candidates": report}, indent=2))
            return 0 if all(row["readyForDraft"] for row in report) else 1
        if not args.base_url:
            raise BatchError("Upload needs --base-url.")
        key = os.environ.get("KNOWBALL_ADMIN_KEY", "")
        if not key:
            raise BatchError("Set KNOWBALL_ADMIN_KEY in the environment before upload.")
        upload(items, args.assets, Client(args.base_url, key))
        return 0
    except (BatchError, OSError, ValueError, KeyError, TypeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
