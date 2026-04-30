#!/usr/bin/env bash
# Build the DigiPay-for-WooCommerce plugin ZIP that merchants upload to
# WordPress admin → Plugins → Add New → Upload.
#
# Output: dist/digipay-for-woocommerce-<version>.zip
#
# The version is read from the plugin header (the "Version:" line in
# samples/woocommerce-plugin/digipay-for-woocommerce.php) so the ZIP filename
# always matches what merchants see in WP admin after install. The GitHub
# Actions release workflow uses --check-version to guard the tag against this.
#
# The archive root is `digipay-for-woocommerce/` (the plugin slug) — WP
# unpacks the ZIP into wp-content/plugins/<archive-root>/, so the slug
# matches what the plugin header declares.
#
# Excludes:
#   - tests/                  (developer-only smoke test + fixtures)
#   - .DS_Store, *.bak        (editor cruft)
#
# Usage:
#   scripts/build-woocommerce-zip.sh
#   scripts/build-woocommerce-zip.sh --check-version 0.1.0   # for CI
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PLUGIN_DIR="$REPO_ROOT/samples/woocommerce-plugin"
PLUGIN_FILE="$PLUGIN_DIR/digipay-for-woocommerce.php"
PLUGIN_SLUG="digipay-for-woocommerce"
DIST_DIR="$REPO_ROOT/dist"

if [ ! -f "$PLUGIN_FILE" ]; then
    echo "::error::plugin file not found: $PLUGIN_FILE" >&2
    exit 1
fi

# Pluck "Version: X.Y.Z" out of the plugin header. WordPress is strict about
# this format, so a single grep is fine.
VERSION="$(grep -E '^[[:space:]]*\*[[:space:]]*Version:' "$PLUGIN_FILE" \
    | head -n1 \
    | sed -E 's/^[[:space:]]*\*[[:space:]]*Version:[[:space:]]*//' \
    | tr -d '[:space:]')"

if [ -z "$VERSION" ]; then
    echo "::error::could not read Version from $PLUGIN_FILE" >&2
    exit 1
fi

# Optional CI guard — bail if the tag-derived version doesn't match the
# plugin header. Keeps us from cutting a vX.Y.Z release that ships an
# inconsistent plugin.
if [ "${1:-}" = "--check-version" ]; then
    EXPECTED="${2:-}"
    if [ -z "$EXPECTED" ]; then
        echo "::error::--check-version requires a version argument" >&2
        exit 1
    fi
    if [ "$EXPECTED" != "$VERSION" ]; then
        echo "::error::tag version $EXPECTED doesn't match plugin header version $VERSION" >&2
        exit 1
    fi
fi

mkdir -p "$DIST_DIR"

ZIP_NAME="${PLUGIN_SLUG}-${VERSION}.zip"
ZIP_PATH="$DIST_DIR/$ZIP_NAME"

# Stage in a temp dir under the slug-named folder so the archive root
# matches what WordPress expects when unpacking into wp-content/plugins/.
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

# rsync gives clean exclude semantics; fall back to a cp+find combo if it's
# not on the box (uncommon — both bash on macOS and ubuntu-latest have it).
if command -v rsync >/dev/null 2>&1; then
    rsync -a \
        --exclude 'tests' \
        --exclude '.DS_Store' \
        --exclude '*.bak' \
        "$PLUGIN_DIR/" "$STAGE/$PLUGIN_SLUG/"
else
    mkdir -p "$STAGE/$PLUGIN_SLUG"
    (cd "$PLUGIN_DIR" && find . \
        -path './tests' -prune -o \
        -name '.DS_Store' -prune -o \
        -name '*.bak' -prune -o \
        -type f -print | while read -r f; do
            mkdir -p "$STAGE/$PLUGIN_SLUG/$(dirname "$f")"
            cp "$PLUGIN_DIR/$f" "$STAGE/$PLUGIN_SLUG/$f"
        done)
fi

rm -f "$ZIP_PATH"

# Prefer `zip` on CI/Linux/Mac. Fall back to Python on Windows dev boxes
# without `zip` in PATH — every modern Python ships zipfile in the stdlib,
# and it's far more common on Windows than the GNU zip toolchain.
if command -v zip >/dev/null 2>&1; then
    (cd "$STAGE" && zip -rq "$ZIP_PATH" "$PLUGIN_SLUG")
elif command -v python >/dev/null 2>&1 || command -v python3 >/dev/null 2>&1; then
    PY="$(command -v python3 || command -v python)"
    "$PY" - "$STAGE" "$PLUGIN_SLUG" "$ZIP_PATH" <<'PY_EOF'
import os, sys, zipfile
stage, slug, out = sys.argv[1], sys.argv[2], sys.argv[3]
root = os.path.join(stage, slug)
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zf:
    for dirpath, _, filenames in os.walk(root):
        for f in filenames:
            full = os.path.join(dirpath, f)
            # Archive paths use forward slashes regardless of host OS.
            arc = os.path.relpath(full, stage).replace(os.sep, '/')
            zf.write(full, arc)
PY_EOF
else
    echo "::error::neither 'zip' nor 'python' is available — install one to build the plugin ZIP" >&2
    exit 1
fi

echo "$ZIP_PATH"
