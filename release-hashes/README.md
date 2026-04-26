# Wallet release-hash manifests

Per-release SHA-256 manifests of every file in the published wallet bundle,
**not committed to this directory** — they're attached to the matching
[GitHub Release][releases] as the `wallet-vX.Y.Z.json` asset, alongside the
zipped bundle (`wallet-vX.Y.Z.zip`) used to produce them.

## How a manifest is produced

1. A maintainer pushes a tag matching `wallet-v*` (e.g. `wallet-v0.1.0`).
2. The
   [`wallet-release-manifest.yml`](../.github/workflows/wallet-release-manifest.yml)
   GitHub Action publishes the wallet in `Release` configuration with the
   .NET SDK pinned by [`global.json`](../global.json).
3. [`tools/generate-release-manifest.py`](../tools/generate-release-manifest.py)
   walks the published `wwwroot/` and writes a sorted JSON manifest with
   the tag, commit, and per-file `{path, size, sha256}`.
4. The manifest + a zip of the bundle are uploaded as Release assets.

## How a reviewer uses it

To verify what `dgbwallet.app` is actually serving against the open source:

```bash
# Download the manifest for the release you want to check
gh release download wallet-v0.1.0 --pattern '*.json'

# Hash whatever the production URL is currently shipping
curl -s https://dgbwallet.app/_framework/dotnet.wasm | sha256sum

# Compare against the entry in the manifest
jq '.assets[] | select(.path == "_framework/dotnet.wasm")' wallet-v0.1.0.json
```

To verify the manifest was honestly produced from the tagged source:

```bash
# Clone, check out the tag, build with the pinned SDK, run the script
git clone https://github.com/DennisPitallano/digibyte-wallet
cd digibyte-wallet && git checkout wallet-v0.1.0
dotnet publish src/DigiByte.Web/DigiByte.Web.csproj -c Release -o publish
python tools/generate-release-manifest.py \
    --asset-root publish/wwwroot \
    --tag wallet-v0.1.0 \
    --commit "$(git rev-parse HEAD)" \
    --output local.json

# Compare your locally-generated manifest with the one from the release
diff <(jq -S . wallet-v0.1.0.json) <(jq -S . local.json)
```

Differences in the `generated_at` timestamp are expected — every other
field should match exactly. If the `assets[]` differ, that's a
reproducibility-build issue worth investigating; please open a security
advisory ([SECURITY.md](../SECURITY.md)).

[releases]: https://github.com/DennisPitallano/digibyte-wallet/releases
