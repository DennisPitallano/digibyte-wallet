---

description: "Task list for Multisig Management Revamp"
---

# Tasks: Multisig Management Revamp

**Input**: Design documents from `/specs/016-multisig-management-revamp/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/multisig-service-and-ui-contracts.md, quickstart.md

**Tests**: REQUIRED. Constitution Principle III and FR-031 make test-first mandatory for `DigiByte.Wallet` logic: write each test task first, confirm it FAILS, then implement. UI is verified manually via `quickstart.md`.

**Organization**: Grouped by user story so each can be implemented, tested, and delivered independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US7, mapping to spec.md user stories
- `MultisigWalletService.cs` and `MultisigModels.cs` are touched by several stories, so tasks editing them are NOT marked [P]

## Path Conventions

- Domain/persistence: `src/DigiByte.Wallet/`
- UI: `src/DigiByte.Web/` (pages in `Pages/`, new shared pieces in `Components/Multisig/`)
- Tests: `tests/DigiByte.Wallet.Tests/`

---

## Phase 1: Setup

**Purpose**: Scaffolding shared by all stories

- [X] T001 Create and switch to the feature branch `feature/multisig-management-revamp` (confirm with the user first because the working tree is currently on `fix/spv-gateway-peer-ping`; keep the untracked spec directory)
- [X] T002 Create folder `src/DigiByte.Web/Components/Multisig/` and confirm `_Imports.razor` in `src/DigiByte.Web/` exposes `DigiByte.Wallet.Models` and `DigiByte.Wallet.Services` to it
- [X] T003 [P] Review `tests/DigiByte.Wallet.Tests/MultisigWalletServiceTests.cs` and reuse/extend its in-memory `ISecureStorage` and fake `IBlockchainService` doubles into a shared helper `tests/DigiByte.Wallet.Tests/MultisigTestDoubles.cs` (fake chain must support `GetAddressTransactionsAsync(address, skip, take)`, `GetUtxosAsync`, `GetFeeRateAsync`, and a settable offline failure)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Additive model/storage changes and plumbing that every story relies on. No story work starts until this is done.

- [X] T004 Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigModelsTests.cs` asserting that JSON written by the OLD models (no new fields) deserializes into the new models without error and with defaults (backward compatibility, data-model.md "additive" rule)
- [X] T005 Extend `src/DigiByte.Wallet/Models/MultisigModels.cs` per data-model.md: `MultisigWalletConfig` gets `OwnerWalletIds` (List<string>, empty = unassigned), `Name` (string?), `BackupAt` (DateTime?); `MultisigDraft` gets `OwnerWalletId` (string?); `PendingMultisigTransaction` gets `UnsignedTxId` (string), `Memo` (string?), `FailureReason` (string?), `CancelledAt` (DateTime?), `UpdatedAt` (DateTime); all additive and defaulted so old JSON still loads
- [X] T006 Extend the private `MultisigWalletEntry` in `src/DigiByte.Wallet/Services/MultisigWalletService.cs` with `OwnerWalletIds` (List<string>, empty = unassigned) and keep `Name` as the source of truth for the display name; make `Name` settable
- [X] T007 [P] Add `ActivityEntry`, `ActivityKind` (Received, Sent, ProposalAwaiting, ProposalReady, Failed, Cancelled) and `ActivityDirection` (In, Out, None) types per data-model.md in new file `src/DigiByte.Wallet/Models/MultisigActivity.cs`
- [X] T008 Add a small helper in `src/DigiByte.Web/` (e.g. `Services/ActiveWalletContext.cs` or an existing service) exposing the active app wallet id (`WalletService.GetActiveWalletIdAsync()`) and active wallet public key (`WalletService.GetPublicKey()`) to multisig pages; register it in `src/DigiByte.Web/Program.cs`
- [X] T009 Verify `dotnet build` is green and T004 now passes

**Checkpoint**: Models/plumbing ready; user stories can proceed.

---

## Phase 3: User Story 1 - Multisig wallets belong to the wallet that created them (Priority: P1) MVP

