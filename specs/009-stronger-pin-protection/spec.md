# Feature Specification: Stronger Wallet PIN & Passphrase Protection

**Feature Branch**: `feature/009-stronger-pin-protection`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "Stronger wallet PIN/passphrase protection (DGB-04 of the external security review). Today the wallet key is derived directly from a six-digit PIN with PBKDF2-SHA256 at 100,000 iterations and the ciphertext sits in browser storage, so a copied browser profile allows offline guessing of the PIN. Add an optional longer passphrase, a memory-hard key derivation (Argon2id) running in the browser, a versioned encryption envelope, a safe migration for existing wallets, correct behaviour with the PIN lockout/throttle and biometric unlock, acceptable performance on low-end phones, and honest user-facing wording."

## Background

The wallet encrypts each stored secret (recovery phrase, imported private key, watch-only key) with a key derived from the user's six-digit PIN. A six-digit PIN has only one million possibilities. The in-app attempt throttle slows guessing *inside the app*, but it does nothing against an attacker who copies the browser's stored data (a stolen or imaged device, a malicious browser extension, a shared profile) and guesses offline at full speed. The current derivation cost is low enough that all one million PINs can be tried in a short time on commodity hardware. An interim warning (`create.pin_limitation`) tells users this today; this feature makes each offline guess far more expensive for everyone and lets users who opt in remove the weakness entirely.

This is finding **DGB-04** of the external review; DGB-01 to DGB-03 and DGB-05 to DGB-07 were fixed under `specs/008-security-hardening`.

## Clarifications

### Session 2026-11-06

- Q: Should the app actively encourage a passphrase, or only offer it? → A: Gentle nudge. Setup recommends a passphrase for larger holdings, settings shows a dismissible upgrade card on PIN wallets, and there is no balance-triggered or recurring reminder.
- Q: For biometric-enabled wallets, what should be done about the biometric key and the biometric seed copy stored side by side in browser storage? → A: In scope, best effort. Protect the biometric key so only the biometric check can release it, where the browser supports that. Where unsupported, keep today's behaviour and warn the user plainly that biometrics lowers protection against copied browser data.
- Q: For the 30 days after an upgrade, should the old weakly protected copy stay in browser storage for rollback? → A: Short window. Keep it for 7 days, remove it on the next successful unlock after that, and tell the user in the upgrade notice. Rollback after removal is recovery from the seed phrase.
- Q: If a device or browser cannot run the memory-hard protection at all, what happens on new-wallet creation? → A: Allow legacy protection only after a plain warning and explicit user agreement; mark the wallet as legacy protection and upgrade it automatically on first unlock on a capable device or browser.
- Q: Besides the passphrase, should users be able to choose a longer numeric PIN? → A: No. Credential types are the six-digit PIN and the passphrase only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Protect a wallet with a strong passphrase (Priority: P1)

A user who holds meaningful funds chooses, when creating a wallet (or later from settings), to protect it with a passphrase of their own choosing instead of a six-digit PIN. The wallet's stored secrets become infeasible to guess offline even if someone copies the browser data.

**Why this priority**: This is the only change that actually closes the offline-guessing gap for a motivated attacker; everything else raises cost but cannot fix a six-digit space.

**Independent Test**: Create a wallet with a passphrase, lock it, unlock with the passphrase, and confirm a wrong passphrase is rejected. Confirm the stored data contains no plaintext secret.

**Acceptance Scenarios**:

1. **Given** a user creating a wallet, **When** they choose the passphrase option and enter a passphrase that meets the minimum strength rules, **Then** the wallet is created and subsequently unlocks only with that passphrase.
2. **Given** a wallet protected by a passphrase, **When** the user enters a wrong passphrase, **Then** unlock fails with the same generic error as a wrong PIN, and the attempt counts toward the lockout.
3. **Given** a user entering a too-short or very common passphrase, **When** they try to confirm it, **Then** they are told why it is not accepted and how to fix it, before anything is stored.
4. **Given** an existing PIN-protected wallet, **When** the user opts to upgrade to a passphrase from settings, **Then** they must first unlock with the current PIN, and afterwards the PIN alone no longer opens the wallet.

---

### User Story 2 - Existing wallets get stronger protection automatically and safely (Priority: P1)

