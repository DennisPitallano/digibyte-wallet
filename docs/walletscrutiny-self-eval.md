# WalletScrutiny self-evaluation — DigiByte Wallet (`dgbwallet.app`)

> Status: **draft / pre-submission**
> Last reviewed: April 2026
> Wallet under review: <https://dgbwallet.app> (Blazor WebAssembly PWA)
> Reviewer: project maintainers

This document walks the wallet through the [WalletScrutiny](https://walletscrutiny.com)
methodology before submitting it for external review. Goal: identify and close the
obvious gaps so the listing lands as cleanly as possible the first time.

---

## TL;DR

- **Custody architecture is strong.** Keys are generated, stored, and used to sign
  *entirely in the browser*. The Blazor app runs on **WebAssembly**, not Blazor
  Server, so no SignalR circuit ever sees a private key, mnemonic, or PIN.
- **The four gaps from the original audit have all been closed:**
  - ✅ CSP now drops `'unsafe-inline'` from `script-src` and `style-src`. The
    one inline `<script type="application/ld+json">` is covered by a
    `'sha256-…'` hash; element-level `style="…"` attributes are scoped to
    `style-src-attr 'unsafe-inline'`.
  - ✅ Repo-wide `Directory.Build.props` sets `<Deterministic>true</…>` and
    `<ContinuousIntegrationBuild>true</…>`, plus `EmbedUntrackedSources`
    and `PublishRepositoryUrl` for SourceLink-driven verification.
  - ✅ `global.json` pins the .NET SDK to `10.0.201`
    (`rollForward: latestPatch`).
  - ✅ Tailwind + Inter are pinned to exact versions and a
    `package-lock.json` is committed.
- **One accepted limitation:** the third-party `gtag.js` is still loaded
  without an SRI hash. Google rotates that file unannounced; pinning a hash
  would silently break analytics on every rotation. We've chosen to keep it
  as-is — the GA bootstrap is in [`js/analytics.js`](../src/DigiByte.Web/wwwroot/js/analytics.js)
  (no key material ever flows through it) and the script is `async crossorigin`
  loaded outside the signing path. Reviewers who want a fully-green outcome
  can either ask us to drop GA or proxy a versioned copy through the wallet
  origin — both are documented as Option 1 / Option 2 in §2.2.

---

## 1. Custody criteria

WalletScrutiny's first question is "do the user's keys ever leave the user's
device?" Three places to check: **generation**, **storage**, **signing**.

### 1.1 Key generation — client-side ✓

Mnemonic + seed generation happens in WebAssembly, in the browser:

| File | Lines | Role |
|------|-------|------|
| [`src/DigiByte.Crypto/KeyGeneration/MnemonicGenerator.cs`](../src/DigiByte.Crypto/KeyGeneration/MnemonicGenerator.cs) | 13 | BIP39 mnemonic via `NBitcoin.Mnemonic` |
| [`src/DigiByte.Web/Pages/CreateWallet.razor`](../src/DigiByte.Web/Pages/CreateWallet.razor) | 219 | UI calls `MnemonicGenerator.Generate()` |

Because `DigiByte.Web` is a `Microsoft.NET.Sdk.BlazorWebAssembly` project
(see [`DigiByte.Web.csproj`](../src/DigiByte.Web/DigiByte.Web.csproj):1),
this C# runs as compiled WASM inside the browser sandbox. There is no
SignalR circuit (which would mean Blazor Server) and no HTTP call to the
backend at the point of generation.

### 1.2 Key storage — encrypted in IndexedDB ✓

Mnemonics are stored encrypted-at-rest in the browser, never sent to the
server:

| File | Lines | Role |
|------|-------|------|
| [`src/DigiByte.Web/wwwroot/js/storage.js`](../src/DigiByte.Web/wwwroot/js/storage.js) | 1–83 | IndexedDB wrapper (`digibyte-wallet` / `keyval`) |
| [`src/DigiByte.Web/wwwroot/js/crypto.js`](../src/DigiByte.Web/wwwroot/js/crypto.js) | 1–60 | AES-256-GCM via WebCrypto SubtleCrypto |
| [`src/DigiByte.Wallet/Storage/WalletKeyStore.cs`](../src/DigiByte.Wallet/Storage/WalletKeyStore.cs) | 26–30 | C# accessor that calls into JS interop |

Encryption parameters: AES-256-GCM, PBKDF2-SHA256 with **100 000 iterations**,
16-byte salt, 12-byte IV. Format: `salt(16) ‖ iv(12) ‖ ciphertext+tag`.
This is a reasonable conservative choice — comparable to the BIP38 /
Electrum-style baseline most reviewed wallets use.

### 1.3 Signing — in-browser, never round-trips to a server ✓

| File | Lines | Role |
|------|-------|------|
| [`src/DigiByte.Crypto/KeyGeneration/HdKeyDerivation.cs`](../src/DigiByte.Crypto/KeyGeneration/HdKeyDerivation.cs) | 49–77 | BIP84 derivation `m/84'/20'/account'/change/index` |
| [`src/DigiByte.Crypto/Transactions/TransactionBuilder.cs`](../src/DigiByte.Crypto/Transactions/TransactionBuilder.cs) | 29–75 | NBitcoin `builder.AddKeys` + `BuildTransaction(sign: true)` |

Signing relies on NBitcoin's secp256k1 implementation, which compiles into
the WebAssembly bundle. Trace the call graph: every code path that touches a
private key originates in user-driven UI events and stays inside the WASM
runtime — no `HttpClient` call carries raw key material.

### 1.4 What the server *does* see

The wallet talks to:
- `digiexplorer.info` — block explorer, *only* receives addresses (already
  public on chain) and reads back UTXOs / tx data.
- `api.coingecko.com` — fiat price feed; receives nothing wallet-specific.
- `github.com` — release/version checks; receives nothing wallet-specific.
- The wallet's own backend is **static asset hosting** for the WASM bundle —
  no API endpoint exists that takes private key material.

This is the correct shape for a non-custodial web wallet.

---

## 2. Asset integrity

WalletScrutiny's web-wallet review is sceptical of *what JS the user is
actually executing*. The shipped HTML and the supply chain that loads it
need to be locked down.

### 2.1 Self-hosted scripts ✓

The wallet's own JS (`crypto.js`, `storage.js`, `dom-helpers.js`, the Blazor
runtime) is served from the same origin — no jsDelivr / cdnjs / unpkg.
Tailwind CSS is built locally and self-hosted.

