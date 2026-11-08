# Security & Threat Model

> Last updated: April 2026 | Version: 0.2.0-alpha

This document provides a comprehensive security analysis of the DigiByte Wallet PWA for technical reviewers, auditors, and contributors. It covers the threat model, attack surface, mitigations, and known limitations.

---

## Table of Contents

1. [Security Architecture Overview](#security-architecture-overview)
2. [Trust Model](#trust-model)
3. [Cryptographic Primitives](#cryptographic-primitives)
4. [Key Management & Storage](#key-management--storage)
5. [Authentication & Access Control](#authentication--access-control)
6. [Network Security](#network-security)
7. [Content Security Policy](#content-security-policy)
8. [Service Worker & Caching](#service-worker--caching)
9. [SignalR Real-Time Security](#signalr-real-time-security)
10. [Transaction Security](#transaction-security)
11. [Digi-ID Authentication Protocol](#digi-id-authentication-protocol)
12. [Multisig Wallet Security](#multisig-wallet-security)
13. [Threat Matrix](#threat-matrix)
14. [OWASP Top 10 Mapping](#owasp-top-10-mapping)
15. [Known Limitations](#known-limitations)
16. [Recommendations for Future Hardening](#recommendations-for-future-hardening)

---

## Security Architecture Overview

The DigiByte Wallet is a **self-custodial** Progressive Web App (PWA) built with Blazor WebAssembly (.NET 10). The fundamental security property is:

> **Private keys never leave the user's device.** All cryptographic operations (key generation, transaction signing, Digi-ID challenge signing) happen entirely within the browser's WebAssembly sandbox.

```
┌─────────────────────────────────────────────────────────────┐
│                    User's Browser (Trust Boundary)           │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              Blazor WASM Sandbox                        │ │
│  │                                                         │ │
│  │  ┌──────────┐  ┌──────────────┐  ┌──────────────────┐ │ │
│  │  │   UI     │  │ WalletService│  │  NBitcoin Crypto  │ │ │
│  │  │ (Razor)  │  │ (key mgmt)   │  │  (BIP39/44/174)  │ │ │
│  │  └────┬─────┘  └──────┬───────┘  └────────┬─────────┘ │ │
│  │       │               │                    │            │ │
│  │  ┌────┴────────────── ┴────────────────────┴─────────┐ │ │
│  │  │     SubtleCrypto (AES-256-GCM) + Argon2id worker   │ │ │
│  │  └───────────────────────┬───────────────────────────┘ │ │
│  │                          │                              │ │
│  │  ┌───────────────────────┴───────────────────────────┐ │ │
│  │  │        IndexedDB (encrypted ciphertext only)       │ │ │
│  │  └────────────────────────────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────┘ │
│                          │                                   │
│                          │ HTTPS (public data only)          │
└──────────────────────────┼───────────────────────────────────┘
                           │
              ┌────────────┴──────────────┐
              │   Public Blockchain APIs   │
              │  (Blockbook, Esplora, Node)│
              │  Only: balances, UTXOs,    │
              │  broadcast signed txs      │
              └───────────────────────────┘
```

**Data that crosses the trust boundary (outbound):**
- Signed transactions (for broadcast)
- Public addresses (for balance/UTXO queries)
- Digi-ID signatures (challenge-response auth)
- Public keys (multisig room coordination via SignalR)

**Data that NEVER crosses the trust boundary:**
- Mnemonic seed phrases
- Private keys (HD or WIF)
- PINs
- Decrypted wallet data

---

## Trust Model

### What we trust

| Component | Trust Basis |
|-----------|-------------|
| Browser WebAssembly sandbox | W3C spec, browser vendor security teams |
| SubtleCrypto API | OS-level cryptographic provider (CNG/OpenSSL) |
| NBitcoin library | Widely audited .NET Bitcoin library |
| IndexedDB same-origin policy | Browser same-origin enforcement |
| TLS/HTTPS | Certificate authorities, browser TLS stack |

### What we do NOT trust

| Component | Mitigation |
|-----------|------------|
| Our own servers | Self-custodial — no keys on server |
| API responses | Used only for display; signing uses local keys |
| Browser extensions | CSP limits injection; WASM memory is opaque |
| Network intermediaries | HTTPS enforced; data integrity via blockchain |
| Other origins | Same-origin policy; CORS whitelist on API |

### Threat Actors

| Actor | Capability | Target |
|-------|-----------|--------|
| Remote attacker | XSS, phishing, MITM | Steal keys, swap addresses |
| Malicious extension | DOM access, network interception | Read clipboard, inject scripts |
| Physical attacker | Device access, forensics | Brute-force PIN, read storage |
| Compromised server | Serve malicious WASM, API manipulation | Replace wallet code, fake data |
| Malicious co-signer | Room manipulation, false readiness | Disrupt multisig creation |

---

## Cryptographic Primitives

### Encryption: AES-256-GCM with Argon2id Key Derivation

All sensitive data is encrypted before storage using the Web Crypto API (`SubtleCrypto`) for AES-256-GCM and a memory-hard
key derivation, Argon2id, that runs in the browser (specification `009-stronger-pin-protection`, finding DGB-04).

| Parameter | Value |
|-----------|-------|
| Algorithm | AES-256-GCM (authenticated encryption) |
| Key Derivation | Argon2id, parallelism 1, 32-byte output, run in a dedicated Web Worker that is terminated after each use |
| Implementation | `hash-wasm` 4.13.0 (MIT), a WebAssembly build vendored in `wwwroot/js/vendor/` with its SHA-256 recorded; same-origin, covered by the existing `'wasm-unsafe-eval'` CSP entry |
| Cost profiles | standard 64 MiB, 3 passes; low-memory 32 MiB, 3 passes; floor 19,456 KiB (19 MiB), 2 passes. The app tries standard, then falls back to smaller profiles when the device cannot allocate, and never goes below the floor. The profile values are provisional until they are frozen against device benchmarks (see below). |
| Salt | 16 bytes (cryptographically random per encryption) |
| Nonce | 12 bytes (cryptographically random per encryption) |
| Key Size | 256 bits |
| Authentication Tag | 128 bits (appended by GCM) |

**Stored format, the `dgb2` envelope** (`dgb2$` followed by base64url, unpadded):
```
[header: 9 bytes] [salt: 16] [nonce: 12] [ciphertext + auth tag]
header = version(1) | kdf id(1) | memory KiB(4, big-endian) | passes(1) | lanes(1) | credential kind(1: PIN, 2: passphrase)
```

**Security properties:**
- **Self-describing**: the parameters used are stored with each value, so they can change later without guesswork.
- **Parameter downgrade is detected**: the 9-byte header is the AES-GCM additional authenticated data, so changing the stored memory, passes or credential kind makes decryption fail.
- **Floor enforced on read and write**: anything below the floor profile is refused when written and when read.
- **Unknown versions are refused untouched**: a value with a newer prefix (`dgb3$` and up) produces an "update required" message and is never altered or counted as a wrong credential.
- **Non-deterministic and no key reuse**: fresh salt and nonce for every encryption.
- **Environmental failures are not wrong credentials**: running out of memory, an unsupported browser or unreadable data are reported as such and do not count toward the attempt limit.

**Credential types.** A wallet is protected by either a six-digit PIN (ASCII digits) or a passphrase of 12 to 256
characters, normalised with Unicode NFKC so the same visible text gives the same key on every device. Passphrases on a
common-password deny list (about 2,300 entries of 12 or more characters, from SecLists, MIT) and trivially repeated or
sequential strings are refused. The deny list is a filter, not a strength meter.

**Legacy format (still readable, never written for existing wallets).** Before this change values were
`Base64(salt(16) | iv(12) | ciphertext+tag)` with the key from PBKDF2-SHA256, 100,000 iterations, no stored parameters and no
authenticated header. They remain readable indefinitely for unlock, recovery and export. Legacy values never contain `$`,
which is how the two formats are told apart.

**Device benchmark status.** One development desktop (Chromium, median of 3) measured the standard profile at about 322 ms
per derivation, the low-memory profile at about 188 ms, the floor at about 75 ms and legacy PBKDF2 at about 18 ms. The
reference low-end phone, a mid-range phone and the GPU-class comparison are still to be measured, and the profile values stay
provisional until they are. A developer-only helper, `dgbCrypto.bench()`, prints these figures (localhost, or with
`localStorage.dgbBench = "1"`; off in release builds).

### HD Key Derivation: BIP39 / BIP84

| Standard | Implementation |
|----------|---------------|
| BIP39 | 24-word mnemonic → 512-bit seed (PBKDF2-HMAC-SHA512, 2048 iterations) |
| BIP84 | Derivation path: `m/84'/20'/0'/change/index` (coin type 20 = DigiByte, native SegWit) |
| BIP67 | Deterministic key sorting for multisig (lexicographic pubkey order) |
| BIP174 | PSBT format for multisig transaction passing |

### Signature Algorithms

| Use Case | Algorithm |
|----------|-----------|
| Transaction signing | ECDSA secp256k1 (via NBitcoin) |
| Digi-ID challenge signing | ECDSA secp256k1 with Bitcoin message prefix |
| Multisig | P2SH-P2WSH / P2WSH multi-signature scripts |

---

## Key Management & Storage

### Storage Architecture

All persistent data resides in **browser IndexedDB**, scoped to the application's origin.

```
IndexedDB: 'digibyte-wallet' (version 1)
└── Object Store: 'keyval'
    ├── wallet_seed_v2_{walletId}      → `dgb2$` envelope: Argon2id + AES-256-GCM encrypted mnemonic/WIF/xpub
    ├── wallet_seed_{walletId}         → legacy PBKDF2 value (Base64). Kept untouched for 7 days after an upgrade so an older
    │                                    app version can still unlock; removed on the first unlock after that, and at once when
    │                                    the PIN or passphrase is changed or reset
    ├── wallet_migration_{walletId}    → JSON {migratedAt, legacyFingerprint, legacyRemovedAt} for the upgrade above
    ├── wallet_info_{walletId}         → JSON metadata (name, type, network, creation date, credential type, protection level)
    ├── active_wallet_id               → GUID of currently active wallet
    ├── multisig_wallet_list           → JSON array of multisig wallet entries
    ├── multisig_config_{walletId}     → JSON multisig config (redeem script, co-signers, threshold)
    ├── multisig_pending_{walletId}    → JSON pending PSBT transactions
    ├── contacts                       → JSON contact list (names + addresses)
    ├── payment_requests               → JSON payment request history
    └── app_preferences                → JSON settings (theme, network, currency)
```

### What is encrypted

| Data | Encrypted | Encryption Key |
|------|-----------|---------------|
| Mnemonic seed phrase | ✅ AES-256-GCM | Derived from the PIN or passphrase via Argon2id (legacy values: PBKDF2) |
| WIF private key | ✅ AES-256-GCM | Derived from the PIN or passphrase via Argon2id (legacy values: PBKDF2) |
| Extended public key (xpub) | ✅ AES-256-GCM | Derived from the PIN or passphrase via Argon2id (legacy values: PBKDF2) |
| DigiDollar store | ✅ AES-256-GCM | Derived from the unlocked seed with HKDF-SHA256, not from the PIN or passphrase |
| Wallet metadata | ❌ Plaintext JSON | — |
| Multisig config (redeem script) | ❌ Plaintext JSON | — |
| Contacts | ❌ Plaintext JSON | — |
| Preferences | ❌ Plaintext JSON | — |

**Rationale for unencrypted fields:** Multisig configs, contacts, and preferences contain no secret material. Redeem scripts are public (shared with all co-signers). Encrypting them would require PIN entry for every metadata read, degrading UX without security benefit.

### Key Lifecycle

```
CREATE:   Mnemonic generated → Argon2id key from the PIN or passphrase → AES-GCM → stored as a dgb2 envelope
UNLOCK:   Credential entered → Argon2id (in a worker) → AES-GCM decrypt → keys held in WASM memory
          A legacy value that unlocks is re-encrypted as a dgb2 envelope (see below), the old copy kept for 7 days
LOCK:     Keys zeroed from memory → requires the credential to unlock again
RECOVER:  Valid recovery phrase + a new PIN or passphrase → new dgb2 envelope → old copies deleted
DELETE:   IndexedDB.clear() → all data wiped (irreversible)
```

**Automatic upgrade of existing wallets.** On a successful unlock of a legacy-format wallet the app writes a new `dgb2` value,
reads it back and checks it decrypts to the identical secret, and only then records the upgrade. Any failure (interruption,
storage full, out of memory, another tab) leaves the old value intact and usable and is retried at the next unlock; it never
blocks the unlock. A per-wallet cross-tab lock (Web Locks, with a storage-lease fallback) prevents two tabs rewriting at once.
If the stored legacy value changes after an upgrade (an older app version changed the PIN), the legacy value wins and the
wallet upgrades again.

**Devices that cannot run Argon2id.** If a browser cannot run the memory-hard function at all (or has no free memory for even
the floor profile), a new wallet is created with the legacy protection only after a plain warning and the user's explicit
agreement. It is marked `legacy` and upgrades by itself on its first unlock on a capable browser. An existing wallet is never
moved to a weaker format.

### Memory Security

- Private keys exist in WASM linear memory only while the wallet is unlocked
- Locking the wallet (`Lock()`) sets key references to `null`
- .NET garbage collector will eventually reclaim the memory
- **Limitation**: No explicit memory zeroing (WASM does not expose `SecureString` or `RtlZeroMemory`)
- **Limitation**: Browser memory dumps (dev tools, crash reports) may contain key material while unlocked

---

## Authentication & Access Control

### PIN and Passphrase Lockout with Exponential Backoff

The wallet is unlocked with a 6-digit PIN or a passphrase. Wrong attempts trigger a progressive lockout, identical for both
credential types; only the wording differs. Problems that are not the user's mistake (out of memory, unsupported browser,
unreadable data, data from a newer version) are explained separately and are not counted.

**Implementation:** Client-side enforcement in `PinAttemptThrottle`, used by `Unlock.razor` and `PsbtSign.razor`

| Failed Attempts | Lockout Duration |
|----------------|-----------------|
| 1–2 | No lockout (instant retry) |
| 3 | 15 seconds |
| 4 | 30 seconds |
| 5 | 60 seconds |
| 6 | 2 minutes |
| 7 | 4 minutes |
| 8+ | 4 minutes (capped) |

**Lockout formula:**
```
delay = 15 × 2^(min(failedAttempts - 3, 5)) seconds
```

**Brute-force analysis:**
- PIN space: 10^6 = 1,000,000 combinations
- Without lockout: trivially crackable by automated tool
- The lockout is client-side and per page: it slows guessing inside the app and does nothing against an attacker who copies the
  browser's storage. Such an attacker can clear localStorage, extract the IndexedDB ciphertext and guess offline.

**Offline guessing against extracted ciphertext:**
- Every guess now costs an Argon2id derivation (64 MiB, 3 passes at the standard profile) instead of 100,000 PBKDF2 iterations.
  On one development desktop that is about 322 ms against about 18 ms, roughly 18 times the CPU cost per guess. Because
  Argon2id is memory-hard, the gap against GPU and specialised hardware is expected to be much larger; the target is at least
  100 times (specification 009, SC-002) and the measured figure will be recorded here once the device benchmark is complete.
- **A six-digit PIN is still a short secret.** There are only a million candidates, so someone who holds a copy of the browser
  data can still work through them with effort. That is a limit of the PIN's size, not of the key derivation.
- **A passphrase is the real protection.** A passphrase of 12 or more characters that is not on the deny list is not practically
  guessable offline with realistic resources. The app offers it at wallet creation, in Settings (a dismissible tip on PIN
  wallets) and during recovery, and does not require it.

### Recovery Flow

- Users can reset their PIN or passphrase by entering their 24-word mnemonic seed phrase (or, for imported-key and watch-only
  wallets, re-importing the key or xpub), and can switch between a PIN and a passphrase while doing so
- The secret is re-encrypted as a `dgb2` envelope under the new credential; the old credential stops working and any legacy
  copy is deleted at once
- An invalid or malformed phrase changes nothing in storage
- No server interaction required
- A forgotten passphrase cannot be reset by the project or anyone else; the recovery phrase is the only way back in

### Biometric Unlock

Biometric unlock keeps a copy of each wallet's seed encrypted with an AES key. Where the browser supports the WebAuthn **PRF**
extension, that key is derived from the authenticator at each unlock (HKDF-SHA256 over the PRF output) and is never stored, so a
copy of the browser's storage is not enough to open the seed copies. Enrolments made before this change stored a random key next
to each ciphertext; the app offers a one-time upgrade after the next credential unlock, re-protects every wallet's copy under the
derived key and then deletes the stored key. Where PRF is not available, biometric unlock works as before but it is only a
convenience: the app says so plainly, asks before turning it on, and shows existing users the same notice once. Credential-gated
actions (viewing the recovery phrase, changing the credential, signing) still ask for the credential.

---

## Network Security

### HTTPS Enforcement

| Component | HTTPS | Mechanism |
|-----------|-------|-----------|
| Blazor WASM app | ✅ | Hosted on Railway (TLS termination at edge) |
| DigiByte.Api | ✅ | Railway TLS + ForwardedHeaders middleware |
| Blockchain explorers | ✅ | All explorer URLs use HTTPS |
| SignalR WebSocket | ✅ | `wss://` protocol |
| DigiByte Node RPC | ⚠️ | HTTP Basic Auth (localhost only, not exposed publicly) |

### Own node as a data source (privacy)

When the deployment turns on the own-node mode (`WALLET_NODE_ENABLED`, see [RAILWAY_NODE.md](RAILWAY_NODE.md)), the wallet may use the wallet team's DigiByte node, through its node API, for sending, transaction lookups, fees and chain height, and as a backup for address balances and spendable outputs. Address history always comes from public explorers.

- **What the node operator sees:** the public transactions and addresses the wallet sends or looks up through the node. That is the same kind of public chain data a third-party explorer would see; only who sees it changes.
- **What it never sees:** private keys, the seed, the PIN or any signing operation. The node only receives already-signed transactions and public chain requests (constitution principle I).
- **When it is used:** only while the node's own health report says it is ready and its height agrees with a public explorer. Otherwise the wallet uses public explorers exactly as before.
- **What users are told:** the in-app data-source detail says so in one line whenever the own node is the active source, and the Help page explains it.

### CORS Configuration

```csharp
policy.WithOrigins(origins)    // Explicit whitelist (configurable)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials();       // Required for SignalR
```

- **Production origin:** Configured via `ClientOrigin` environment variable
- **No wildcard origins** — explicit whitelist only
- **Credentials allowed** — needed for SignalR WebSocket connections

### API Proxy Architecture

The wallet never directly calls CoinGecko or other rate-limited APIs from the browser. All price data is proxied through `DigiByte.Api`:

```
Browser → DigiByte.Api (CORS-allowed) → CoinGecko API
                                      → open.er-api.com (USD exchange rates, for display
                                        currencies CoinGecko does not quote; cached 6 h)
                                      → GitHub API (releases)
```

**Benefits:**
- Avoids exposing API keys in client-side code
- Prevents CORS issues with third-party APIs
- Server-side caching (60s TTL) reduces external API load
- Rate limiting absorbed by server, not end users

### Outbound requests to user-supplied URLs

Two server features call a URL somebody else supplies: the Digi-ID callback proxy (`DigiByte.Api`) and DigiPay webhooks (`DigiByte.Pay.Api`). Both go through the shared guard in `DigiByte.Server.Shared` (`OutboundUrlGuard`, `GuardedHttpClientExtensions`; referenced by those two services only):

- **Digi-ID callback**: the destination is derived from the signature-verified `digiid://` URI (`digiid://host/path?x=nonce` becomes `https://host/path?x=nonce`). A `callbackUrl` sent by the caller is only accepted if it equals that URL; `u=1` (http) is refused; a bad signature is refused. Rate limited to 10 requests a minute per client. Proxy refusals are `{"source":"proxy","reason":...}` so the wallet can tell them from the site's own reply.
- **Webhooks**: the URL is validated at merchant registration, at store update, and again at every dispatch. A stored URL that fails is not called; the delivery records "destination not allowed", and the stored URL and secret are left alone. `ResponseSnippet` keeps 512 characters as plain text. Merchant registration is limited to 5 an hour per client.
- **Rules (both)**: absolute `https`, no userinfo, at most 2048 characters; ports 443 only for Digi-ID and 443, 8443, 8080, 8000 for webhooks; no loopback, private, link-local (including the cloud metadata address), CGNAT, multicast or reserved addresses, and no IPv4-mapped IPv6 forms of them. Redirects are not followed. The address is checked on the socket actually connected (so DNS rebinding cannot change it after validation), with a 10 second timeout and a 64 KB response cap.
- **Development switch**: `Outbound:AllowLoopback=true` permits loopback only. It defaults to false and must not be set in production.
- **Client address for rate limits**: the rightmost `X-Forwarded-For` entry (the one Railway's proxy appends), captured before the forwarded-headers middleware runs, so a spoofed header cannot reset a limit.
- **Egress**: the guard is the control in code. Where Railway lets a service restrict outbound network access, also restrict `digibyte-api` and `digipay-api` to the public internet only. Whether Railway offers this for these services has not been confirmed from this repository; the maintainer records the answer in the release notes (spec `008-security-hardening`, T037).
- Refused attempts are logged server-side (destination host and the short reason); the client only ever gets the short category.

### Forwarded Headers (Reverse Proxy)

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

Ensures correct HTTPS detection behind Railway's load balancer.

---

## Content Security Policy

The CSP is enforced via `<meta>` tag in `index.html`:

```
default-src    'self'
script-src     'self' 'wasm-unsafe-eval' 'unsafe-inline'
style-src      'self' 'unsafe-inline'
img-src        'self' data: blob: https://avatars.githubusercontent.com
font-src       'self'
connect-src    'self' http://localhost:5007 https://digiexplorer.info
               https://digibyteblockexplorer.com
               https://digibyte-api-production.up.railway.app
               https://api.coingecko.com https://api.github.com ws: wss:
frame-src      'none'
object-src     'none'
base-uri       'self'
```

### Directive Analysis

| Directive | Value | Threat Mitigated |
|-----------|-------|-----------------|
| `default-src 'self'` | Only load from own origin | Foreign resource injection |
| `script-src 'self' 'wasm-unsafe-eval'` | No external scripts | XSS via script injection |
| `frame-src 'none'` | No iframes allowed | Clickjacking |
| `object-src 'none'` | No plugins/Flash | Plugin-based exploits |
| `base-uri 'self'` | Prevent `<base>` hijacking | Relative URL redirection |
| `font-src 'self'` | Self-hosted Inter font | Font-based tracking/fingerprinting |
| `connect-src` whitelist | Only approved APIs | Data exfiltration to rogue endpoints |

### Required Unsafe Directives

| Directive | Reason | Risk Level |
|-----------|--------|-----------|
| `'wasm-unsafe-eval'` | .NET WASM runtime requires eval-like WASM compilation | Low — only WASM, not JS `eval()` |
| `'unsafe-inline'` (scripts) | Blazor generates inline event handlers | Medium — XSS vector if DOM is compromised |
| `'unsafe-inline'` (styles) | Dynamic Blazor style bindings | Low — CSS-only, no script execution |

### Additional Security Headers

A `<meta>` tag cannot set `frame-ancestors` or `X-Frame-Options`, so the wallet's Nginx (`docker/web/nginx.conf`) sends these as real response headers on every route, from `docker/web/security-headers.conf` (included in the server block and in every location that sets its own `add_header`, because Nginx drops inherited ones there):

```
Content-Security-Policy: frame-ancestors 'none'
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

The `<meta>` copies of `X-Content-Type-Options` and `Referrer-Policy` stay for local development. Verify with `curl -sI` on `/`, `/config.json`, a `.js` file, `/service-worker.js`, an unknown route and a 404.

### No third-party scripts

Google Analytics was removed from the wallet origin (`gtag.js`, `js/analytics.js` and their CSP entries). No third-party script runs in the wallet origin and none is loaded from a CDN. First-party analytics, if wanted, is a separate spec.

### PIN and passphrase key derivation (DGB-04)

Finding DGB-04 of the external review was that the wallet key came straight from the six-digit PIN with PBKDF2 at 100,000
iterations, so a copied browser profile allowed fast offline guessing. Specification `009-stronger-pin-protection` replaces
it with Argon2id in a versioned envelope, adds an optional passphrase, upgrades existing wallets automatically with a rollback
window, and closes the stored biometric key where the browser allows. See Cryptographic Primitives, Key Management and Known
Limitations for the details and what remains.

**Implementation versus this document.** Implemented and covered by tests: the envelope and codec, Argon2id in a worker, the
passphrase, automatic upgrade with the 7-day legacy copy, recovery and credential change, unsupported-device handling, the
biometric PRF path and its upgrade, and localised wording. **Not yet done:** freezing the Argon2id profile values against the
reference low-end phone, a mid-range phone and desktop, and recording the GPU-class cost ratio (SC-002) and unlock timings
(SC-003); running the real PRF path on a device with a platform authenticator; and a small usability check (SC-006).

### Self-Hosted Assets (Supply Chain Mitigation)

All frontend assets are self-hosted to eliminate CDN dependency risks:

| Asset | Source | Self-Hosted |
|-------|--------|------------|
| Tailwind CSS | Pre-compiled, purged (~37KB) | ✅ `wwwroot/css/tailwind.css` |
| Inter font | WOFF2 | ✅ `wwwroot/fonts/` |
| jsQR | QR scanner library | ✅ `wwwroot/js/jsqr.js` |
| Heroicons | SVG icons inline in Razor | ✅ No external fetch |

---

## Service Worker & Caching

### Caching Strategy

| Resource Type | Strategy | Rationale |
|--------------|----------|-----------|
| App shell (HTML, CSS, JS, WASM, fonts) | Cache-first | Instant load, offline support |
| External APIs (CoinGecko, explorers, GitHub) | Network-first (8s timeout) | Fresh data, cache fallback |
| SignalR/WebSocket | Pass-through (not cached) | Real-time, stateful connections |

### Cache Versioning

```javascript
const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
```

- Cache is versioned by the build manifest hash
- Old caches are deleted on service worker `activate`
- Automatic update check every 60 minutes

### Offline Behavior

- App UI loads fully offline (cached WASM + assets)
- Price/balance data served from last cached response
- Transaction signing works offline (signing is local)
- Broadcasting requires network (queued until online)
- SignalR features gracefully degrade (no real-time)

### Security Considerations

- ✅ Cache versioned — stale code replaced on update
- ✅ Old caches pruned on activation
- ⚠️ API responses cached without explicit TTL (relies on version bump)
- ⚠️ A compromised service worker could serve modified WASM — mitigated by HTTPS + integrity hashes in asset manifest

---

## SignalR Real-Time Security

### MultisigRoomHub — Collaborative Wallet Creation

The MultisigRoomHub enables real-time multisig wallet creation without manual public key exchange.

#### Room Lifecycle

```
CREATE → 6-char invite code generated (cryptographic RNG)
       → Room stored in server memory (ConcurrentDictionary)
       → 15-minute TTL timer started
       → Initiator auto-joins

JOIN   → Invite code validated
       → Public key validated (compressed secp256k1: 66 hex chars, 02/03 prefix)
       → Participant added to room
       → All participants notified

READY  → Each participant signals readiness
       → When all ready → wallet creation event broadcast
       → Room auto-cleaned

EXPIRE → 15 minutes elapsed → room destroyed
       → Initiator disconnect → room destroyed
       → All participants notified of closure
```

#### Invite Code Generation

```csharp
const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";  // 32 chars, no 0/O/1/I
RandomNumberGenerator.Fill(bytes);  // Cryptographically secure
```

- **Entropy:** 6 characters × ~5 bits each = ~30 bits → ~1 billion possible codes
- **Collision resistance:** Adequate for short-lived rooms (15 min TTL)
- **Confusable characters excluded:** No `0/O`, no `1/I`

#### Public Key Validation

```csharp
private static bool IsValidPublicKeyHex(string hex)
{
    if (hex.Length != 66) return false;              // 33 bytes compressed
    if (!hex.StartsWith("02") && !hex.StartsWith("03")) return false;  // SEC1 prefix
    return hex.All(c => Uri.IsHexDigit(c));          // Valid hex chars
}
```

#### Data Sanitization

Before broadcasting room state to participants, sensitive server-internal fields are stripped:

```csharp
private static MultisigParticipant SanitizeParticipant(MultisigParticipant p)
{
    return new MultisigParticipant {
        Name = p.Name,
        PublicKeyHex = p.PublicKeyHex,
        IsInitiator = p.IsInitiator,
        IsReady = p.IsReady,
        ConnectionId = ""  // Server-only field, never sent to clients
    };
}
```

#### Security Assessment

| Property | Status | Notes |
|----------|--------|-------|
| Authentication | ⚠️ None | Rooms are relay-only; wallet creation is client-side |
| Data transmitted | ✅ Public keys only | Private keys never leave device |
| Room enumeration | ✅ Mitigated | Must know 6-char code; no listing endpoint |
| ConnectionId exposure | ✅ Stripped | Server-only field sanitized |
| Room persistence | ✅ Memory-only | Not persisted; lost on server restart |
| TTL enforcement | ✅ 15 minutes | Auto-cleanup prevents resource exhaustion |
| Input validation | ✅ Strict | Public key format, threshold bounds, name presence |
| Replay attacks | ✅ Not applicable | Rooms are one-time use, ephemeral |

### TradeChatHub — P2P Trading Chat

**Current status:** Early prototype. Known limitations:

| Issue | Severity | Notes |
|-------|----------|-------|
| No authentication | 🔴 High | Any client can join any trade room by guessing GUID |
| SenderId hardcoded | 🔴 High | `Guid.Empty` — no message attribution |
| No rate limiting | ⚠️ Medium | Spam potential |
| Group isolation | ✅ | Messages only broadcast within group |

**Note:** TradeChatHub is not used in production flows. It is an early prototype for the P2P marketplace feature (see [Roadmap](ROADMAP.md)).

---

## Transaction Security

### Transaction Building

All transactions are built and signed entirely in the browser using NBitcoin:

```
User input (destination, amount) 
  → Address validation (NBitcoin BitcoinAddress.Create)
  → UTXO selection (largest-first or closest-match)
  → Transaction construction (NBitcoin TransactionBuilder)
  → Signing (ECDSA secp256k1 with private keys in WASM memory)
  → Verification (builder.Verify())
  → Broadcast via API (raw signed hex)
```

### Input Validation

| Input | Validation | Library |
|-------|-----------|---------|
| Destination address | `BitcoinAddress.Create()` with network check | NBitcoin |
| Amount | `Money` type enforces positive, satoshi precision | NBitcoin |
| Fee rate | Configurable (Low/Normal/Fast presets, manual sat/vB) | Custom |
| Memo (OP_RETURN) | Truncated to 80 bytes (protocol limit) | Custom |
| Public keys (multisig) | 66 hex chars, `02`/`03` prefix, hex-only | Custom |
| Redeem script (import) | NBitcoin `Script` parsing + threshold extraction | NBitcoin |

### UTXO Selection Strategies

| Strategy | Algorithm | Privacy Implication |
|----------|-----------|-------------------|
| Largest-first | Sort descending by amount, select until target met | Predictable order — reveals wallet size distribution |
| Closest-match | Find single UTXO closest to target, fallback to largest-first | Minimizes change output — better privacy |

### Change Address Derivation

Change addresses use the BIP84 internal chain: `m/84'/20'/0'/1/index`

- Separate derivation path from receiving addresses
- New change address per transaction
- Prevents address reuse (privacy)

### Multisig Transaction Flow (PSBT — BIP174)

```
Initiator creates PSBT → signs with own key → shares base64 PSBT with co-signers
  → Co-signer imports PSBT → adds their signature → returns updated PSBT
  → When M signatures collected → finalize → extract raw tx → broadcast
```

- PSBTs carry no private key material — only partial signatures
- Each signer validates the transaction before signing
- NBitcoin handles signature combination and finalization

---

## Digi-ID Authentication Protocol

### Protocol Flow

```
1. Website generates: digiid://domain/callback?x=nonce
2. Wallet scans QR → parses URI → displays domain for user approval
3. Wallet derives site-specific key: m/13'/SHA256(domain)'/0'/0
4. Wallet signs the full URI with Bitcoin message signing format
5. Wallet POSTs { address, uri, signature } to callback URL
6. Website verifies signature against address
```

### Site Key Isolation

Each website receives a **unique derived key** based on its domain:

```csharp
var domainBytes = Encoding.UTF8.GetBytes(domain.ToLower());
var hash = Hashes.SHA256(domainBytes);
var siteIndex = (int)(BitConverter.ToUInt32(hash, 0) & 0x7FFFFFFF);
// Path: m/13'/siteIndex'/0'/0
```

**Privacy property:** Different domains receive different key pairs. A website cannot correlate a user across domains.

### Message Signing Format

```
\x18DigiByte Signed Message:\n
[message length byte]
[original URI bytes]
→ SHA256d (double SHA256)
→ ECDSA sign with derived key
→ DER-encoded signature → Base64
```

- Standard Bitcoin message signing (adapted for DigiByte)
- Message prefix prevents cross-chain replay
- Nonce in URI prevents replay of the same challenge

### Security Properties

| Property | Status |
|----------|--------|
| Domain isolation | ✅ Unique key per website |
| No private key transmission | ✅ Only signature sent |
| Replay protection | ✅ Nonce (`x=`) parameter |
| HTTPS enforcement | ✅ Default; `u=1` flag allows HTTP (development) |
| User consent | ✅ Domain displayed for approval before signing |
| Cross-chain replay | ✅ "DigiByte Signed Message" prefix |

---

## Multisig Wallet Security

### Creation Security

- Public keys are validated (compressed SEC1 format, 33 bytes)
- Key order is deterministic via **BIP67** — all co-signers derive the same address regardless of input order
- Redeem scripts use standard OP_CHECKMULTISIG
- Address types: P2SH-P2WSH (default) or P2WSH

### Backup & Recovery

| Scenario | Recoverable? | How |
|----------|-------------|-----|
| Seed phrase recovery | ❌ Multisig NOT restored | Seed only restores personal HD wallet |
| Redeem script backup | ✅ Full restore | Import via Multisig → Import (paste or file upload) |
| Co-signer has backup | ✅ Any co-signer can share | Redeem script is the same for all co-signers |
| All backups lost | ❌ Funds locked | Requires M-of-N signatures; need redeem script to spend |

**Backup format:** The wallet provides a **Download Backup** feature that exports a `.txt` file containing:
- Wallet label, threshold (M-of-N), address, address type
- Redeem script (hex)
- All co-signer names and public keys
- Recovery instructions

### PSBT Security

- PSBTs carry **no private keys** — only partial signatures and transaction metadata
- Each co-signer independently validates the transaction before adding their signature
- Signatures cannot be forged without the corresponding private key
- NBitcoin handles BIP174 serialization/deserialization with strict parsing

### Signing PSBTs made elsewhere (`/settings/psbt-sign`)

A PSBT loaded on this page is **untrusted**: anyone may have built it, and every field in it may be false.

| Rule | Why |
|---|---|
| An input is the wallet's only when its script is one of the wallet's scanned addresses (native SegWit, or legacy within its range); key paths in the PSBT are ignored, for inputs and for change | A PSBT can claim an attacker's output is "change" or that a multisig input is the wallet's |
| Multisig, wrapped SegWit and Taproot inputs are never signed; Taproot is refused | Keeps signing to the single-key scripts the wallet holds |
| The wallet's input amounts and scripts are proven against the network's unspent list, which throws rather than returning an empty list when no source answers. A mismatch, a spent coin, or a coin known only on the other network is refused | Defeats false amounts that hide the real fee (the SegWit fee attack) |
| Chain checks run again just before signing and before broadcast | A coin spent after review is caught |
| Only the standard signature type (`SIGHASH_ALL`) is used; any other requested on any input is refused. Each input is signed on its own, never with `SignWithKeys` | Prevents signatures that let others change the transaction afterwards |
| DigiDollar transactions are refused, by version or data output | Their rules aren't checked by this page |
| A fee over 10% of what leaves, or an unverified fee on a shared transaction, needs an explicit acknowledgment | Makes overpaying visible |
| Signing and broadcasting run the spending limit, Baku's send-risk warning, then biometrics or a fresh PIN (with the unlock screen's wrong-PIN delay) | Same protections as Send |
| Before broadcast the signed PSBT is finalized again, so a tampered signature is never sent | The result handed to broadcast isn't trusted either |
| No PSBT contents are logged; the PSBT is held only by the page and discarded on lock | Avoids leaking transaction details |

Until shared transactions are fully checked (Story 4), other participants' amounts are taken from the PSBT alone. The review then says the fee is unverified and that outputs were not checked, and asks for acknowledgment.

---

## Threat Matrix

### High Severity

| # | Threat | Vector | Impact | Mitigation | Residual Risk |
|---|--------|--------|--------|-----------|---------------|
| T1 | Offline PIN brute-force | Extract IndexedDB ciphertext → offline PBKDF2 attack | Full key compromise | 100K PBKDF2 iterations; 6-digit PIN | **Medium** — 10^6 keyspace brutable on GPU in ~17 min |
| T2 | Compromised WASM delivery | MITM or server compromise → serve malicious WASM | Key extraction via modified code | HTTPS + CSP + service worker integrity hashes | **Low** — requires HTTPS compromise or railway infra breach |
| T3 | Malicious browser extension | Extension injects scripts, reads DOM, intercepts clipboard | Address swap, key extraction from DOM | CSP limits injection; WASM memory opaque to JS | **Medium** — sophisticated extensions can bypass |

### Medium Severity

| # | Threat | Vector | Impact | Mitigation | Residual Risk |
|---|--------|--------|--------|-----------|---------------|
| T4 | XSS | Injected script reads DOM / calls JS interop | Data exfiltration, address swap | Blazor rendering model (no innerHTML); CSP blocks inline | **Low** — Blazor's virtual DOM prevents most XSS |
| T5 | Clipboard snooping | Malware monitors clipboard for crypto addresses | User sends to attacker's address | Standard risk for all wallets; users verify visually | **Medium** — cannot mitigate fully from browser |
| T6 | CSRF on API | Forged requests to broadcast/relay endpoints | Broadcast pre-signed tx (unlikely, tx must be signed) | CORS whitelist; no sensitive mutations on API | **Low** |
| T7 | Session fixation | Attacker sets PIN lockout state | Minor DoS (user sees fake lockout) | Lockout is client-side; refresh clears state | **Low** |
| T8 | SignalR room brute-force | Guess 6-char invite codes | Join unauthorized multisig room | 30-bit code space; 15 min TTL; only public keys exposed | **Low** |

### Low Severity

| # | Threat | Vector | Impact | Mitigation | Residual Risk |
|---|--------|--------|--------|-----------|---------------|
| T9 | Blockchain analysis | UTXO patterns, address reuse | Privacy leakage (not fund loss) | Change addresses; closest-match UTXO selection | **Low** |
| T10 | Service worker cache poisoning | Compromised CDN or proxy | Serve stale/modified assets | No CDN; self-hosted; cache versioned by manifest | **Very Low** |
| T11 | Room DoS | Create many multisig rooms | Server memory exhaustion | 15 min TTL; rooms are lightweight (~1KB each) | **Very Low** |

---

## OWASP Top 10 Mapping

| # | OWASP Category | Applicability | Status |
|---|---------------|--------------|--------|
| A01 | Broken Access Control | PIN lockout, no server-stored credentials | ✅ Mitigated |
| A02 | Cryptographic Failures | AES-256-GCM, PBKDF2, BIP39/44, ECDSA | ✅ Industry-standard crypto |
| A03 | Injection | No SQL; Blazor prevents XSS; CSP blocks scripts | ✅ Mitigated |
| A04 | Insecure Design | Self-custodial eliminates many server-side risks | ✅ By design |
| A05 | Security Misconfiguration | CSP configured; CORS whitelisted; HTTPS enforced | ✅ Configured |
| A06 | Vulnerable Components | Minimal dependencies; self-hosted assets | ✅ Low attack surface |
| A07 | Auth Failures | Client-side PIN; PBKDF2 key derivation | ⚠️ See T1 (offline brute-force) |
| A08 | Data Integrity Failures | Service worker versioning; no deserialization of untrusted data | ✅ Mitigated |
| A09 | Logging & Monitoring | Client-side app; no server logging of sensitive data | ℹ️ N/A (no server-side user data) |
| A10 | SSRF | API proxies to known URLs only; no user-controlled URLs | ✅ Mitigated |

---

## Known Limitations

### Browser-Inherent Limitations

| Limitation | Impact | Possible Mitigation |
|-----------|--------|-------------------|
| No secure enclave access | Keys in WASM memory, not hardware-protected | Future: WebAuthn for PIN replacement |
| No memory zeroing | Private keys may linger in GC heap | Future: Use pinned byte arrays with explicit clear |
| Extension access to DOM | Malicious extension could read/modify UI | User responsibility; CSP helps but doesn't fully prevent |
| `'unsafe-inline'` in CSP | Slightly wider XSS attack surface | Required by Blazor; no current workaround |
| Single-origin IndexedDB | Any same-origin XSS compromise accesses all data | Data is encrypted; key material requires PIN |

### PIN and Passphrase Protection

| Limitation | Impact |
|-----------|--------|
| A 6-digit PIN has only 10^6 candidates | Someone with a copy of the browser data can still guess a PIN offline with effort, although each guess now costs far more. A passphrase removes this |
| Client-side lockout only, and per page | Attacker with device access can bypass the timer; the count resets when the page is left. It slows guessing in the app, nothing more |
| No server-side enforcement | No second factor; no remote wipe |
| The old copy stays for 7 days after an upgrade | For up to 7 days the legacy PBKDF2 copy exists so an older app version can still unlock; the full protection applies once it is removed |
| Wallets created without Argon2id support | Created with the legacy protection only after the user's agreement, marked `legacy`, and upgraded automatically on a capable browser |
| Biometric unlock without PRF | The key is stored next to the ciphertext, so the biometric prompt is a convenience and not extra protection. The user is told and can decline |
| A forgotten passphrase cannot be reset | Only the recovery phrase restores access |
| Recovery does not check the phrase belongs to this wallet | There is no old credential to compare with, so any valid phrase is accepted and replaces the stored secret; the user is expected to enter their own |
| The deny list is small | About 2,300 common entries of 12 or more characters; it is a filter, not a strength meter |
| The memory-hard cost needs free memory | On low-memory devices the app falls back to smaller profiles, down to the 19 MiB floor, and below that keeps the old format rather than weakening anything |
| Argon2id profile values are provisional | They are frozen after benchmarking real devices (see Known gap above) |

### Secrets in Logs and Telemetry (audit, FR-005)

The web app was audited for credential and key handling. There is no analytics or telemetry path: the only `console` calls in app code are the service worker's install messages, the QR scanner's camera error object, and the developer-only `?crypto-bench` table (disabled in release builds), which uses a fixed dummy credential and prints only profile, time and memory. No credential, derived key or plaintext is logged, sent to the JS worker beyond the derivation request, or included in an error message. User-visible failures for unlock, create and recover show either a fixed localised message or the exception text of a .NET or interop error, which does not carry call arguments.

### Multisig Limitations

| Limitation | Impact |
|-----------|--------|
| Redeem script not auto-backed up | Users must manually export backup file |
| Seed phrase doesn't restore multisig | Recovery requires redeem script (or co-signer sharing it) |
| PSBT exchange is manual | Copy/paste between co-signers (except real-time room creation) |

---

## Recommendations for Future Hardening

### Priority 1 — High Impact

| Recommendation | Addresses | Effort |
|---------------|----------|--------|
| **Increase PIN to alphanumeric passphrase** | T1 (offline brute-force) | Medium — UI change + increased PBKDF2 iterations |
| **Add Argon2id key derivation** | T1 — memory-hard function resists GPU attacks | Medium — requires Argon2 WASM or JS implementation |
| **WebAuthn / passkey support** | T1 — hardware-backed authentication replaces PIN | High — new auth flow + backward compatibility |
| **Server-side security headers** | Missing HSTS, X-Frame-Options, Permissions-Policy | Low — middleware addition |

### Priority 2 — Medium Impact

| Recommendation | Addresses | Effort |
|---------------|----------|--------|
| **Auto-prompt redeem script backup** on multisig creation | Data loss prevention | Low — UI prompt after wallet creation |
| **Encrypt multisig configs** | Defense-in-depth for redeem scripts | Low — use existing AES-256-GCM |
| **Rate limiting on API endpoints** | DoS protection, abuse prevention | Medium — middleware or reverse proxy |
| **TradeChatHub authentication** | T-Chat prototype security | Medium — JWT or signed message auth |
| **Subresource Integrity (SRI)** on script tags | Supply chain verification | Low — add `integrity` attributes |

### Priority 3 — Nice to Have

| Recommendation | Addresses | Effort |
|---------------|----------|--------|
| **Coin control UI** | Privacy improvement — user selects UTXOs | Medium |
| **UTXO randomization** | T9 — reduce blockchain analysis surface | Low |
| **Automatic backup reminders** | Periodic prompt to verify seed phrase backup | Low |
| **CSP nonce-based scripts** | Remove `'unsafe-inline'` if Blazor supports it | Blocked on Blazor framework |
| **Memory-safe key handling** | Pin/zero key bytes after use | Medium — .NET WASM limitations |

---

## Security Contact

If you discover a security vulnerability, please report it responsibly:

- **Email:** [file a private security advisory](https://github.com/DennisPitallano/digibyte-wallet/security/advisories/new) on GitHub
- **Do NOT** open a public issue for security vulnerabilities
- We aim to acknowledge reports within 48 hours

---

## Changelog

| Date | Change |
|------|--------|
| April 2026 | Initial threat model document |