**Goal**: A multisig, draft, and its records appear only under the owning wallet; legacy items are migrated or claimable.

**Independent Test**: quickstart Scenario 1 — create under Wallet A, nothing under Wallet B, direct URL shows "belongs to another wallet", legacy unassigned flow works.

### Tests for User Story 1 (write first, must FAIL)

- [X] T010 [P] [US1] Create `tests/DigiByte.Wallet.Tests/MultisigScopingTests.cs`: (a) multisig created with owner A is listed for A and not for B; (b) created/imported under B appears only for B; (c) `GetConfigAsync(id, ownerB)` returns null for A's multisig; (d) drafts are scoped by owner; (e) legacy entry (no owner) whose co-signer key equals the active wallet public key is attributed to that wallet on first list; (f) unmatched legacy entry is absent from every wallet's list but present in `ListUnassignedAsync()`; (g) `ClaimAsync` assigns it to the claimer, is idempotent, and rejects when owned by another wallet; (h) migration never deletes data; (i) deleting from the only owner removes its config, pending items, and list entry, while deleting from one of two owners leaves it fully intact for the other owner; (j) a multisig whose co-signer keys match two wallets appears in both wallets' lists once each wallet has listed (F1: ownership is a list); (k) `GetAccessStateAsync` returns Owned for a legacy multisig whose key matches the active wallet even if it was never listed, OtherWallet for another wallet's multisig, and NotFound for an unknown id

### Implementation for User Story 1

- [X] T011 [US1] In `src/DigiByte.Wallet/Services/MultisigWalletService.cs` add `ownerWalletId` parameters to `CreateMultisigWalletAsync`, `ImportMultisigWalletAsync`, and persist `OwnerWalletIds` (containing that wallet) on entry and config
- [X] T012 [US1] In `MultisigWalletService.cs` change `ListWalletsAsync` to `ListWalletsAsync(ownerWalletId, ownPublicKeyHex)` returning only owned entries, running the idempotent lazy ownership migration (for entries not yet owned by this wallet, match `ownPublicKeyHex` against co-signer keys and add the wallet id to `OwnerWalletIds`; never delete)
- [X] T013 [US1] In `MultisigWalletService.cs` add `ListUnassignedAsync()` and `ClaimAsync(walletId, ownerWalletId)` (adds the owner to an unassigned entry; idempotent; throw if the entry already has a different owner)
- [X] T014 [US1] In `MultisigWalletService.cs` add `GetAccessStateAsync(walletId, ownerWalletId, ownPublicKeyHex)` returning Owned / OtherWallet / NotFound, running the same key-based attribution as `ListWalletsAsync` before deciding; change `GetConfigAsync` to `GetConfigAsync(walletId, ownerWalletId)` returning the config only when Owned (otherwise null); keep an internal unscoped getter for migration/claim only
- [X] T015 [US1] In `MultisigWalletService.cs` scope drafts: `ListDraftsAsync(ownerWalletId, ownPublicKeyHex)` (legacy drafts matched on `OwnPublicKeyHex`), `SaveDraftAsync` sets `OwnerWalletId`
- [X] T016 [US1] In `MultisigWalletService.cs` update `DeleteWalletAsync` to `DeleteWalletAsync(walletId, ownerWalletId)`: remove only that wallet from `OwnerWalletIds`, and delete the config, pending items, and list entry only when no owner remains (activity is computed from the chain and proposals, so no separate activity data exists)
- [X] T017 [US1] Update all call sites to the new signatures: `src/DigiByte.Web/Pages/MultisigWallets.razor`, `MultisigDetail.razor`, `MultisigSend.razor`, `MultisigPending.razor`, `MultisigCreate.razor`, `MultisigImport.razor`, `MultisigRoomPage.razor`, and any other caller found via grep for `MultisigService.` in `src/DigiByte.Web/`
- [X] T018 [US1] Add or update the delete flow in `src/DigiByte.Web/Pages/MultisigWallets.razor` and `MultisigDetail.razor`: confirmation dialog that states the redeem-script backup warning ("your seed phrase alone cannot recover it") and offers Download backup before the delete is performed (FR-005)
- [X] T019 [US1] Update the existing tests `tests/DigiByte.Wallet.Tests/MultisigWalletServiceTests.cs` and `MultisigModelsTests.cs` to the new owner-aware method signatures so the suite compiles and passes
- [X] T020 [US1] In `src/DigiByte.Web/Pages/MultisigWallets.razor` show only the active wallet's multisigs/drafts, add an "Unassigned multisigs" section with a Claim action (shown whenever `ListUnassignedAsync()` is non-empty, dismissible for the current session), and a correct empty state with Create/Import actions
- [X] T021 [US1] In `src/DigiByte.Web/Pages/MultisigDetail.razor` (and pending/send/activity routes) render a "This multisig belongs to another wallet" state with a Back action when `GetAccessStateAsync` returns OtherWallet (show the existing "Wallet Not Found" state for NotFound); never render its contents or flash data while loading
- [X] T022 [US1] Pass the active wallet id into create/import in `MultisigCreate.razor` and `MultisigImport.razor`; react to wallet switching on the multisig pages (re-load or navigate back to `/multisig` when the active wallet id changes)
- [ ] T023 [US1] Run T010 tests to green, then manually run quickstart Scenario 1