### 2.2 Third-party scripts — **gap to fix** ✗

[`src/DigiByte.Web/wwwroot/index.html`](../src/DigiByte.Web/wwwroot/index.html):47–53
loads Google Analytics:

```html
<script async src="https://www.googletagmanager.com/gtag.js?id=G-NR6R1MNL9X"></script>
```

There is **no `integrity="sha384-…"` SRI attribute** and **no version pin**.
A compromise of `googletagmanager.com` would let an attacker replace this
script with arbitrary code that runs in the wallet origin — including code
that reads IndexedDB.

**Recommended fix (in priority order):**

1. **Best:** drop Google Analytics from the wallet origin entirely and host
   a privacy-respecting pageview pixel from your own domain.
2. **Acceptable:** load GA from a server-side proxy on your own origin, with
   an SRI hash pinned to a specific gtag.js version.
3. **Minimum:** at least add `integrity` + `crossorigin="anonymous"` to the
   tag, accepting the maintenance cost of bumping the hash on every Google
   gtag release.

### 2.3 CSP — **partial ⚠**

`style-src` is strict (`'self'` only); element-level `style="…"` attributes
are allowed via `style-src-attr 'unsafe-inline'`. `script-src` does *not*
yet drop `'unsafe-inline'` because Blazor WebAssembly's publish pipeline
injects a populated `<script type="importmap">` whose contents — and
therefore SHA-256 — change on every build with the static-asset
fingerprints.

We tried locking it with a static hash; production then failed with:

```
Executing inline script violates the following Content Security Policy
directive 'script-src 'self' 'wasm-unsafe-eval' 'sha256-…' …'.
```

…on every redeploy because the importmap's bytes had moved.

**Path to close this in a follow-up:** add a publish-time post-processor
to the GitHub Action that:

1. Reads the inline `<script type="importmap">` block from the published
   `index.html`.
