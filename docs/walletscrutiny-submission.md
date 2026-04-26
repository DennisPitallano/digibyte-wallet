# WalletScrutiny submission — DigiByte Wallet

> Draft of the issue body to file at
> <https://github.com/walletscrutiny/walletScrutinyCom/issues/new>.
> Copy from the **`---`** marker below into the issue. Update the version
> + commit fields once a `wallet-v*` tag has been cut.

---

**Wallet name:** DigiByte Wallet
**Type:** Web wallet (Blazor WebAssembly PWA)
**URL:** https://dgbwallet.app
**Source:** https://github.com/DennisPitallano/digibyte-wallet
**License:** MIT (see repo `LICENSE`)
**Version under review:** _to be filled in once `wallet-v*` is cut_
**Commit:** _commit hash matching the tag above_
**Maintainer:** Dennis Pitallano (https://dennispitallano.github.io)

## Why this is a self-custodial wallet

- Mnemonic generation, encryption (AES-256-GCM with PBKDF2-SHA256, 100 000
  iterations), storage (browser IndexedDB), and signing (NBitcoin's
  secp256k1) all happen in the user's browser.
- The Blazor app runs on **WebAssembly**, not Blazor Server — there is no
  SignalR circuit that could carry key material to the backend.
- The server side of `dgbwallet.app` is a static asset host: it serves the
  WASM/JS/CSS bundle, then steps out of the way. There is no API endpoint
  on the wallet origin that accepts seed phrases or private keys.
- Plain-English summary for users: [SELF_CUSTODY.md](../SELF_CUSTODY.md)
- Code-level walk-through of every key path with file/line references:
  [docs/walletscrutiny-self-eval.md §1](walletscrutiny-self-eval.md#1-custody-criteria)

## Asset integrity

- Page CSP drops `'unsafe-inline'` from both `script-src` and `style-src`.
  All inline scripts have been moved out to
  [`wwwroot/js/analytics.js`](../src/DigiByte.Web/wwwroot/js/analytics.js)
  and [`wwwroot/js/pwa-bootstrap.js`](../src/DigiByte.Web/wwwroot/js/pwa-bootstrap.js);
  inline styles to [`wwwroot/css/splash.css`](../src/DigiByte.Web/wwwroot/css/splash.css).
- The single static `<script type="application/ld+json">` block (Schema.org
  metadata, no executable code) is covered by a `'sha256-…'` hash in CSP.
- All wallet-specific JS is served from the wallet origin.
- One **accepted third-party script:** `gtag.js` (Google Analytics) is
  loaded async from `googletagmanager.com` without an SRI hash because
  Google rotates that file unannounced. The bootstrap is isolated in
  `js/analytics.js`, no key material ever flows through it, and the
  script loads outside the signing path. See
  [self-eval §2.2](walletscrutiny-self-eval.md#22-third-party-scripts---gap-to-fix-x)
  for the rationale and the mitigation paths if you'd prefer it closed.

## Reproducible build

- Repo-wide [`Directory.Build.props`](../Directory.Build.props) sets
  `<Deterministic>`, `<ContinuousIntegrationBuild>`,
  `<EmbedUntrackedSources>`, and `<PublishRepositoryUrl>` for every .NET
  project in the tree.
- [`global.json`](../global.json) pins the .NET SDK to `10.0.201` with
  `rollForward: latestPatch`.
- [`src/DigiByte.Web/package-lock.json`](../src/DigiByte.Web/package-lock.json)
  is committed; Tailwind and Inter are pinned to exact versions in
  `package.json`.
- Every `wallet-v*` tag triggers
  [`.github/workflows/wallet-release-manifest.yml`](../.github/workflows/wallet-release-manifest.yml),
  which publishes the bundle, hashes it, and attaches the resulting
  `wallet-vX.Y.Z.json` manifest + `wallet-vX.Y.Z.zip` of the published
  bundle to the GitHub Release.
- Reviewer recipe (clone tag, build with pinned SDK, diff manifest against
  the published one): [`release-hashes/README.md`](../release-hashes/README.md).

## PWA / offline

- [`manifest.webmanifest`](../src/DigiByte.Web/wwwroot/manifest.webmanifest)
  is present; once installed the wallet works offline.
- [Service worker](../src/DigiByte.Web/wwwroot/service-worker.published.js)
  caches the app shell. Signing and key derivation are entirely
  WASM-resident and don't touch the network.

## How to build locally

```bash
git clone https://github.com/DennisPitallano/digibyte-wallet
cd digibyte-wallet && git checkout <tag>
# .NET SDK is pinned via global.json; install matching SDK from dotnet.microsoft.com.
dotnet publish src/DigiByte.Web/DigiByte.Web.csproj -c Release -o publish
# Bundle to verify is now in publish/wwwroot/. Optionally:
python tools/generate-release-manifest.py \
    --asset-root publish/wwwroot \
    --tag <tag> \
    --commit "$(git rev-parse HEAD)" \
    --output local.json
```

Diff `local.json` against the `wallet-vX.Y.Z.json` attached to the GitHub
Release; only the `generated_at` timestamp should differ.

## Disclosure / contact

Security policy: [SECURITY.md](../SECURITY.md) — GitHub Security Advisories
is the primary private-disclosure channel.

Happy to answer follow-up questions in this thread or via the security
contact above.