**Checkpoint**: US1 independently verifiable; the privacy defect is fixed.

---

## Phase 4: User Story 2 - Rename a multisig (Priority: P1)

**Goal**: Rename multisig, draft, and co-signer labels; names are local only.

**Independent Test**: quickstart Scenario 2.

### Tests for User Story 2 (write first, must FAIL)

- [X] T024 [P] [US2] Create `tests/DigiByte.Wallet.Tests/MultisigRenameTests.cs`: name is trimmed; empty/whitespace rejected and previous name kept; "1–40 chars" enforced (40 accepted, 41 rejected with error text, never silently capped); duplicate name within the same owner allowed with `DuplicateWarning = true`; rename leaves `Address`, `RedeemScriptHex`, `RequiredSignatures`, and every co-signer `PublicKeyHex` byte-identical; persists across a fresh service instance; `RenameCoSignerAsync` changes only the label (non-empty, ≤ 40 chars); draft rename keeps all draft progress; backup text contains the name only as an informational note and `ImportMultisigWalletAsync` never overwrites existing local labels; a rename or label edit made under one owner wallet is visible under the other owner wallet

### Implementation for User Story 2

- [X] T025 [US2] In `src/DigiByte.Wallet/Services/MultisigWalletService.cs` add `RenameWalletAsync(walletId, name)` returning `RenameResult { Ok, Error?, DuplicateWarning }`, `RenameDraftAsync(draftId, name)`, and `RenameCoSignerAsync(walletId, publicKeyHex, name)` per contracts; names trimmed, 1–40 chars
- [X] T026 [US2] In `MultisigWalletService.BuildBackup` include the multisig name as an informational line only; ensure import paths never replace existing labels
- [X] T027 [P] [US2] Create `src/DigiByte.Web/Components/Multisig/RenameDialog.razor` (input with max length 40, inline error, duplicate warning, Save/Cancel, accessible labels, 44 px targets)
- [X] T028 [US2] Wire rename into `src/DigiByte.Web/Pages/MultisigWallets.razor` (multisig and draft rows) and the dashboard header in `src/DigiByte.Web/Pages/MultisigDetail.razor` (tap name or edit icon); show the entered name instead of the generated "2-of-3 Multisig" label everywhere a multisig is listed or titled (`MultisigSend`, `MultisigPending`, headers)
- [ ] T029 [US2] Run T024 tests to green, then quickstart Scenario 2

**Checkpoint**: US1 + US2 deliver the P1 data fixes.

---

## Phase 5: User Story 3 - See and verify full co-signer public keys (Priority: P1)

**Goal**: Every co-signer's full key is viewable, copyable, QR-able, with editable labels.

**Independent Test**: quickstart Scenario 3.