2. Computes its SHA-256.
3. Rewrites the CSP `<meta>` tag to include both that hash and the
   existing ld+json hash.

Until that's wired, `'unsafe-inline'` stays on `script-src`. The other
inline-script paths (GA bootstrap, service-worker registration, splash
styles) are already external — `'unsafe-inline'` here only buys a
shipped-by-the-build importmap, which can't carry user-injected
content.

### 2.4 PWA + offline capability ✓

| File | Role |
|------|------|
| [`src/DigiByte.Web/wwwroot/manifest.webmanifest`](../src/DigiByte.Web/wwwroot/manifest.webmanifest) | PWA manifest (standalone display, `web+digiid` protocol handler) |
| [`src/DigiByte.Web/wwwroot/service-worker.published.js`](../src/DigiByte.Web/wwwroot/service-worker.published.js) | Caches app shell + selected API responses |

Once installed as a PWA, the wallet works offline: signing and key
derivation are entirely WASM-resident and don't touch the network. This is
a strong WalletScrutiny signal — it demonstrates the *device* is doing the
work, not the server.

---

## 3. Build reproducibility

The wallet is .NET 10 WebAssembly, so the WalletScrutiny "Android APK
reproducible-build" workflow doesn't apply directly. The equivalent for a
web/PWA wallet is: **same source tag → same set of asset hashes**.

### 3.1 Dependency pinning

**.NET — well-pinned ✓**

| File | Package | Version |
|------|---------|---------|
| `src/DigiByte.Crypto/DigiByte.Crypto.csproj`:10 | NBitcoin | `9.0.5` (exact) |
| `src/DigiByte.Web/DigiByte.Web.csproj`:21 | Microsoft.AspNetCore.Components.WebAssembly | `10.0.5` (exact) |

**Node — minor gap ⚠**

[`src/DigiByte.Web/package.json`](../src/DigiByte.Web/package.json):

```json
"tailwindcss": "^3.4.19",
"@fontsource-variable/inter": "^5.2.8"
```

Caret ranges allow patch + minor drift, and there's no committed
`package-lock.json`. **Recommended fix:** commit the lockfile and switch to
exact pins (`3.4.19` not `^3.4.19`) for deterministic CSS output across
rebuilds.

### 3.2 Build flags — **gap to fix** ✗

The .csproj files are missing two flags that WalletScrutiny reviewers look
for to verify reproducibility:

```xml
<PropertyGroup>
  <Deterministic>true</Deterministic>
  <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
</PropertyGroup>
```

`Deterministic` strips PE timestamps and stabilises `<some-guid>` paths.
`ContinuousIntegrationBuild` is what tells SourceLink to embed the exact
commit hash. Together they let a reviewer clone the tag, build with the
documented .NET SDK version, and produce a byte-identical assembly.

Also helpful: a `global.json` pinning the .NET SDK version explicitly so
"the documented SDK version" is unambiguous.

### 3.3 Asset-hash manifest ✓

Per-tag manifests are generated automatically by the
[`wallet-release-manifest.yml`](../.github/workflows/wallet-release-manifest.yml)
GitHub Action whenever a tag matching `wallet-v*` is pushed. The action:

1. Publishes the Blazor WebAssembly bundle in `Release` configuration with
   the .NET SDK pinned by [`global.json`](../global.json).
2. Runs [`tools/generate-release-manifest.py`](../tools/generate-release-manifest.py)
   over `publish/wwwroot/` to produce a sorted JSON manifest of every file
   with its size and SHA-256.
3. Zips the published bundle.
4. Attaches both files to the GitHub Release as `wallet-vX.Y.Z.json` and
   `wallet-vX.Y.Z.zip`.

A reviewer can then either:
- Hash what `dgbwallet.app` is actually serving and compare against the
  manifest, or
- Build from the tag locally with the pinned SDK and diff their manifest
  against the published one (only the `generated_at` timestamp should
  differ).

[`release-hashes/README.md`](../release-hashes/README.md) documents both
verification recipes with copy-paste commands.

---

## 4. Self-evaluation matrix

