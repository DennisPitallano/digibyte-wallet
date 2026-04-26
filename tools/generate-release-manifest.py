#!/usr/bin/env python3
"""Generate a SHA-256 manifest of a published wallet bundle.

Output shape (matches docs/walletscrutiny-self-eval.md §3.3):

    {
        "tag": "wallet-v1.2.3",
        "commit": "abc123…",
        "generated_at": "2026-04-26T10:42:31Z",
        "tool": "tools/generate-release-manifest.py",
        "asset_root": "wwwroot",
        "assets": [
            { "path": "_framework/dotnet.wasm", "size": 1234567, "sha256": "…" },
            …
        ]
    }

Why a separate script (not inlined in the workflow):
    - lets a contributor reproduce the same manifest locally with the same
      hashing rules (which `find … | sha256sum` would not),
    - keeps the GitHub Action thin so the same logic is testable offline.

Usage:
    python tools/generate-release-manifest.py \\
        --asset-root publish/wwwroot \\
        --tag wallet-v0.1.0 \\
        --commit "$(git rev-parse HEAD)" \\
        --output release-hashes/wallet-v0.1.0.json
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import sys
from pathlib import Path


def sha256_of(path: Path) -> str:
    """Stream-hash a file in 64KB chunks so this works on the WASM bundles
    (which can be tens of MB) without holding the whole thing in memory."""
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def walk_assets(root: Path) -> list[dict]:
    """Walk every regular file under `root`, hash it, and return the
    manifest entries sorted by path. Sorted so the output is byte-stable
    across runs — different filesystems iterate directories in different
    orders, and a non-deterministic manifest defeats the whole point."""
    entries: list[dict] = []
    root = root.resolve()
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            full = Path(dirpath) / name
            if not full.is_file():
                continue
            rel = full.relative_to(root).as_posix()
            entries.append({
                "path": rel,
                "size": full.stat().st_size,
                "sha256": sha256_of(full),
            })
    entries.sort(key=lambda e: e["path"])
    return entries


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--asset-root", required=True, type=Path,
                   help="Directory whose files will be hashed (e.g. publish/wwwroot).")
    p.add_argument("--tag", required=True,
                   help="Release tag, e.g. wallet-v0.1.0.")
    p.add_argument("--commit", required=True,
                   help="Git commit hash being released.")
    p.add_argument("--output", required=True, type=Path,
                   help="Path to write the JSON manifest.")
    args = p.parse_args(argv)

    if not args.asset_root.is_dir():
        print(f"error: --asset-root {args.asset_root} is not a directory", file=sys.stderr)
        return 2

    entries = walk_assets(args.asset_root)
    if not entries:
        print(f"error: no files found under {args.asset_root}", file=sys.stderr)
        return 3

    manifest = {
        "tag": args.tag,
        "commit": args.commit,
        # ISO-8601 UTC, second precision — enough to identify the build,
        # not so granular it changes when the same source rebuilds.
        "generated_at": dt.datetime.now(dt.UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "tool": "tools/generate-release-manifest.py",
        "asset_root": args.asset_root.name,
        "assets": entries,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    # Compact-ish JSON: 2-space indent so it's diffable, ensure_ascii=False
    # so the file stays small (no escaping for any non-ASCII paths) and a
    # trailing newline so POSIX tools don't complain.
    with args.output.open("w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False, sort_keys=False)
        f.write("\n")

    print(f"wrote {args.output} — {len(entries)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