A user with a wallet created before this feature unlocks as usual. On that successful unlock the wallet's stored secrets are silently re-protected with the stronger scheme, without the user doing anything and without any risk of losing access.

**Why this priority**: Most users will never visit settings. Without automatic migration the improvement only reaches new wallets, and a botched migration could lock people out of real funds, so this must be both automatic and provably safe.

**Independent Test**: Load storage containing a wallet in the legacy format, unlock with the correct PIN, and verify the stored data is now in the new format, the wallet still unlocks with the same PIN, and the recovery phrase is unchanged. Simulate a failure midway and verify the legacy data is intact and still usable.

**Acceptance Scenarios**:

1. **Given** a legacy-format wallet, **When** the user unlocks it with the correct PIN, **Then** the stored secret is re-encrypted in the new format and the user sees no change in how they use the wallet.
2. **Given** a migration that is interrupted (tab closed, storage error, out of memory), **When** the user next opens the app, **Then** the old data is still present and valid and unlock works; migration is retried on the next unlock.
3. **Given** a wallet that has just been migrated, **When** the new data is read back and decrypted in the same session, **Then** it matches the original secret exactly; if it does not, the old data is kept and the new data discarded.
4. **Given** a user with several wallets, **When** they unlock one, **Then** only that wallet is migrated; others migrate when they are unlocked, and every wallet remains usable while others are still legacy.
5. **Given** a wallet that has just been migrated and then opened by an older version of the app within 7 days, **When** the user unlocks it there, **Then** it still unlocks from the retained legacy copy; after that copy is removed, the older version finds no wallet and the user recovers with the recovery phrase (see FR-012).

---

### User Story 3 - Recovery from the seed phrase keeps working (Priority: P1)

A user who forgets their PIN or passphrase restores access with their recovery phrase and sets a new PIN or passphrase, exactly as today, including for wallets in the new format.

**Why this priority**: Self-custody depends on the recovery phrase being the ultimate backstop. If stronger protection can strand users, they will choose weaker habits or lose funds.

**Independent Test**: Create a wallet in the new format, forget the credential, run recovery with the phrase, and confirm access is regained with the new credential and that the same addresses and balance appear.

**Acceptance Scenarios**:

1. **Given** a wallet in the new format, **When** the user completes recovery with the correct phrase and sets a new credential, **Then** the wallet is re-protected under the new credential and the old credential no longer works.
2. **Given** an incorrect or malformed recovery phrase, **When** the user attempts recovery, **Then** nothing stored is changed.
3. **Given** a wallet protected by a passphrase, **When** the user uses recovery, **Then** they can switch to a PIN, choose a different passphrase, or stay on a passphrase.

---

### User Story 4 - Unlock stays fast and reliable on low-end phones (Priority: P2)

On a low-memory phone, unlocking and creating a wallet complete in a reasonable time without freezing the page, crashing the tab, or silently weakening protection.

**Why this priority**: A memory-hard scheme that is unusable on the devices many DigiByte users own would be abandoned or would force a weak fallback.

**Independent Test**: On a throttled, low-memory device profile, unlock a new-format wallet and measure time, responsiveness and success; repeat on a desktop.

**Acceptance Scenarios**:

1. **Given** a representative low-end phone, **When** the user unlocks, **Then** the wallet opens within the targets in Success Criteria and the interface keeps showing progress and responding during the wait.
2. **Given** a device that cannot provide the memory required, **When** the user creates or migrates a wallet, **Then** the wallet is never silently stored with weaker-than-minimum protection: the user is told, and a migration leaves the wallet in its current format.
3. **Given** derivation is in progress, **When** the user watches the screen, **Then** they see a clear "unlocking" state and cannot trigger duplicate attempts.

---

### User Story 5 - Lockout and biometrics behave consistently (Priority: P2)

The existing attempt throttle and biometric unlock continue to work with both credential types and do not become a way around the stronger protection.

**Why this priority**: These are existing user-facing behaviours with trust implications; breaking them or leaving a bypass defeats the point.

**Independent Test**: Exercise wrong-credential sequences on the unlock and PSBT-signing pages for both PIN and passphrase wallets, and enrol, use and disable biometrics on migrated and new wallets.

**Acceptance Scenarios**:

