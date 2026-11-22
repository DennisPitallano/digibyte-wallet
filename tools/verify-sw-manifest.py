#!/usr/bin/env python3
"""Checks a published wallet's service-worker asset list against the files beside it.

The service worker (service-worker.published.js) fetches every file in
service-worker-assets.js with the listed fingerprint as its integrity value, and refuses to
install if any one does not match. A single stale fingerprint therefore stops every update:
devices keep the stylesheets and scripts they cached earlier while new page code arrives from
the network. Releases 0.7.0 and 0.8.0 shipped in that state, because the CSP step rewrote
index.html after the list had been written.

Usage:
    python tools/verify-sw-manifest.py publish/wwwroot

Exits 0 when every listed file exists and matches, 1 otherwise.
"""
import base64
import hashlib
import json
import sys
from pathlib import Path


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "publish/wwwroot")
    manifest_path = root / "service-worker-assets.js"
    if not manifest_path.exists():
        print(f"verify-sw-manifest: {manifest_path} not found")
        return 1

    text = manifest_path.read_text(encoding="utf-8")
    manifest = json.loads(text[text.index("{"):text.rindex("}") + 1])
    assets = manifest["assets"]

    missing, wrong = [], []
    for asset in assets:
        path = root / asset["url"]
        if not path.exists():
            missing.append(asset["url"])
            continue
        actual = "sha256-" + base64.b64encode(hashlib.sha256(path.read_bytes()).digest()).decode()
        if actual != asset["hash"]:
            wrong.append(asset["url"])

    for url in missing:
        print(f"verify-sw-manifest: listed but not published: {url}")
    for url in wrong:
        print(f"verify-sw-manifest: fingerprint does not match the published file: {url}")

    if missing or wrong:
        print(f"verify-sw-manifest: FAILED ({len(missing)} missing, {len(wrong)} mismatched of {len(assets)}). "
              "The service worker would refuse to install.")
        return 1

    print(f"verify-sw-manifest: OK, {len(assets)} files match (version {manifest.get('version')}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
