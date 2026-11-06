# Tasks: Stronger Wallet PIN & Passphrase Protection

**Input**: Design documents from `/specs/009-stronger-pin-protection/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included and written first. The constitution (III, test-first for Crypto and Wallet) requires tests that fail before the change, with negative paths. xUnit covers C#; `node --test` covers the browser JS.

**Organization**: Grouped by user story. Foundational work blocks all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US6 map to the stories in spec.md
- Paths are relative to the repository root (the worktree at `C:\GitHub\digibyte-wallet-009`)

---

## Phase 1: Setup

- [ ] T001 Run the benchmark spike and record results in `specs/009-stronger-pin-protection/research.md` under R3: Argon2id derivation time and peak memory for 64 MiB/3 passes, 32 MiB/3 passes and 19 MiB/2 passes on the reference low-end phone, a mid-range phone and desktop, plus legacy PBKDF2 per-guess cost; choose and freeze the three profile values, pick the reference phone, and check SC-002 (at least 100x for a GPU-class attacker; the benchmark measures phone and desktop CPU time, so derive the GPU figure by comparing memory cost with published GPU rates for PBKDF2-SHA256 and Argon2id and record the source and assumptions) and SC-003 (3 s typical, 6 s worst case). Stop and report if SC-002 or SC-003 cannot be met.
- [ ] T002 [P] Vendor the pinned Argon2id WASM build under `src/DigiByte.Web/wwwroot/js/vendor/` with its licence text and a recorded SHA-256 in `src/DigiByte.Web/wwwroot/js/vendor/README.md`; confirm it instantiates under the existing CSP (`'wasm-unsafe-eval'`) with no policy change.
- [ ] T003 [P] Add `tests/js/` with a `node --test` runner entry (`"test": "node --test tests/js"` in `src/DigiByte.Web/package.json`, no new dependencies) and shared fixtures folder `tests/js/fixtures/`.
- [ ] T004 [P] Generate fixtures once from the current code and commit them to both `tests/js/fixtures/` and `tests/DigiByte.Wallet.Tests/Fixtures/`: one legacy ciphertext (known PIN and plaintext), one `dgb2` value per profile, and tampered variants (flipped header bit, flipped ciphertext bit, truncated, unknown version `dgb3$`, below-floor parameters).
- [ ] T005 [P] Add `.github/workflows` job (or extend the existing wallet CI if present) to run `dotnet test tests/DigiByte.Wallet.Tests` and `node --test tests/js`.

## Phase 2: Foundational (blocks every user story)

### Tests first (must fail before implementation)

- [ ] T006 [P] Write `tests/DigiByte.Wallet.Tests/EnvelopeCodecTests.cs`: parse and format round trip; reject truncated input, unknown prefix `dgb3$`, unknown kdf id, kind not in {1, 2}, memory below 19,456 KiB, passes below 2, lanes other than 1; legacy value detected as legacy; header bytes exposed for use as additional authenticated data (per [contracts/envelope-format.md](contracts/envelope-format.md)).
- [ ] T007 [P] Write `tests/js/crypto.test.mjs`: Argon2id known-answer vectors; `encryptV2` then `decryptV2` round trip for each profile; wrong credential returns `WrongCredential`; flipped header bit fails authentication (parameter downgrade is detected); legacy fixture decrypts via `decryptV2`; `ResourceExhausted` when the worker cannot allocate (stubbed); nothing is written below the floor.
- [ ] T008 [P] Write `tests/DigiByte.Wallet.Tests/CryptoResultTests.cs`: a fake `ICryptoService` returns each `CryptoFailure` and the wrappers map `WrongCredential` to false/null and surface the others distinctly.

### Implementation

- [ ] T009 Create `src/DigiByte.Wallet/Storage/Envelope/KdfProfile.cs` with the three frozen profiles from T001 and the floor constant (19,456 KiB, 2 passes, 1 lane).
- [ ] T010 Create `src/DigiByte.Wallet/Storage/Envelope/EnvelopeCodec.cs` implementing parse, format and validation exactly as in `contracts/envelope-format.md`; make T006 pass.
- [ ] T011 Add `CredentialKind` (`Pin`, `Passphrase`), `CryptoFailure` and `CryptoResult<T>` to `src/DigiByte.Wallet/Storage/EncryptionService.cs`; extend `ICryptoService` with typed `EncryptAsync(byte[], string, CredentialKind)` and `DecryptAsync(string, string)` per `contracts/js-crypto-api.md`; keep the old byte-array methods as thin wrappers until T052.
- [ ] T012 Create `src/DigiByte.Web/wwwroot/js/crypto-worker.js` running Argon2id with the vendored build, per the worker protocol in `contracts/js-crypto-api.md`; terminate after each use.
- [ ] T013 Update `src/DigiByte.Web/wwwroot/js/crypto.js`: add `encryptV2`, `decryptV2`, `capabilities`, `normalisePassphrase` (NFKC); keep legacy PBKDF2 decrypt; automatic profile fallback down to the floor, never below; classify failures; make T007 pass.
- [ ] T014 Update `src/DigiByte.Web/Services/JsCryptoService.cs` to implement the typed `ICryptoService` (T011) over `dgbCrypto.encryptV2` / `decryptV2`; make T008 pass.
- [ ] T015 [P] Add `.txt` handling for the new data file in `src/DigiByte.Web/wwwroot/service-worker.published.js` (`offlineAssetsInclude`) and confirm the worker, vendor script and `wwwroot/data/` file are in the offline manifest.
- [ ] T016 [P] Extend `WalletInfo` in `src/DigiByte.Wallet/Models/WalletInfo.cs` with `CredentialType` (default `pin`), `ProtectionLevel` (`standard` or `legacy`, default `standard`) and `PassphraseNudgeDismissed` (default false); confirm older JSON without the fields still loads (add a test in `tests/DigiByte.Wallet.Tests/`).

**Checkpoint**: typed encryption works end to end; legacy values still decrypt.

---

## Phase 3: User Story 2 - Existing wallets upgrade automatically and safely (P1)

**Goal**: A legacy PIN wallet is re-protected with the new scheme on the next successful unlock, with no data loss and a working rollback.

**Independent Test**: Quickstart scenarios 2 to 5.

### Tests first

- [ ] T017 [P] [US2] Write `tests/DigiByte.Wallet.Tests/WalletMigrationTests.cs`: legacy-only wallet unlocks and produces `wallet_seed_v2_{id}` plus `wallet_migration_{id}`; legacy key byte-identical afterwards; verification mismatch deletes `v2` and leaves legacy usable; fault injected (including a storage-quota error on the write) after each of the five steps in research R4 leaves the wallet unlockable and retries on next unlock; each wallet migrates independently with mixed formats; all wallet types (HD, WIF, xpub) covered.
- [ ] T018 [P] [US2] Write `tests/DigiByte.Wallet.Tests/WalletMigrationConcurrencyTests.cs`: two simultaneous unlocks of one wallet produce one valid `v2` value and never a wallet that cannot be unlocked (lock abstraction faked).
- [ ] T019 [P] [US2] Write `tests/DigiByte.Wallet.Tests/WalletRollbackTests.cs`: legacy fingerprint mismatch (legacy changed by an older version after migration) makes legacy authoritative and re-migrates; legacy copy removed on the first successful `v2` unlock at or after 7 days and not before; credential change and reset delete the legacy copy immediately; `legacyRemovedAt` stamped.

### Implementation

- [ ] T020 [US2] Add migration-record and v2-key handling to `src/DigiByte.Wallet/Storage/WalletKeyStore.cs` per `contracts/storage-keys.md` (read order, fingerprint check, record read/write, legacy delete); make the storage parts of T017 and T019 pass.
- [ ] T021 [US2] Implement migrate-on-unlock in `src/DigiByte.Wallet/Services/WalletService.cs` (`UnlockWalletAsync`, `VerifyPinAsync`, `GetSeedPhraseAsync`, `GetDecryptedSeedAsync`): five verified steps, injectable clock and lock; migration failure is silent to the user and never blocks the unlock; make T017 to T019 pass.
- [ ] T022 [US2] Add a per-wallet lock abstraction (`IMigrationLock`) in `src/DigiByte.Wallet/Services/` with a Web Locks implementation in `src/DigiByte.Web/Services/WebLockMigrationLock.cs` and a storage-lease fallback when Web Locks is unavailable.
- [ ] T023 [P] [US2] Apply the same v2 read order and migration to multisig config and DigiDollar store payloads in `src/DigiByte.Wallet/Services/MultisigWalletService.cs` and `src/DigiByte.Wallet/Storage/DigiDollarStore.cs`, with tests in `tests/DigiByte.Wallet.Tests/MultisigWalletServiceTests.cs` and `DigiDollarStoreTests.cs`.
- [ ] T024 [US2] Add the one-time upgrade notice (shown after a successful migration) in `src/DigiByte.Web/Pages/Unlock.razor` stating the old copy stays for 7 days and what that means; strings added in T049.

**Checkpoint**: existing users are protected by the new derivation with no extra steps (SC-001, SC-004, SC-009, SC-012).

---

## Phase 4: User Story 3 - Recovery from the seed phrase keeps working (P1)

**Goal**: Recovery works for legacy, migrated, PIN and passphrase wallets and never changes anything on failure.

**Independent Test**: Quickstart scenario 6.

- [ ] T025 [P] [US3] Write `tests/DigiByte.Wallet.Tests/WalletRecoveryTests.cs`: reset with the correct phrase re-protects under the new credential as `v2` and deletes the legacy copy and any migration record's legacy flag; old credential stops working; invalid or malformed phrase changes nothing in storage (assert full storage snapshot equality); passphrase wallet can switch to a PIN and back; watch-only and imported-key wallets recover by re-import.
- [ ] T026 [US3] Update `ResetPinAsync` in `src/DigiByte.Wallet/Services/WalletService.cs` (and the equivalent reset paths around lines 1868 to 1912) to write `v2`, remove the legacy copy, accept either credential kind, and make no storage writes before the phrase is validated; make T025 pass.

---

## Phase 5: User Story 1 - Passphrase protection (P1)

**Goal**: Users can choose a passphrase at creation or upgrade later in settings.

**Independent Test**: Quickstart scenario 1; spec US1 scenarios 1 to 4.

### Tests first

- [ ] T028 [P] [US1] Write `tests/DigiByte.Wallet.Tests/CredentialPolicyTests.cs`: minimum 12 characters after NFKC; reject common-list entries, trivially repeated and sequential strings, over 256 characters; accept valid; composed and decomposed forms of the same text are equal; leading and trailing spaces are preserved as typed; each rejection returns a specific reason code.
- [ ] T029 [P] [US1] Write `tests/DigiByte.Wallet.Tests/CredentialChangeTests.cs`: PIN to passphrase requires the current PIN first, writes `v2` with kind `passphrase`, deletes legacy immediately, and the PIN alone no longer unlocks; wrong current PIN changes nothing.
- [ ] T030 [P] [US1] Write `tests/js/passphrase.test.mjs`: NFKC normalisation parity between JS and the C# policy fixtures; same visible text from different Unicode forms derives the same key.
- [ ] T058 [P] [US1] Write `tests/DigiByte.Wallet.Tests/UpgradeNudgeTests.cs`: dismissing the passphrase upgrade card persists `PassphraseNudgeDismissed`, the card does not reappear on later unlocks or sessions, and reopening from settings shows it again (SC-010); no balance-based or recurring trigger exists.

### Implementation

- [ ] T031 [US1] Create `src/DigiByte.Wallet/Storage/CredentialPolicy.cs` (rules and reason codes; deny list injected through an interface) and `src/DigiByte.Web/Services/CommonPasswordList.cs` loading `wwwroot/data/common-passwords.txt` lazily; make T028 pass.
- [ ] T032 [P] [US1] Add `src/DigiByte.Web/wwwroot/data/common-passwords.txt` (compact lowercased deny list of about 10,000 entries; the final size is chosen in this task and recorded in `research.md` R6) with its source and licence noted in `src/DigiByte.Web/wwwroot/data/README.md`.
- [ ] T033 [US1] Create `src/DigiByte.Web/Components/CredentialInput.razor` (six-digit PIN input or masked passphrase field with show/hide, rejection reason text, no autocomplete, `data-testid` attributes) and use it in the pages below.
- [ ] T034 [US1] Update `src/DigiByte.Web/Pages/CreateWallet.razor`: choice between PIN and passphrase with a short honest explanation, passphrase recommended for larger holdings, confirm field, rejection reasons shown before anything is stored; creation uses `CredentialKind` and writes `v2` only; make the Wallet service calls in `CreateWalletAsync`, `CreateWalletFromWifAsync`, `CreateWatchOnlyWalletAsync` take the kind.
- [ ] T035 [US1] Add `ChangeCredentialAsync` to `src/DigiByte.Wallet/Services/WalletService.cs` (PIN to passphrase and passphrase to PIN, current credential required, legacy copy deleted, biometric flags handled in T041); make T029 pass.
- [ ] T036 [US1] Add the "Use a passphrase" upgrade card with dismiss to the wallet settings page in `src/DigiByte.Web/Pages/Settings.razor`, persisting `PassphraseNudgeDismissed`; no balance-triggered or recurring reminders (SC-010).
- [ ] T037 [US1] Update `src/DigiByte.Web/Pages/Unlock.razor` and `src/DigiByte.Web/Pages/PsbtSign.razor` to show the PIN input or the passphrase input according to `WalletInfo.CredentialType`.
- [ ] T027 [US3] Update the reset UI in `src/DigiByte.Web/Pages/Unlock.razor` and `src/DigiByte.Web/Pages/RecoverWallet.razor` to choose the new credential type during recovery (uses the component from T033, so this task runs after T033 and is placed in the US1 phase).

---

## Phase 6: User Story 5 - Lockout and biometrics (P2)

**Goal**: Throttle behaves the same for both credential types, environmental failures are not counted, and biometrics no longer leaves a key next to its ciphertext where the browser supports PRF.

**Independent Test**: Quickstart scenarios 7 and 8.

### Tests first

- [ ] T038 [P] [US5] Extend `tests/DigiByte.Wallet.Tests/` with `PinAttemptThrottleTests.cs`: identical thresholds and delays for PIN and passphrase; messages say "PIN" or "passphrase"; only `WrongCredential` increments; `ResourceExhausted` and `UnsupportedVersion` do not; reset on success.
- [ ] T039 [P] [US5] Extend `tests/DigiByte.Wallet.Tests/BiometricServiceTests.cs`: PRF enrolment stores no `bio_key`; migration of a wallet with biometrics enrolled keeps biometric unlock working; one-time re-enrolment prompt state (`bio_prf_state`); non-PRF path sets the weak warning once and respects a decline; credential-gated actions (view phrase, change credential, enrol biometrics) still require the credential.
- [ ] T040 [P] [US5] Write `tests/js/webauthn-prf.test.mjs` with a stubbed `navigator.credentials`: PRF enabled result yields a derived key never persisted; PRF missing yields `prfSupported: false`.

### Implementation

- [ ] T041 [US5] Update `src/DigiByte.Web/Services/PinAttemptThrottle.cs` to take a credential kind and a failure classification (only wrong-credential counts); update `src/DigiByte.Web/Pages/Unlock.razor` and `src/DigiByte.Web/Pages/PsbtSign.razor` call sites; make T038 pass.
- [ ] T042 [US5] Update `src/DigiByte.Web/wwwroot/js/webauthn.js` with `enrollPrf` and `deriveKeyPrf` (HKDF-SHA256 over the PRF output, never persisted) per `contracts/js-crypto-api.md`; make T040 pass.
- [ ] T043 [US5] Update `src/DigiByte.Web/Services/BiometricService.cs`: PRF enrolment, re-encrypting each `bio_seed_{id}` under the derived key, removing `bio_key` after success, one-time re-enrolment prompt after the next credential unlock, weak-path warning with decline, `bio_prf_state` and `bio_weak_warned` flags; make T039 pass. Credential change in T035 must keep biometric state consistent.

---

## Phase 7: User Story 4 - Unlock performance on low-end phones (P2)

**Goal**: Unlock stays responsive and within SC-003 without silently weakening protection.

**Independent Test**: Quickstart scenarios 9 and 10.

- [ ] T044 [P] [US4] Write `tests/DigiByte.Wallet.Tests/UnsupportedDeviceTests.cs`: new wallet on a device reporting no Argon2 support requires an explicit agreement flag, is marked `legacy` protection (persisted in `WalletInfo.ProtectionLevel`), and upgrades to `v2` on the first unlock where capabilities allow; migration on a device that cannot allocate even the floor leaves the wallet in its current format and shows the explanation; no write below the floor under any path.
- [ ] T045 [US4] Implement the unsupported-device flow in `src/DigiByte.Wallet/Services/WalletService.cs` and the warning-and-agreement step in `src/DigiByte.Web/Pages/CreateWallet.razor` (FR-021, SC-013).
- [ ] T046 [US4] Add the unlocking indicator (shown when derivation exceeds 300 ms) and double-submit protection to `src/DigiByte.Web/Pages/Unlock.razor`, `src/DigiByte.Web/Pages/PsbtSign.razor` and `src/DigiByte.Web/Pages/CreateWallet.razor`; keep the existing Baku behaviour on the unlock page unchanged.
- [ ] T047 [US4] Add a `?crypto-bench` developer page or console helper in `src/DigiByte.Web/wwwroot/js/crypto.js` (disabled in release builds) that prints derive time and memory per profile, used by T001 and the quickstart benchmark.

---

## Phase 8: User Story 6 - Honest wording (P3)

**Goal**: Accurate English and Spanish text everywhere the protection is described.

**Independent Test**: Quickstart scenario 12.

- [ ] T048 [P] [US6] Write `tests/DigiByte.Wallet.Tests/LocaleParityTests.cs` (or extend the existing locale test if one exists): every key added by this feature exists in both `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`, no value is empty or identical to English in `es.json` unless allow-listed.
- [ ] T049 [US6] Add and reword strings in `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`: choice PIN versus passphrase, passphrase rules and rejection reasons, unrecoverable-passphrase warning, 7-day upgrade notice, weak-biometric warning, re-enrolment prompt, unsupported-browser agreement, unlock progress text; reword `create.pin_limitation` to say a PIN is stronger than before but still guessable with effort and a passphrase is the real protection (shown in `src/DigiByte.Web/Pages/CreateWallet.razor` line 131); make T048 pass.
- [ ] T050 [P] [US6] Update `docs/SECURITY.md`: Cryptographic Primitives (Argon2id, envelope, AAD), Key Management and Storage (new keys, 7-day legacy copy, PRF biometrics), Known Limitations (PIN-only still guessable, throttle per page, weak-biometric path, 7-day window), and the implementation-versus-doc note for this finding (DGB-04).
- [ ] T056 [US6] Show an "update required" message (no data changed, no throttle count) when a stored value has a format version newer than this build understands (FR-009), in `src/DigiByte.Web/Pages/Unlock.razor` and `src/DigiByte.Web/Pages/PsbtSign.razor`, with a matching string in both locale files; extend `tests/DigiByte.Wallet.Tests/CryptoResultTests.cs` to assert `UnsupportedVersion` maps to this message and not to a wrong-credential error.

---

## Phase 9: Polish & cross-cutting

- [ ] T051 Run the full quickstart in `specs/009-stronger-pin-protection/quickstart.md` including the offline and CSP scenarios; record results and the frozen parameters in `research.md`.
- [ ] T052 Remove the transitional byte-array `ICryptoService` wrappers and any unused legacy-write code from `src/DigiByte.Wallet/Storage/EncryptionService.cs` and `src/DigiByte.Web/Services/JsCryptoService.cs`; keep legacy read.
- [ ] T053 [P] Audit for secrets in logs and telemetry: grep for credential and key handling in `src/DigiByte.Web/` and confirm no console, error report or analytics path includes a credential, derived key or plaintext (FR-005); add a short note to `docs/SECURITY.md`.
- [ ] T054 [P] Update What's New in the version info (as done for 0.4.0) in the web app and `src/DigiByte.Web/ANNOUNCEMENT.md`, describing the automatic upgrade and the passphrase option.
- [ ] T057 [P] Run a small usability check for SC-006 (at least 5 people, mix of English and Spanish): create a passphrase wallet, lock, unlock, and state what happens if the credential is forgotten; record results in `specs/009-stronger-pin-protection/research.md`. If this cannot be run before release, mark SC-006 as a post-release measure in `spec.md`.
- [ ] T055 Run `dotnet test` for the whole solution and `node --test tests/js`; confirm every new test was seen failing before its implementation task (constitution III).

---

## Dependencies & execution order

- **Phase 1** can start immediately; T001 (benchmark) gates T009 and the final parameters. T002 to T005 are parallel.
- **Phase 2** blocks everything else. Within it: tests T006 to T008 first, then T009 to T016.
- **US2 (Phase 3)** needs Phase 2 only. It delivers protection to every existing wallet and is the recommended first slice.
- **US3 (Phase 4)** needs Phase 2 and the `v2` handling from T020/T021; its UI step T027 sits in the US1 phase because it needs the component from T033.
- **US1 (Phase 5)** needs Phase 2; T034 to T037 need T031 and T033.
- **US5 (Phase 6)** needs Phase 2; T043 needs T035 for the credential-change interaction.
- **US4 (Phase 7)** needs Phase 2 and T021.
- **US6 (Phase 8)** strings are used by several UI tasks; add keys as UI tasks land, finish in this phase.
- **Phase 9** last.

```text
Setup ─► Foundational ─┬─► US2 ─► US3 ─┐
                       ├─► US1 ────────┼─► US5 ─► US4 ─► US6 ─► Polish
                       └───────────────┘
```

## Parallel examples

- After Phase 2: US2 (T017 to T019 tests together) and US1 (T028 to T030 tests together) can proceed in parallel by different people, since they touch different test files; they meet in `WalletService.cs`, so implementation tasks T021 and T035 must be merged in order.
- Within US5: T038, T039, T040 are independent test files.
- Polish: T053 and T054 are independent.

## Implementation strategy

1. **MVP**: Phase 1 + Phase 2 + US2 + US3. Every existing wallet gets the stronger derivation automatically with a safe rollback and recovery, with no new UI beyond the upgrade notice. This is the smallest slice that fixes the weakness for current users.
2. **Next**: US1 (passphrase) and US5 (biometrics and throttle), which close the remaining gaps.
3. **Then**: US4 (device limits and progress) and US6 (wording and docs), finishing with Polish.
4. Ship behind no flag, but do not release US2 without US3 (recovery tested) and the T001 benchmark result.