1. **Given** repeated wrong credentials on the unlock or PSBT-signing page, **When** the failure threshold is reached, **Then** the same delays apply for PIN and passphrase wallets, and the messages say "PIN" or "passphrase" correctly.
2. **Given** a wallet with biometric unlock enrolled, **When** it is migrated to the new format, **Then** biometric unlock continues to work without re-enrolment.
3. **Given** a user has enrolled biometrics on a passphrase wallet, **When** they perform actions that require the credential today (view recovery phrase, change credential, sign multisig), **Then** those still require the credential, as they do for PIN wallets.
4. **Given** the throttle resets when a page is left, **When** the product describes protection, **Then** it does not claim the throttle protects against offline copying; only the stronger derivation and a passphrase do.

---

### User Story 6 - Honest, clear wording about what protects the user (Priority: P3)

Users see accurate, plain-language guidance in English and Spanish: what a PIN does and does not protect against, when a passphrase is worth choosing, and what happens to their data during an upgrade.

**Why this priority**: Security copy that overstates protection is itself a defect; the current note exists precisely because of this.

**Independent Test**: Review all new and changed strings in both locales; check the PIN limitation note on PIN setup is reworded to match the protection actually in place.

**Acceptance Scenarios**:

1. **Given** a user creating a wallet, **When** they reach credential setup, **Then** they see the choice between PIN and passphrase with a short, accurate explanation of the trade-off.
2. **Given** a wallet on PIN-only protection, **When** the user views its protection, **Then** the wording reflects the real remaining risk (stronger than before, still bounded by a six-digit space) and offers the passphrase upgrade.
3. **Given** either locale, **When** any new string is displayed, **Then** it exists and reads correctly (no raw keys, no untranslated English in the Spanish UI).

---

### Edge Cases

- Storage contains a mix of legacy and new-format wallets, plus biometric-enrolled and non-enrolled wallets.
- Stored data carries a format version this app does not recognise (newer than the app, or corrupt): the user is told to update or that the data is unreadable; nothing is deleted or overwritten.
- Migration is attempted while another tab has the same wallet open or is migrating it at the same moment.
- Browser storage quota is exhausted while writing the re-encrypted data.
- A passphrase contains non-ASCII characters, emoji, leading/trailing spaces, or differently composed Unicode forms of the same visible text; the same text typed on different devices or keyboards must unlock.
- A user forgets a passphrase and has no phrase backup: the product says clearly the project cannot reset it.
- Watch-only and imported-key wallets, which have no recovery phrase, still migrate and can still upgrade; their recovery path is re-import.
- Multisig wallets and PSBT signing that ask for the credential continue to work.
- A very old browser lacking a required capability: the wallet continues in legacy mode with an explanation, not an error.
- Offline use: migration and unlock need no network.
- The device runs out of memory during derivation: the attempt fails cleanly without corrupting stored data or counting as a wrong credential.

## Requirements *(mandatory)*

### Functional Requirements

**Credential and protection**

- **FR-001**: Users MUST be able to protect a wallet with a passphrase instead of a six-digit PIN, both when creating a wallet and later from settings; PIN-only protection MUST remain available. Credential setup MUST recommend a passphrase (explaining it is advised for larger holdings), and settings MUST show a dismissible upgrade prompt on PIN-protected wallets; the app MUST NOT use balance-triggered or recurring reminders, and MUST NOT require a passphrase. The supported credential types are exactly the six-digit PIN and the passphrase; longer or variable-length numeric PINs are not offered.
- **FR-002**: The system MUST enforce a minimum passphrase length and reject very common passphrases, and MUST show the user why a passphrase was rejected.
- **FR-003**: The key that encrypts stored secrets MUST be derived with a deliberately memory-hard, time-hard process performed entirely in the user's browser, with cost settings chosen so each offline guess is substantially more expensive than today's (target in SC-002).
- **FR-004**: Each stored secret MUST use its own fresh random salt and nonce, and stored data MUST remain authenticated so tampering or a wrong credential is detected.
- **FR-005**: Neither the PIN, the passphrase, derived keys, nor decrypted secrets may leave the device through any channel (telemetry, logs, errors, backups), consistent with the constitution's self-custody principle.
- **FR-006**: Passphrases MUST be normalised so the same visible text produces the same key regardless of how the device encodes it.

**Versioned envelope**