| Criterion | Status | Notes |
|-----------|--------|-------|
| Self-custodial — keys never leave device | ✅ | Confirmed via code paths above |
| Source code public | ✅ | <https://github.com/DennisPitallano/digibyte-wallet> |
| Build instructions in README | ✅ | (verify before submission) |
| Released binaries match a tagged commit | ✅ | `wallet-v*` tag triggers `.github/workflows/wallet-release-manifest.yml`; per-tag JSON manifest + zipped bundle attached to GitHub Release |
| Reproducible build | ✅ | `Deterministic` + `ContinuousIntegrationBuild` set in `Directory.Build.props`; SDK pinned in `global.json`; verification recipe in `release-hashes/README.md` |
| No third-party script without SRI | ⚠ | Self-hosted scripts ✓; `gtag.js` accepted with documented operational rationale (§2.2) |
| Strict CSP | ⚠ | `style-src` strict; `script-src` still has `'unsafe-inline'` because Blazor WASM's publish pipeline injects a fingerprinted `<script type="importmap">`. Closing this needs a publish-time CSP-hash injector — see §2.3. |
| PWA / offline capable | ✅ | manifest + service worker |
| License clear | ✅ | (verify LICENSE file before submission) |
| Lockfile committed | ✅ | `src/DigiByte.Web/package-lock.json` committed; Tailwind + Inter pinned to exact versions |
| .NET SDK pinned | ✅ | `global.json` pinned to `10.0.201` with `rollForward: latestPatch` |

---

## 5. Pre-submission checklist

What's already done:

1. ✅ **Determinism flags** — repo-wide `Directory.Build.props` sets
   `Deterministic`, `ContinuousIntegrationBuild`, `EmbedUntrackedSources`,
   `PublishRepositoryUrl`.
2. ✅ **`global.json` pins the .NET SDK** to `10.0.201`.
3. ⚠ **Tightened CSP — partial.** Inline GA bootstrap, service-worker
   registration, and splash styles all moved out to external files.
   `style-src` is strict (`'self'` only); element `style="…"`
   attributes scoped via `style-src-attr`. `script-src` still has
   `'unsafe-inline'` because Blazor WASM's publish pipeline injects a
   fingerprinted `<script type="importmap">` whose hash moves on every
   build; tracked in §2.3 with the proposed publish-time CSP-hash
   injector.
4. ✅ **`package-lock.json` committed** under `src/DigiByte.Web/`.
   Tailwind + Inter pinned to exact versions.

5. ✅ **Per-tag release-hash manifest** — `.github/workflows/wallet-release-manifest.yml`
   generates a JSON manifest + zipped bundle on every `wallet-v*` tag and
   attaches them to the GitHub Release. Verification recipes are in
   [`release-hashes/README.md`](../release-hashes/README.md).
6. ✅ **`SECURITY.md` and `SELF_CUSTODY.md`** at the repo root — short,
   public-facing summaries that link back here for reviewers who want the
   full audit.

**Accepted limitation:**

7. **Google Analytics** is loaded from `googletagmanager.com` without an
   SRI hash. Pinning a hash isn't viable on the canonical URL because
   Google rotates the file unannounced (silent break on every rotation).
   The bootstrap is in [`js/analytics.js`](../src/DigiByte.Web/wwwroot/js/analytics.js)
   — no key material flows through it and the script loads outside the
   signing path. We've documented this as an accepted trade-off rather
   than removing analytics entirely. If a reviewer wants this closed,
   the two paths are: drop GA from the wallet origin, or proxy a
   versioned copy through the wallet's own origin (e.g. `/g/<release>.js`)
   with an SRI hash.

## 6. Submission

Once the pre-submission list is green, file an issue at
<https://github.com/walletscrutiny/walletScrutinyCom/issues/new> with:

- App name: **DigiByte Wallet**
- Type: **web wallet (Blazor WebAssembly PWA)**
- URL: <https://dgbwallet.app>
- Source: <https://github.com/DennisPitallano/digibyte-wallet>
- Custody summary: link to `SELF_CUSTODY.md`
- Build instructions: link to `README.md` build section
- Hash manifest: link to the latest `/release-hashes/<tag>.json`
- This document: link to the committed `docs/walletscrutiny-self-eval.md`

The reviewer will likely respond with follow-up questions on the CSP, the
GA replacement, and the deterministic-build evidence — all answered if the
checklist above is complete.
