# Research: Stronger Wallet PIN & Passphrase Protection

## Current state (verified in the repo)

- `src/DigiByte.Web/wwwroot/js/crypto.js`: PBKDF2-SHA256, 100,000 iterations, AES-256-GCM, packed as `salt(16) | iv(12) | ciphertext+tag`, Base64. No version marker, no parameters stored, no AAD.
- Every secret goes through `ICryptoService` (`EncryptAsync` / `DecryptAsync` taking the PIN string). Callers: `WalletService`, `MultisigWalletService`, `DigiDollarStore`. `DecryptAsync` returns `null` for wrong PIN, tampering and any other failure alike.
- Seeds live under `wallet_seed_{id}` (`WalletKeyStore`); `WalletInfo` is plaintext JSON metadata.
- Biometrics (`BiometricService`, `webauthn.js`): `enroll` creates a WebAuthn credential but derives nothing from it; `bioKey` is `crypto.getRandomValues(32)`, saved in storage as `bio_key`, and each wallet's seed copy is saved as `bio_seed_{id}`. The WebAuthn prompt is therefore a UI gate only; copied browser data contains both the key and the ciphertext.
- `PinAttemptThrottle`: 3 free attempts, then 15 s doubling up to 5 doublings; per page instance; messages hard-code "PIN".
- CSP already allows `'wasm-unsafe-eval'`; scripts are same-origin files with per-build hashes injected for inline scripts; `default-src 'self'` covers same-origin workers.
- Service worker caches `.js`, `.wasm`, `.json`, `.dll` etc.; a new `.txt` data file needs an `offlineAssetsInclude` entry.

## R1: Where does Argon2id run, and with what library?

**Decision**: A vendored, version-pinned WASM Argon2id (candidate `hash-wasm`, MIT licence, small, no threads required) loaded by a dedicated Web Worker. The file and its SHA-256 are committed under `wwwroot/js/vendor/`, with the licence text.

**Rationale**: Runs in the browser (constitution II), same-origin (CSP), works offline, and the worker keeps the UI responsive (FR-020). Single-lane Argon2id (parallelism 1) works everywhere; threaded WASM would need cross-origin isolation headers the app does not set.

**Alternatives considered**:
- *Managed Argon2 in .NET (for example Konscious) inside Blazor WASM*: would allow xUnit tests with the real KDF, but runs on the main thread, without SIMD, and is several times slower; it fails SC-003 on low-end phones.
- *libsodium.js `crypto_pwhash`*: sound but much larger; overkill for one function.
- *scrypt or higher PBKDF2 iterations*: scrypt is memory-hard but weaker than Argon2id's guidance; PBKDF2 is not memory-hard and does not address the GPU gap.
- *Loading from a CDN*: forbidden by the constitution's CSP rule.

**To verify at implementation**: exact version, that its WASM instantiates under the existing CSP, and licence compatibility. If it does not, the fallback is a different vendored build, not a CSP loosening.

## R2: Envelope format and legacy detection

**Decision**: Text envelope with a literal prefix, `dgb2$<base64url(header | salt | nonce | ciphertext+tag)>`. The header (version, KDF id, memory KiB, passes, lanes, credential kind) is bound as AES-GCM additional authenticated data. Legacy values are plain Base64 and can never contain `$`, so detection is exact and cannot be confused by random salt bytes. Full layout in [contracts/envelope-format.md](contracts/envelope-format.md).

**Rationale**: Self-describing (FR-007), forward-extensible (unknown version is detected and refused untouched, FR-009), and tamper-resistant: changing the stored parameters (a downgrade attack) makes authentication fail. Credential kind in the header lets the UI prompt correctly even if metadata is lost.

**Alternatives**: a binary magic number (can collide with the first bytes of a random legacy salt); JSON envelope (larger, needs canonicalisation for AAD); a separate metadata record for parameters (can be tampered or lost independently of the ciphertext).

## R3: KDF parameters and what they buy

**Decision**: Three named profiles, all Argon2id, parallelism 1, 32-byte output, stored in each envelope:

| Profile | Memory | Passes | Use |
|---|---|---|---|
| standard | 64 MiB | 3 | Default |
| low-memory | 32 MiB | 3 | Used when standard cannot allocate |
| floor | 19 MiB | 2 | Minimum, matches the OWASP minimum configuration; below this nothing is written |