### Tests for User Story 3 (write first, must FAIL)

- [X] T030 [P] [US3] Create `tests/DigiByte.Wallet.Tests/KeyFormatTests.cs` for a pure helper `MultisigKeyFormat.Group(hex)` in `DigiByte.Wallet`: a 66-char compressed key is split into readable blocks that rejoin to the original exactly; uppercase/whitespace normalised; invalid input returns the input unchanged

### Implementation for User Story 3

- [X] T031 [US3] Add `src/DigiByte.Wallet/Services/MultisigKeyFormat.cs` implementing `Group(hex)` and a `Normalize(hex)` used by the UI
- [X] T032 [P] [US3] Create `src/DigiByte.Web/Components/Multisig/KeyView.razor`: full key wrapped (never clipped), grouped display, Copy with confirmation, and QR code reusing the QR approach used on `Receive.razor`
- [X] T033 [P] [US3] Create `src/DigiByte.Web/Components/Multisig/CoSignerRow.razor`: collapsed row (label, short key, "You"/watch-only marker) that expands in place to `KeyView` plus a label-edit control calling `RenameCoSignerAsync`
- [X] T034 [US3] Replace the truncated co-signer list in `src/DigiByte.Web/Pages/MultisigDetail.razor` with `CoSignerRow`; show a "Watch-only" notice when no key on this device
- [ ] T035 [US3] Verify at 320 px (no clipped text/horizontal scroll) and run quickstart Scenario 3

**Checkpoint**: All three reported defects are fixed (US1–US3).

---

## Phase 6: User Story 6 - Transaction activity and history (Priority: P2)

**Goal**: A unified Activity view (and dashboard summary) of received, sent, and proposal entries. Built before the dashboard revamp because the dashboard shows recent activity.

**Independent Test**: quickstart Scenario 7.

### Tests for User Story 6 (write first, must FAIL)

- [X] T036 [P] [US6] Create `tests/DigiByte.Wallet.Tests/MultisigActivityBuilderTests.cs`: chain tx paying the multisig address with no multisig inputs → Received with amount = outputs to the address and counterparty = first external input address; tx spending from the address → Sent with amount = outputs not to the address (change excluded), counterparty = first external output, `MoreCount` for others; a proposal whose `BroadcastTxId` matches a chain tx yields ONE Sent entry (no duplicate); awaiting/ready/failed/cancelled proposals map to their kinds with signed/required counts; ordering newest first; date grouping; filters All/Received/Sent/Pending/Failed; offline (no chain txs) still returns local proposals and sets the partial flag; paging `skip/take` returns the next 25
- [X] T037 [P] [US6] Add to `MultisigWalletServiceTests.cs` tests for `GetActivityAsync(config, skip, take)`: first call requests `take: 25`, "Load more" advances `skip` by 25, chain failure returns local entries with `IsPartial = true`

### Implementation for User Story 6

- [X] T038 [US6] Create `src/DigiByte.Wallet/Services/MultisigActivityBuilder.cs` with pure `Build(address, chainTxs, proposals)`, `GroupByDate`, and `Filter` implementing T036 rules
- [X] T039 [US6] In `MultisigWalletService.cs` add `GetActivityAsync(config, skip, take = 25)` using `_blockchain.GetAddressTransactionsAsync` and `GetPendingTransactionsAsync`, catching chain errors into an offline/partial result
- [X] T040 [P] [US6] Create `src/DigiByte.Web/Components/Multisig/ActivityItem.razor` and `ActivityList.razor` (direction icon, amount, counterparty short with tap-to-expand, memo, date, status chip, confirmations; filter chips; date group headers; Load more button; empty and offline states)
- [X] T041 [US6] Create `src/DigiByte.Web/Pages/MultisigActivity.razor` at `@page "/multisig/{WalletId}/activity"` using `ActivityList`, scoped by owner, with an entry detail panel (full TxID with copy and open-in-explorer via existing explorer URL helper, full addresses, fee, signer progress, status timeline)
- [X] T042 [US6] Add a "Recent activity" section (latest 3–5 entries + "View all") to `src/DigiByte.Web/Pages/MultisigDetail.razor`
- [ ] T043 [US6] Run T036/T037 to green, then quickstart Scenario 7 (including 25+ entries and offline)

