# Implementation Plan: Stronger Wallet PIN & Passphrase Protection

**Branch**: `feature/009-stronger-pin-protection` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-stronger-pin-protection/spec.md`

## Summary

Replace the direct PIN-to-AES-key derivation (PBKDF2-SHA256, 100,000 iterations) with Argon2id running in a browser worker, wrapped in a versioned, self-describing envelope. Add an optional passphrase as a second credential type. Migrate legacy wallets on the next successful unlock by writing the new envelope under a **new storage key** and leaving the legacy ciphertext untouched for 7 days (which is also what makes rollback to an older app version work). Close the biometric hole by deriving the biometric key from the WebAuthn PRF extension instead of storing it next to the ciphertext. Decisions and alternatives are in [research.md](research.md).

**Note**: SC-002 was amended from 1,000x to 100x for a GPU-class attacker because the original target was not reachable within SC-003 (3 s on a low-end phone); see "Decisions taken" and research R3. A passphrase is unaffected; this only changes how we describe PIN-only wallets.

## Technical Context

**Language/Version**: C# / .NET 10 (Blazor WebAssembly), vanilla JS for crypto and WebAuthn glue

**Primary Dependencies**: Argon2id WASM implementation vendored under `wwwroot/js/vendor/` (candidate: `hash-wasm`, MIT; exact version pinned and hash-recorded at implementation time). WebCrypto (AES-GCM, HKDF). WebAuthn PRF extension for the biometric key. No new NuGet packages.

**Storage**: Browser IndexedDB via `ISecureStorage` (existing). New keys only; no schema version bump of the database.

**Testing**: xUnit for `DigiByte.Wallet` (envelope codec, migration orchestration, throttle, credential policy) using fake `ICryptoService`; Node built-in test runner (`node --test`, no new dependencies) for the JS KDF and envelope round-trip against known-answer vectors; manual quickstart for UI.

**Target Platform**: Static PWA (`dgbwallet.app`), evergreen mobile and desktop browsers

**Project Type**: Multi-project monorepo; this feature touches the wallet PWA only (no backend)

**Performance Goals**: Unlock at most 3 s typical and 6 s worst case on the low-end reference phone; at most 1 s on a mid-range phone and desktop (SC-003). UI stays responsive (derivation off the main thread).

**Constraints**: CSP unchanged in principle (`'wasm-unsafe-eval'` is already present; the vendored script and worker are same-origin files); offline-capable (the service worker must cache the new assets); no network involvement in unlock or migration; no secrets in logs.

**Scale/Scope**: One crypto module, one codec, a migration step in `WalletService`, credential-type UI in 5 pages (Create, Unlock, PsbtSign, Recover/Reset, Settings), biometric re-enrolment, 2 locales, docs.

## Constitution Check

| Gate | Result |
|---|---|
| I. Self-custody | Pass. No key, seed, credential or derived key leaves the device; no escrow or recovery service. Passphrase loss is unrecoverable by the project and the UI says so. |
| II. In-browser crypto | Pass. Argon2id, AES-GCM and the PRF key derivation all run in the browser. No server role changes. |
| III. Test-first for Crypto/Wallet | Applies. Tests land in `DigiByte.Wallet.Tests` first and must fail before the change: envelope codec (truncated, tampered, unknown version, bad params), migration (interrupt at each step, mixed formats, concurrent), credential-type policy, throttle messages, failure classification. JS known-answer tests cover the KDF because it cannot run under xUnit. |
| IV. Boundary discipline | Pass. The envelope codec and migration live in `DigiByte.Wallet`; only the Argon2/WebCrypto/WebAuthn calls live in `DigiByte.Web` (JS + interop), as today. `DigiByte.Crypto` is untouched. |
| V. Resilience | Pass. No backend dependency. Devices without the required capability keep a labelled legacy mode instead of failing (FR-021). |
| Security & Custody: CSP | Pass with a check. Vendored script and worker are first-party files; the existing `'wasm-unsafe-eval'` covers WASM instantiation. Verified again in the quickstart. |

No violations; Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/009-stronger-pin-protection/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── envelope-format.md
│   ├── js-crypto-api.md
│   └── storage-keys.md
└── tasks.md             # created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Storage/
│   ├── EncryptionService.cs          # ICryptoService: typed-result decrypt + credential kind
│   ├── Envelope/EnvelopeCodec.cs     # NEW: parse/format "dgb2$" envelopes, header validation
│   ├── Envelope/KdfProfile.cs        # NEW: standard / low-memory / floor profiles
│   ├── WalletKeyStore.cs             # prefers v2 key, falls back to legacy; migration record
│   └── CredentialPolicy.cs           # NEW: passphrase min length, normalisation, common-list check
├── Models/WalletInfo.cs              # + CredentialType, + PassphraseNudgeDismissed
└── Services/WalletService.cs         # unlock + migrate-on-unlock, change credential, reset, verify

src/DigiByte.Web/
├── wwwroot/js/crypto.js              # versioned encrypt/decrypt, dispatch to worker
├── wwwroot/js/crypto-worker.js       # NEW: Argon2id in a dedicated worker
├── wwwroot/js/vendor/hash-wasm-argon2.umd.min.js  # NEW: vendored, pinned, hash recorded
├── wwwroot/js/webauthn.js            # PRF-based bio key, capability detection
├── wwwroot/data/common-passwords.txt # NEW: compact deny list, cached by the service worker
├── Services/JsCryptoService.cs       # typed results, failure classification
├── Services/BiometricService.cs      # PRF enrolment, one-time re-enrolment, warning flag
├── Services/PinAttemptThrottle.cs    # credential-kind-aware messages; environmental failures not counted
├── Components/                       # CredentialInput (PIN or passphrase), rejection reasons
├── Pages/ CreateWallet, Unlock, PsbtSign, Recover/Reset, Settings (upgrade card)
└── wwwroot/locales/{en,es}.json      # new strings; reword create.pin_limitation

tests/DigiByte.Wallet.Tests/          # envelope, migration, policy, throttle, biometric tests
tests/js/                             # NEW: node --test known-answer and round-trip tests
docs/SECURITY.md                      # update Cryptographic Primitives, Key Management, Known Limitations
```

**Structure Decision**: Keep the existing split: orchestration and format rules in `DigiByte.Wallet` (testable under xUnit), primitives in browser JS (necessarily). The new JS test folder runs with Node's built-in runner so no new package dependency is added.

## Decisions taken on the open items

1. **SC-002 amended** to at least 100 times higher cost per guess for a GPU-class attacker, estimated from the quickstart benchmark and published GPU rates. The original 1,000 times target was not reachable together with SC-003. If the benchmark shows less than 100 times, the parameters or the target are revisited before release.
2. **Older-version behaviour amended** (FR-012, US2 scenario 5): older shipped versions keep working from the untouched legacy copy for 7 days; after removal the user recovers with the recovery phrase. "Update required" applies to versions released after this feature.
3. **Still open, settled by the benchmark task**: exact Argon2id parameters, the reference low-end phone, and the common-password list size.