Final numbers come from the benchmark task on the reference phone and are then frozen per profile; the table is a starting point, not a measured result.

**Honest effect on a six-digit PIN**: only one million candidates exist. On CPU, Argon2id at these settings costs a few times more per guess than PBKDF2 at 100,000 iterations. The much larger gap is against GPU or ASIC attackers, where memory cost dominates (order of hundreds of times, to be measured). That is a real improvement but not 1,000x, and it still means a PIN-only wallet is guessable with effort. Passphrases are the actual fix. This is why the spec nudges toward a passphrase and why wording must not oversell PIN protection. See Open item 1 in the plan.

**First measurement (T047 helper, `dgbCrypto.bench()`, one development desktop, Chromium, median of 3)**: standard (64 MiB, 3 passes) 322 ms; low-memory (32 MiB, 3 passes) 188 ms; floor (19 MiB, 2 passes) 75 ms; legacy PBKDF2 (100,000 iterations) 18 ms. So the CPU cost per guess is roughly 18 times the legacy cost at the standard profile on this machine. This is one desktop only: the low-end phone, a mid-range phone and the GPU-class comparison that SC-002 and SC-003 need are still to be measured (T001), so the profiles stay provisional.

**Alternatives**: higher memory (128 MiB or more) improves the GPU ratio but fails or crashes on low-end phones; more passes raise time linearly on the phone but only help the attacker ratio linearly too.

## R4: Migration, rollback, and storage layout

**Decision**: New key `wallet_seed_v2_{id}` holds the new envelope. The legacy `wallet_seed_{id}` is **not modified** at migration. A per-wallet record `wallet_migration_{id}` stores `{ migratedAt, legacyFingerprint }`.

Migrate-on-unlock steps (each step crash-safe):
1. Unlock succeeds against the legacy copy.
2. Take a cross-tab lock for the wallet (Web Locks API; if unavailable, a storage-based lease).
3. Encrypt the same plaintext as a new envelope, write it to `wallet_seed_v2_{id}`.
4. Read it back and decrypt with the same credential; compare to the plaintext. If different or it throws, delete `v2` and stop (legacy remains the source of truth, retry next unlock).
5. Write the migration record with the legacy ciphertext's SHA-256 as `legacyFingerprint`.
6. Release the lock.

Reading: prefer `v2`; if the stored `legacyFingerprint` no longer matches the current legacy value (an older app version was used after rollback and changed the PIN), treat legacy as authoritative and migrate again.

Cleanup: on any successful unlock via `v2`, if `now - migratedAt >= 7 days`, delete the legacy key, the record keeps a `legacyRemovedAt` stamp. Changing or resetting the credential deletes the legacy copy immediately, because it is protected by the old weak credential. The upgrade notice states all of this.

**Rationale**: Leaving the legacy value in place is what makes rollback real for versions that cannot be updated: an older build reads `wallet_seed_{id}` as it always did and works. It also makes steps 3 to 5 trivially recoverable. The cost is the 7-day window in which the weak copy exists (accepted in clarification).

**Alternatives**: overwrite in place (breaks older builds, a crash window); keep both for 30 days (rejected in clarification); delete legacy immediately after verification (no rollback).

## R5: Biometric key (closing the stored-key hole)

**Decision**: Derive the biometric key from the WebAuthn PRF extension (hmac-secret): at enrolment request `prf` with a fixed per-wallet-set salt and check `getClientExtensionResults().prf.enabled`; at unlock evaluate PRF during the assertion, HKDF-SHA256 the output to an AES key, and never store the key. `bio_key` is deleted once re-enrolled. Where PRF is unavailable, keep the old behaviour but show the warning and let the user decline (FR-018b).

Existing enrolments cannot be upgraded silently: a credential created without the extension generally cannot evaluate PRF later. So the upgrade is a one-time re-enrolment prompt after the next credential unlock, which re-creates the credential with PRF, re-encrypts each `bio_seed_{id}`, then deletes `bio_key`. If the user declines or the browser lacks PRF, the warning is shown once.