**Checkpoint**: Activity works standalone and feeds the dashboard.

---

## Phase 7: User Story 4 - Modern, compact multisig dashboard (Priority: P2)

**Goal**: Above-the-fold balance/address/actions; collapsible secondary sections; compact backup status.

**Independent Test**: quickstart Scenario 4.

### Tests for User Story 4 (write first, must FAIL)

- [X] T044 [P] [US4] Add to `MultisigWalletServiceTests.cs`: `MarkBackedUpAsync` sets `BackupAt` (UTC) and persists; absent before; calling twice updates the date; pending-needs-my-signature count helper returns the number of AwaitingSignatures items whose `SignedBy` does not contain the local key (watch-only → 0)

### Implementation for User Story 4

- [X] T045 [US4] In `MultisigWalletService.cs` add `MarkBackedUpAsync(walletId)` and a `CountNeedingMySignature(pending, config)` helper
- [X] T046 [P] [US4] Create `src/DigiByte.Web/Components/Multisig/MultisigHeader.razor` (back, editable name, M-of-N badge, watch-only chip) and `BackupCard.razor` (full amber prompt until `BackupAt`, then one-line "Backed up <date>"; expandable redeem script with Copy and Download that call `MarkBackedUpAsync`)
- [X] T047 [P] [US4] Create `src/DigiByte.Web/Components/Multisig/ReceiveSheet.razor`: bottom sheet/modal showing the multisig address (wrapped), QR code (same approach as `Receive.razor`), and Copy with confirmation; accessible close control and 44 px targets
- [X] T048 [US4] Rebuild `src/DigiByte.Web/Pages/MultisigDetail.razor` layout: header → balance (skeleton while loading, distinct offline/error state, never show unknown as 0) → address row with Copy and QR → primary actions Send / Receive (opens `ReceiveSheet`) / Pending (attention badge) and a prominent "Needs your signature" CTA when applicable → collapsible Co-signers, Redeem script & backup, Recent activity
- [ ] T049 [US4] Ensure 44 px touch targets, accessible labels on icon-only buttons, light/dark themes, and that balance, address, and the three actions are visible without scrolling at 360×800; compare collapsed-height against the old layout (SC-004 ≥ 30% shorter). This rebuild MUST keep the rename (T028), `CoSignerRow` (T034), and Recent activity (T042) work already added to this page
- [ ] T050 [US4] Run T044 tests to green, then quickstart Scenario 4

**Checkpoint**: Dashboard revamped.

---

## Phase 8: User Story 5 - Improved create-transaction page (Priority: P2)

**Goal**: Paste/scan/Max, fee and remaining balance, validation, review step, post-create sign/share.

**Independent Test**: quickstart Scenario 5.

### Tests for User Story 5 (write first, must FAIL)

- [X] T051 [P] [US5] Create `tests/DigiByte.Wallet.Tests/MultisigSendValidationTests.cs`: `ValidateDestination` rejects wrong-network and bad-checksum addresses and accepts valid ones for mainnet and testnet; flags destination equal to the multisig's own address as a warning; `EstimateSend(utxos, feeRateSatPerVByte, amount, outputCount)` fee = estimated vsize × sat/vB rate and grows with input count (not a flat 250 bytes), Max = total − fee, Remaining = total − amount − fee; amount above spendable returns an error naming the available balance; amount below `MultisigSendHelper.DustThresholdSatoshis` (100,000 sat = 0.001 DGB) rejected and an amount equal to it accepted; change below the threshold is added to the fee (no change output) and Max leaves no change; amount precision never rounds a satoshi value; scanned-payload classifier maps address / `digibyte:` URI (with amount and label) / PSBT (`cHNidP…`) / unknown as in contracts §4
- [X] T052 [P] [US5] Add test that two rapid calls to create a proposal with the same inputs (double submit guard at service level via idempotency key or page flag contract) produce only one pending item when the guard token is reused; and that two concurrent `FinalizeAndBroadcastAsync` calls on the same proposal broadcast exactly once