- **FR-007**: Every encrypted secret MUST be stored in a self-describing, versioned format that records which scheme and cost settings were used, so later parameter changes need no guesswork and unknown versions are detected.
- **FR-008**: The system MUST continue to read the legacy format indefinitely for unlock, recovery and export, so no wallet is stranded by this change.
- **FR-009**: The system MUST refuse, with a clear message and without altering data, to process a format version it does not understand.

**Migration, rollback and recovery**

- **FR-010**: On a successful unlock of a legacy-format wallet the system MUST re-encrypt that wallet's secrets in the new format automatically, without requiring user action.
- **FR-011**: Migration MUST be non-destructive: the new data MUST be written and verified (decrypts to the identical secret) before the old data is replaced, and any failure MUST leave the old data intact and usable.
- **FR-012**: Rollback: for 7 days after migration the system MUST retain the legacy encrypted copy so that reverting to an earlier app version, or an offline-cached older version, can still unlock the wallet; after 7 days the copy MUST be removed on the next successful unlock. The upgrade notice MUST tell the user that the old copy remains for 7 days and what that means for protection against copied browser data. Recovery from the seed phrase remains the rollback path after removal. Versions of the app released after this feature MUST detect formats newer than they understand and show an "update required" message rather than a wrong-credential error; versions shipped before this feature cannot be changed and are covered by the 7-day retained copy.
- **FR-013**: Recovery from the recovery phrase MUST work for wallets in both formats and MUST let the user set a new PIN or passphrase; failure or an invalid phrase MUST change nothing stored.
- **FR-014**: Migration MUST be idempotent and safe under concurrency: two tabs, repeated unlocks or a crash MUST NOT produce a wallet that cannot be unlocked.
- **FR-015**: Each wallet MUST migrate independently when it is unlocked; the app MUST work with a mixed set of formats.

**Throttle, biometrics and re-authentication**

- **FR-016**: The existing wrong-credential throttle MUST apply equally to PIN and passphrase wallets everywhere a credential is requested, including the unlock page and PSBT signing, with wording appropriate to the credential type.
- **FR-017**: A failed attempt caused by an environmental problem (out of memory, interrupted work) MUST NOT be counted as a wrong credential.
- **FR-018**: Biometric unlock MUST keep working across migration without re-enrolment, and MUST NOT bypass credential-gated actions that require the credential today.
- **FR-018a**: Where the browser supports it, the key that protects the biometric seed copy MUST be releasable only by the biometric check itself, so that copying the browser's stored data does not expose the seed without that check. Existing biometric enrolments MUST be upgraded to this protection without re-enrolment where possible, or prompt a one-time re-enrolment where not.
- **FR-018b**: Where the browser does not support that protection, biometric enrolment MUST show a plain warning that biometrics lowers protection against copied browser data, and MUST let the user decline; users who already have biometrics enabled MUST see the same warning once.
- **FR-019**: Operations that currently require the credential (view recovery phrase, change credential, enrol biometrics, signing flows that prompt for it) MUST continue to do so.

**Performance and device limits**

- **FR-020**: Derivation MUST NOT freeze the interface; the user MUST see an unlocking indicator, and repeat submissions during derivation MUST be ignored.
- **FR-021**: Cost settings MUST meet SC-003 on a representative low-end phone; on devices that cannot meet the standard settings the system MUST NOT silently fall below a defined minimum and MUST tell the user what is happening. If a device or browser cannot run the memory-hard protection at all, new-wallet creation MUST still be allowed with the legacy protection, but only after a plain warning that the wallet is weaker and the user's explicit agreement; the wallet MUST be marked as legacy protection and MUST upgrade automatically on its first unlock on a capable device or browser.

**Wording and documentation**

- **FR-022**: All new and changed user-facing text MUST be provided in English and Spanish and explain PIN versus passphrase trade-offs honestly; the interim `create.pin_limitation` note MUST be updated or removed to match the protection actually in place.
- **FR-023**: Project security documentation (cryptographic primitives and key-management sections) MUST be updated to describe the new scheme, the envelope format, the migration and the remaining limitations.
- **FR-024**: Per the constitution, every change to crypto and wallet logic MUST ship with tests that fail before the change, including negative paths (wrong credential, truncated or tampered data, unknown version, interrupted migration, legacy-format fixtures).

### Key Entities