**Rationale**: It is the only browser-native way to bind a key to the authenticator's user verification so that copied storage is not enough. Support is partial and varies by browser, OS and authenticator, hence capability detection rather than assumptions.

**Alternatives**: removing the bio seed copy (strict option, rejected in clarification); wrapping `bio_key` with the credential (the same stored-key problem); a WebAuthn signature as a gate only (no cryptographic binding).

## R6: Passphrase rules and normalisation

**Decision**: Minimum 12 characters after normalisation (so deny-list entries shorter than 12 characters are redundant and are left out; the list holds common passwords and phrases of 12 or more characters; as built in T032 it has 2,268 entries from three SecLists 100,000-entry lists, MIT licence, because that is all the qualifying entries those lists contain), no maximum below a generous cap of 256; Unicode NFKC normalisation before UTF-8 encoding; reject entries on a compact common-password list (target about 10,000 lowercased entries, loaded lazily and cached by the service worker) and trivially repeated or sequential strings. PIN stays six digits, passed through unchanged. The common list is a deny list, not a strength meter, and the UI says "not accepted" with a reason rather than scoring.

**Rationale**: Predictable behaviour on every device (FR-006) and a defence against the most common choices at the cost of a small static file. The final list size is settled when the file is chosen.

**Alternatives**: a full strength estimator library (large, scoring can mislead); length-only (accepts "passwordpassword").

## R7: Throttle, failure classification, and credential kind

**Decision**: `DecryptAsync` is joined by a typed result: `Success`, `WrongCredential` (authentication tag failed), `Tampered/Unreadable`, `UnsupportedVersion`, `ResourceExhausted` (allocation failed or worker error). Only `WrongCredential` counts toward the throttle (FR-017). `PinAttemptThrottle` gets a credential-kind parameter so messages say "PIN" or "passphrase" and thresholds stay identical (FR-016, SC-007). The throttle remains per page instance as documented; no change to that limit.

**Alternatives**: counting every failure (punishes users on low-memory phones); persisting the throttle (out of scope, recorded in spec).

## R8: Concurrency across tabs and performance protections

**Decision**: Web Locks per wallet for migration; one in-flight derivation per page (buttons disabled while deriving); the worker is terminated after use so 64 MiB is released; a progress state is shown for any derivation over 300 ms.

## R9: Testing approach

**Decision**: Test-first. xUnit covers everything that is pure C# with fake `ICryptoService`: envelope codec, migration state machine with fault injection at each step, mixed formats, fingerprint mismatch after rollback, credential policy, throttle, failure classification, biometric flag handling. `node --test` covers the JS: Argon2id known-answer vectors, envelope round-trip, tamper/AAD, legacy decrypt of a committed fixture, passphrase normalisation. UI flows are exercised by the quickstart scenarios. A benchmark script records unlock time and memory per profile for the reference phone.

**Alternatives**: running Argon2 in .NET only to make everything xUnit-testable (rejected in R1).

## Quickstart run (T051), partial

Run on 2026-11-09 against a Debug build of the feature branch (`dotnet run`, Chromium in the Claude desktop browser pane), test wallet only.

| Scenario | Result |
|---|---|
| 1. New passphrase wallet | Pass. `password1234` rejected with a reason; a valid passphrase accepted; IndexedDB held only `wallet_seed_v2_{id}` starting `dgb2$` (header decodes to the standard profile, 64 MiB). Reload and unlock with the passphrase worked. |
| 7. Throttle wording (wrong credential only) | Pass for the wrong-passphrase message in English and Spanish ("2 attempts remaining before lockout"). The allocation-failure hook was not exercised. |
| 12. Wording | Credential strings translate in Spanish on Unlock and Create. Not every changed screen was visited. Unlock still shows some pre-existing hard-coded English ("Welcome back", "Unlock", "Good to see you"). |
| 13. CSP | Not conclusive. A Debug build reports one inline-script violation and cannot register the service worker, because CSP hashes and the service worker are produced at publish time. Needs a published build. |
| 2, 3, 4, 5, 6, 8, 9, 10, 11 | Not run. They need the previous build's profile, test hooks, a second browser, a WebAssembly-disabled browser, a real phone, or a published build for offline. |

Frozen profile parameters: not yet; they depend on the T001 benchmark.