### Implementation for User Story 5

- [X] T053 [US5] Create `src/DigiByte.Wallet/Services/MultisigSendHelper.cs` with a public `DustThresholdSatoshis = 100_000` constant, `ValidateDestination`, `EstimateSend`, and `ClassifyScan(payload)` using `AddressValidator` and `PaymentRequest.FromUri`
- [X] T054 [US5] In `MultisigWalletService.CreateSpendingPSBTAsync` replace the flat `GetFee(250)` estimate and the old 5 sat/byte default with the real estimate from `MultisigSendHelper` using the same sat/vB fee-rate model and default as `Pages/Send.razor`, store `UnsignedTxId`, `Memo`, `UpdatedAt`, accept an optional `submissionToken` for idempotency (T052), and apply address validation before building; omit a change output below the dust threshold (fold it into the fee)
- [X] T055 [P] [US5] Create `src/DigiByte.Web/Components/Multisig/RecipientInput.razor`: manual input, Paste button (clipboard), Scan button using `QrScannerModal` (address or `digibyte:` URI prefills destination/amount/label; PSBT or unknown shows an explanatory message), optional known-recipient picker from Contacts (hidden when the contacts store is empty or unavailable), inline validation errors
- [X] T056 [US5] Rebuild `src/DigiByte.Web/Pages/MultisigSend.razor`: `RecipientInput`, amount with Max, available balance / estimated fee / remaining balance, own-address warning, disabled Continue on any error, review step (full recipient, amount, fee, signatures required, memo; Edit/Confirm; double-tap guard), success state offering Sign now and Share
- [ ] T057 [US5] Run T051/T052 to green, then quickstart Scenario 5 (including camera denied → paste fallback)

**Checkpoint**: Send flow complete.

---

## Phase 9: User Story 7 - Improved pending transactions (Priority: P3)

**Goal**: Pending queue with signer checklist, contextual actions, verified signature import, local-only cancel, recoverable failures.

**Independent Test**: quickstart Scenario 6.

### Tests for User Story 7 (write first, must FAIL)

- [X] T058 [P] [US7] Create `tests/DigiByte.Wallet.Tests/MultisigPendingImportTests.cs`: importing a PSBT whose unsigned txid differs from `UnsignedTxId` is rejected with a specific reason; a PSBT signed by a key not in the redeem script is rejected; a valid co-signer copy merges and updates `SignedBy` and status to `ReadyToFinalize` at threshold; malformed base64 rejected with a user-facing message; `CancelPendingAsync` sets `Cancelled`/`CancelledAt` and `FinalizeAndBroadcastAsync` then refuses it; a broadcast failure sets `Failed` with `FailureReason` and `RetryBroadcastAsync` restores a recoverable `ReadyToFinalize`; queue sort puts needs-my-signature first, then ready, then awaiting others, then failed, then cancelled
- [X] T059 [US7] Add failing tests to `tests/DigiByte.Wallet.Tests/MultisigPendingImportTests.cs`: a proposal saved by the OLD model (empty `UnsignedTxId`) gets `UnsignedTxId` computed from its stored PSBT when loaded via `GetPendingTransactionsAsync`, persisted, and then accepts a valid co-signer signature copy

### Implementation for User Story 7

