#!/usr/bin/env python3
"""Post-process a published Blazor WASM index.html so its CSP enumerates
SHA-256 hashes of every inline <script> block, then drops 'unsafe-inline'
from script-src.

Why we need this:
    Blazor's publish pipeline injects an inline <script type="importmap">
    that maps logical module names to fingerprinted filenames. The map's
    contents move on every build, so we can't hardcode its hash in the
    source. This script bridges the gap — it runs *after* dotnet publish,
    inspects the actual published HTML, and patches CSP to match.

What it does:
    1. Parse every <script>…</script> block whose tag has no `src` attr.
       This catches both `type="application/ld+json"` (Schema.org) and
       `type="importmap"` (Blazor). Element-level attributes (style="…")
       are governed by style-src-attr and are out of scope here.
    2. SHA-256 each block's exact contents (the bytes between > and <).
    3. Rewrite the <meta http-equiv="Content-Security-Policy"> tag's
       script-src directive: remove 'unsafe-inline', add a 'sha256-…'
       entry per inline block found.

Idempotent — running twice produces the same output. The script will not
add a hash that's already present, and will not strip a hash for a block
that no longer exists (so a manual edit during local dev still works).

Usage:
    python tools/inject-csp-hashes.py --index-html publish/wwwroot/index.html
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import re
import sys
from pathlib import Path

# Match every inline <script> (no src=…) and capture its body. DOTALL so the
# body can span newlines; non-greedy so we don't swallow neighbours.
INLINE_SCRIPT = re.compile(
    r"<script(?P<attrs>(?:\s[^>]*?)?)>(?P<body>.*?)</script>",
    re.DOTALL,
)

# HTML comments — we strip these before scanning for <script> blocks because
# explanatory prose in our own comments mentions <script> tags by name (e.g.
# "the inline <script type=\"importmap\"> that Blazor injects"), and a naive
# regex matches that text as a real tag.
HTML_COMMENT = re.compile(r"<!--.*?-->", re.DOTALL)

CSP_META = re.compile(
    r'(<meta\s+http-equiv="Content-Security-Policy"\s+content=")([^"]+)(")',
    re.IGNORECASE,
)


def is_external(attrs: str) -> bool:
    """A <script> with a src= attribute loads its body from elsewhere; CSP
    governs that via the script-src URL list, not via a hash."""
    return re.search(r"\bsrc\s*=", attrs, re.IGNORECASE) is not None


def sha256_csp(content: str) -> str:
    """CSP hash format: `'sha256-<base64>'` of the raw UTF-8 bytes of the
    block (whitespace and all). Matches what Chromium / Firefox compute."""
    digest = hashlib.sha256(content.encode("utf-8")).digest()
    return "sha256-" + base64.b64encode(digest).decode()


def collect_inline_hashes(html: str) -> list[str]:
    """Walk every inline <script> block and return its CSP hash. Order is
    document order; deduped (a single hash covers all blocks with the same
    content, which is unusual but possible)."""
    # Strip HTML comments first so prose mentioning "<script>" in a comment
    # doesn't get matched as a real tag.
    scrubbed = HTML_COMMENT.sub("", html)
    hashes: list[str] = []
    seen: set[str] = set()
    for m in INLINE_SCRIPT.finditer(scrubbed):
        if is_external(m.group("attrs")):
            continue
        h = sha256_csp(m.group("body"))
        if h not in seen:
            hashes.append(h)
            seen.add(h)
    return hashes


def patch_script_src(directive: str, hashes: list[str]) -> str:
    """Rewrite the script-src directive: drop 'unsafe-inline' and any
    pre-existing sha256 tokens, then re-add the freshly-computed ones.
    The injector is the authoritative source of inline-script hashes
    against the *published* bytes — source-CSP hashes (which are
    correct for the dev-mode file) would otherwise stick around as
    dead entries when the published copy has different bytes (e.g.
    line-ending normalisation)."""
    tokens = directive.split()
    if tokens and tokens[0].lower() == "script-src":
        head, tail = tokens[0], tokens[1:]
    else:
        head, tail = "script-src", tokens
    tail = [
        t for t in tail
        if t != "'unsafe-inline'" and not t.startswith("'sha256-")
    ]
    for h in hashes:
        tail.append(f"'{h}'")
    return " ".join([head, *tail])


def patch_csp(content: str, hashes: list[str]) -> str:
    """Apply patch_script_src to the script-src directive inside the CSP
    string. CSP directives are `;`-separated; we only touch script-src."""
    parts = [p.strip() for p in content.split(";")]
    out: list[str] = []
    for part in parts:
        if not part:
            continue
        if part.lower().startswith("script-src "):
            out.append(patch_script_src(part, hashes))
        else:
            out.append(part)
    return ";\n                   ".join(out) + ";"


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--index-html", required=True, type=Path,
                   help="Path to the published index.html to patch in place.")
    p.add_argument("--dry-run", action="store_true",
                   help="Print the patched CSP to stdout without modifying the file.")
    args = p.parse_args(argv)

    html = args.index_html.read_text(encoding="utf-8")

    hashes = collect_inline_hashes(html)
    if not hashes:
        print("warning: no inline <script> blocks found", file=sys.stderr)

    csp_match = CSP_META.search(html)
    if not csp_match:
        print(f"error: no <meta http-equiv=\"Content-Security-Policy\"> in {args.index_html}", file=sys.stderr)
        return 2

    new_csp_value = patch_csp(csp_match.group(2), hashes)

    if args.dry_run:
        print(new_csp_value)
        return 0

    new_html = (
        html[:csp_match.start(2)]
        + new_csp_value
        + html[csp_match.end(2):]
    )
    args.index_html.write_text(new_html, encoding="utf-8")
    print(f"patched {args.index_html} with {len(hashes)} inline-script hash(es): "
          + ", ".join(h[:18] + "…" for h in hashes))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