- **Protected secret**: An encrypted wallet secret (recovery phrase, imported key, or watch-only key) bound to one wallet. Attributes: format version, scheme and cost settings used, salt, nonce, ciphertext.
- **Credential**: What the user types to unlock: a six-digit PIN or a passphrase. The type is a property of the wallet so the app prompts correctly.
- **Format version**: Identifier stored with each secret: "legacy" for the existing scheme, increasing numbers thereafter.
- **Migration state**: Per-wallet record of whether re-encryption has completed and whether a retained legacy copy is still within its grace period.
- **Biometric enrolment**: Existing per-device association that lets a wallet unlock without typing the credential; it must survive migration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of existing legacy-format wallets unlocked with the correct PIN end up in the new format with the identical recovery phrase and addresses, across a test set covering every wallet type (recovery phrase, imported key, watch-only).
- **SC-002**: The cost per guess for an attacker trying all one million six-digit PINs offline against a copied wallet is at least 100 times higher than under the current scheme for a GPU-class attacker, as measured by the benchmark in the quickstart; a passphrase meeting the minimum rules is not practically guessable offline with realistic resources. PIN-only wallets are described to users as stronger but still guessable with effort.
- **SC-003**: On a representative low-end phone (reference device defined in planning, for example 2 GB RAM and a mid-2010s processor), a typical unlock takes no more than 3 seconds and never more than 6 seconds with the interface staying responsive; on a current mid-range phone and on desktop it takes no more than 1 second.
- **SC-004**: In fault-injection testing (interruption, storage failure, out of memory, concurrent tabs) at every step of migration, 0 cases leave a wallet that cannot be unlocked with its prior credential or recovered from its phrase.
- **SC-005**: Recovery from the phrase succeeds in 100% of tested cases for legacy, new-format, PIN and passphrase wallets, and an incorrect phrase changes nothing.
- **SC-006**: At least 90% of test participants can create a wallet with a passphrase, lock it and unlock it on the first try without help, and at least 90% correctly state after reading the setup text that the recovery phrase is the only way back if they forget the credential.
- **SC-007**: Wrong-credential behaviour (threshold, delay, messages) is identical in outcome for PIN and passphrase wallets on every page that asks for a credential.
- **SC-008**: Every new string exists in both English and Spanish, with none missing or showing a raw key.
- **SC-009**: Users who take no action see no new required steps after the upgrade.
- **SC-010**: A dismissed passphrase upgrade prompt does not reappear on later unlocks or sessions unless the user reopens it from settings.
- **SC-011**: On browsers that support it, copying a biometric-enabled wallet's stored data to another profile or device does not allow the seed to be recovered without the biometric check or the credential.
- **SC-012**: No legacy-format copy of a wallet secret remains in storage more than 7 days after migration plus one successful unlock.
- **SC-013**: On a browser that cannot run the memory-hard protection, 100% of new wallets are created with legacy protection only after the user has seen the weaker-protection warning and agreed, and are marked as legacy protection until upgraded.

## Assumptions

- The memory-hard scheme is Argon2id, run in the browser through a vetted library, as mandated by the security review. Memory, iteration and parallelism values are a planning decision validated against SC-002 and SC-003. If no library fits the existing static-hosting and content-security constraints, planning returns that as a decision rather than weakening the requirements.
- The passphrase is optional. PIN-only wallets remain supported and are strengthened by the new derivation, but are not claimed to be immune to offline guessing.
- The minimum passphrase length defaults to 12 characters plus a common-password check (final value set in planning).
- While the retained legacy copy exists (up to 7 days), copied browser data can still be guessed against the old, weaker protection; full protection for an upgraded wallet starts when that copy is removed.
- Migration happens only on a successful unlock with the correct credential.
- Today the biometric key and the biometric seed copy are stored together in browser storage, so copied browser data can expose the seed without the PIN. This feature closes that where the browser allows (FR-018a) and otherwise warns (FR-018b); it does not remove biometric unlock.
- The throttle remains per-page as today. A persistent throttle or a wipe-after-N-attempts policy is out of scope.
- Server-side components do not participate; all work stays in the browser, consistent with the constitution.
- Out of scope: longer or variable-length numeric PINs (the credential types are the six-digit PIN and the passphrase), first-party analytics, directory registration with ownership proof, changing the recovery-phrase format, hardware-wallet support, cloud backup, and any operator-assisted recovery.