- [X] T060 [US7] In `MultisigWalletService.GetPendingTransactionsAsync` backfill and persist `UnsignedTxId` for proposals where it is empty (computed from the PSBT's unsigned transaction id)
- [X] T061 [US7] In `MultisigWalletService.cs` update `ImportSignedPSBTAsync` to verify same unsigned txid and signer keys ∈ redeem script, throwing a typed `PsbtImportException` with a user-facing reason (FR-023)
- [X] T062 [US7] In `MultisigWalletService.cs` add `CancelPendingAsync`, `RetryBroadcastAsync`, failure capture in `FinalizeAndBroadcastAsync` (`Failed` + `FailureReason`), an in-flight guard so concurrent broadcast/retry calls broadcast once, refuse cancelled items, and a `SortPending(pending, config)` helper implementing the order in T058
- [X] T063 [P] [US7] Create `src/DigiByte.Web/Components/Multisig/SignerProgress.razor` (per-co-signer signed/unsigned checklist, "x of M" progress bar, accessible text)
- [X] T064 [US7] Rebuild `src/DigiByte.Web/Pages/MultisigPending.razor`: sorted queue, cards with recipient, amount, fee, memo, age, `SignerProgress`, "Needs your signature" label, contextual actions (Sign; Share via copy PSBT / QR / file; Import signature via paste / scan (checked with `ClassifyScan`; a non-PSBT scan shows an explanation) / file; Broadcast primary at threshold (disabled while a broadcast is in flight); Retry on failure; Cancel with confirmation stating it is local only and co-signers holding a copy could still broadcast), human-readable failure reasons
- [ ] T065 [US7] Run T058 to green, then quickstart Scenario 6

**Checkpoint**: All user stories complete.

---

## Phase 10: Polish & Cross-Cutting

- [X] T066 [P] Update the in-app guide `src/DigiByte.Web/Pages/MultisigGuide.razor` and `Help.razor` multisig entries for rename, full keys, activity, scan/paste, and cancel semantics
- [X] T067 [P] Update `docs/PROCESS_FLOWS.md` (multisig flows) and `docs/ARCHITECTURE.md` if storage/ownership description changes (constitution documentation rule)
- [ ] T068 Accessibility and responsive pass across all multisig pages at 320, 360, 768 px in light and dark themes (FR-029, SC-008)
- [X] T069 Security review pass: confirm no private key/seed/password appears in UI, logs, or backups (FR-030); confirm every scanner/clipboard entry path validates before use
- [ ] T070 Run `dotnet build` and `dotnet test` for `tests/DigiByte.Wallet.Tests` (and any other affected project); run all quickstart scenarios once end to end
- [ ] T071 Prepare PR with Conventional Commit title (e.g. `feat(multisig): wallet scoping, rename, full keys, activity and send revamp`) and note the storage migration in the description; flag as security-relevant: PSBT signature-import verification (T061), scanner/clipboard address validation (T055), and the ownership/scoping change

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2** first; Phase 2 blocks all stories.
- **US1 (P1)** should land first (it changes service signatures used by every page). US2 and US3 follow and are independent of each other.
- **US6 (Activity)** precedes **US4 (Dashboard)** because the dashboard embeds recent activity; US4's other work can start in parallel once US3 components exist.
- **US5 (Send)** depends only on Foundational + US1 signatures; it can run parallel to US6/US4 by a second developer.
- **US7 (Pending)** depends on `UnsignedTxId` (T005/T054) and benefits from US5 storing it; do it after US5.
- **Polish** last.

Story order: US1 → (US2 ∥ US3) → US6 → US4 → US5 → US7.

### Within each story

Tests first (must fail) → models/service → components → pages → manual quickstart check.

## Parallel Examples

- Phase 2: T007 can run while T005/T006 are in progress (different file).
- US1: T010 (tests) can be written while T011 begins; UI tasks T020–T022 must wait for T011–T015.
- US2 ∥ US3: T024–T027 and T030–T033 touch different files (note both edit `MultisigDetail.razor` later in T028/T034: do those two sequentially).
- US6: T036, T037, T040 are different files and can be done together.
- US5: T051, T052, T055 in parallel.

## Implementation Strategy

- **MVP**: Phase 1 + Phase 2 + US1 (fixes the wallet-scoping privacy defect), then US2 and US3 to close all three reported issues; ship as the first PR if desired.
- **Increment 2**: US6 + US4 (Activity and compact dashboard).
- **Increment 3**: US5 + US7 (Send and Pending), then Polish.
- After each phase run the matching quickstart scenario and `dotnet test tests/DigiByte.Wallet.Tests`.
