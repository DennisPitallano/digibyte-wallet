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
│  │  │     SubtleCrypto (AES-256-GCM + PBKDF2)          │ │ │
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

### Encryption: AES-256-GCM with PBKDF2 Key Derivation

All sensitive data is encrypted before storage using the Web Crypto API (`SubtleCrypto`).

| Parameter | Value |
|-----------|-------|
| Algorithm | AES-256-GCM (authenticated encryption) |
| Key Derivation | PBKDF2-SHA256 |
| Iterations | 100,000 |
| Salt | 16 bytes (cryptographically random per encryption) |
| IV (nonce) | 12 bytes (cryptographically random per encryption) |
| Key Size | 256 bits |
| Authentication Tag | 128 bits (auto-appended by GCM) |

**Packed ciphertext format:**
```
[salt: 16 bytes] [iv: 12 bytes] [ciphertext + auth tag]
→ Base64-encoded for storage
```

**Security properties:**
- **Non-deterministic**: Random salt + IV per encryption means identical plaintexts produce different ciphertexts
- **Authenticated**: GCM provides integrity — tampered ciphertext fails decryption
- **Key stretching**: 100,000 PBKDF2 iterations slow brute-force
- **No key reuse**: Each encryption generates fresh salt → fresh derived key

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
    ├── wallet_seed_{walletId}         → AES-256-GCM encrypted mnemonic/WIF/xpub (Base64)
    ├── wallet_info_{walletId}         → JSON metadata (name, type, network, creation date)
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
| Mnemonic seed phrase | ✅ AES-256-GCM | Derived from PIN via PBKDF2 |
| WIF private key | ✅ AES-256-GCM | Derived from PIN via PBKDF2 |
| Extended public key (xpub) | ✅ AES-256-GCM | Derived from PIN via PBKDF2 |
| Wallet metadata | ❌ Plaintext JSON | — |
| Multisig config (redeem script) | ❌ Plaintext JSON | — |
| Contacts | ❌ Plaintext JSON | — |
| Preferences | ❌ Plaintext JSON | — |

**Rationale for unencrypted fields:** Multisig configs, contacts, and preferences contain no secret material. Redeem scripts are public (shared with all co-signers). Encrypting them would require PIN entry for every metadata read, degrading UX without security benefit.

### Key Lifecycle

```
CREATE:  Mnemonic generated → encrypted with PIN → stored in IndexedDB
UNLOCK:  PIN entered → PBKDF2 derives key → AES-GCM decrypt → keys held in WASM memory
LOCK:    Keys zeroed from memory → requires PIN to unlock again
DELETE:  IndexedDB.clear() → all data wiped (irreversible)
```

### Memory Security

- Private keys exist in WASM linear memory only while the wallet is unlocked
- Locking the wallet (`Lock()`) sets key references to `null`
- .NET garbage collector will eventually reclaim the memory
- **Limitation**: No explicit memory zeroing (WASM does not expose `SecureString` or `RtlZeroMemory`)
- **Limitation**: Browser memory dumps (dev tools, crash reports) may contain key material while unlocked

---

## Authentication & Access Control

### PIN Lockout with Exponential Backoff

The wallet uses a 6-digit PIN for authentication. Failed attempts trigger a progressive lockout.

**Implementation:** Client-side enforcement in `Unlock.razor`

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
- With lockout: ~1,000,000 attempts × average 2 min delay = ~3.8 years
- **BUT**: Lockout is client-side (in-memory). Attacker with device access can:
  - Clear browser localStorage
  - Extract IndexedDB ciphertext directly
  - Attempt offline brute-force against AES-256-GCM ciphertext

**Offline brute-force against extracted ciphertext:**
- 100,000 PBKDF2 iterations with 6-digit PIN (10^6 space)
- On modern GPU: ~1,000 PBKDF2-SHA256 ops/sec per core
- Full keyspace: ~1,000 seconds ≈ **17 minutes** on a single GPU
- **This is the primary risk** — see [Recommendations](#recommendations-for-future-hardening)

### Recovery Flow

- Users can reset their PIN by entering their 24-word mnemonic seed phrase
- The seed is re-encrypted with the new PIN
- No server interaction required

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
                                      → GitHub API (releases)
```

**Benefits:**
- Avoids exposing API keys in client-side code
- Prevents CORS issues with third-party APIs
- Server-side caching (60s TTL) reduces external API load
- Rate limiting absorbed by server, not end users

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

```html
<meta http-equiv="X-Content-Type-Options" content="nosniff" />
<meta http-equiv="Referrer-Policy" content="no-referrer" />
```

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

### PIN Security

| Limitation | Impact |
|-----------|--------|
| 6-digit keyspace (10^6) | Brutable offline against extracted ciphertext (~17 min on GPU) |
| Client-side lockout only | Attacker with device access can bypass timer |
| No server-side enforcement | No second factor; no remote wipe |

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
