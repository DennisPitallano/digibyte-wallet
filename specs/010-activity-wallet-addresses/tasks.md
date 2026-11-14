---

description: "Task list for Wallet Addresses in Activity"
---

# Tasks: Wallet Addresses in Activity

**Input**: Design documents from `/specs/010-activity-wallet-addresses/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/activity-addresses-ui.md, quickstart.md

**Tests**: Included. Constitution principle III requires tests in the same PR for any change to `DigiByte.Wallet`, written first and failing before the code change.

**Organization**: Grouped by user story. US1 is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 from spec.md

## Phase 1: Setup

- [x] T001 Create branch `feature/activity-wallet-addresses` from main (the spec and plan use this same name) (commit locally only; do not push until asked)
- [x] T002 Run `dotnet build` and `dotnet test tests/DigiByte.Wallet.Tests` to record the baseline before changes

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The data the three stories all read.

- [x] T003 [P] Write failing test in `tests/DigiByte.Wallet.Tests/TransactionHistoryAddressTests.cs` that old stored `TransactionRecord` JSON (no new members) deserializes with both lists empty (write before T004)
- [x] T004 Add optional members `WalletAddresses` (List<string>, default empty) and `WalletReceivedAddresses` (List<string>, default empty) to `TransactionRecord` in `src/DigiByte.Wallet/Models/WalletInfo.cs`. Constraints from data-model.md: "No duplicates (compared in normalized form). Never contains change-only addresses (FR-007)."; `WalletReceivedAddresses` is "Sent transactions only" and "Empty for received transactions and ordinary sends". Old JSON without these members must still deserialize.
- [x] T005 Add a way for `WalletService` to expose the wallet's normalized change addresses (same derivation cache as `GetAllAddresses`, keyed the same way) in `src/DigiByte.Wallet/Services/WalletService.cs`; single-key wallets treat own outputs of a send as change per research.md R3

**Checkpoint**: Model and change-address set ready.

---

## Phase 3: User Story 1 - See which of my addresses a transaction used (Priority: P1) 🎯 MVP

**Goal**: Each row shows the wallet address (or label) with "+N more"; the details dialog shows all, copyable.

**Independent Test**: Receive to two generated addresses; each row shows its own address; details show the full address with a copy action.

### Tests for User Story 1 (write first, must fail)

- [x] T006 [P] [US1] Test in `tests/DigiByte.Wallet.Tests/TransactionHistoryAddressTests.cs`: received transaction paying one own address sets `WalletAddresses` to that address
- [x] T007 [P] [US1] Test in the same file: received transaction paying several own addresses lists them all, first paid address first
- [x] T008 [P] [US1] Test in the same file: sent transaction funded from several own addresses lists funding addresses; change outputs are NOT in `WalletAddresses`
- [x] T009 [P] [US1] Test in the same file: transfer between own addresses puts funding address(es) in `WalletAddresses` and the non-change receiving address in `WalletReceivedAddresses`
- [x] T010 [P] [US1] Test in the same file: a locally tracked send that lacks addresses gets them copied from the matching explorer transaction after merge; with no explorer data both lists stay empty and nothing throws
- [x] T010a [P] [US1] Test in the same file: addresses copied onto a local record are saved, so a reload with the explorer unreachable still shows them
- [x] T011 [P] [US1] Test in the same file: bech32 variants of the same address (different checksum) are de-duplicated
- [x] T011a [P] [US1] Test in the same file: a single-key (WIF) wallet with both legacy and SegWit addresses lists the right own address for received and sent transactions, and its change is not listed
- [x] T012 [P] [US1] Create `tests/DigiByte.Wallet.Tests/ActivityAddressIndexTests.cs` with failing tests for `Display(transaction, labels)`: label preferred over address, `MoreCount` = other wallet addresses, `Primary` null when none known

### Implementation for User Story 1

- [x] T013 [US1] In `GetTransactionHistoryAsync` in `src/DigiByte.Wallet/Services/WalletService.cs`, fill `WalletAddresses` / `WalletReceivedAddresses` for explorer-built records using the existing `isOurs` pass and the change set from T005 (no extra network calls)
- [x] T014 [US1] In the same method, after the explorer/local merge, copy the address members from the explorer record onto local records that have none (research.md R2), and save them through `TransactionTracker` (`src/DigiByte.Wallet/Services/TransactionTracker.cs`) so they survive a reload when the explorer is unreachable
- [x] T015 [US1] Create `src/DigiByte.Wallet/Services/ActivityAddressIndex.cs` with `Display(...)` (normalize via the wallet's existing address normalization; display form kept as generated)
- [x] T016 [P] [US1] Add optional parameters `WalletAddressText` and `WalletAddressMore` to `src/DigiByte.Web/Components/Shared/TransactionRow.razor`; render one short line (label, else shortened address, plus "+N more") only when the text is set
- [x] T017 [US1] Resolve labels and pass the new row parameters from `src/DigiByte.Web/Pages/Transactions.razor` and `src/DigiByte.Web/Pages/Home.razor` (labels via `AddressLabelService`)
- [x] T018 [P] [US1] In `src/DigiByte.Web/Components/Shared/TransactionDetailModal.razor`, list every wallet address (and received-to address) with label, full address and a copy action (FR-003)

**Checkpoint**: US1 works alone and is demoable.

---

## Phase 4: User Story 2 - Browse all used addresses (Priority: P2)

**Goal**: A picker in the activity filter bar lists every used address with label and count.

**Independent Test**: After receiving to three addresses, the picker shows all three with correct counts; an unused address is absent.

### Tests for User Story 2 (write first, must fail)

- [x] T019 [P] [US2] In `tests/DigiByte.Wallet.Tests/ActivityAddressIndexTests.cs`: `Build` returns one entry per normalized address, counts both lists, sorted newest first
- [x] T020 [P] [US2] Same file: change-only and unreferenced addresses never appear; pending transactions count
- [x] T021 [P] [US2] Same file: empty history and records with no address data return an empty list without throwing

### Implementation for User Story 2

- [x] T022 [US2] Add `UsedAddress` and `Build(transactions, labels)` to `src/DigiByte.Wallet/Services/ActivityAddressIndex.cs` per data-model.md
- [x] T023 [US2] Add the address picker (label, shortened address, count; sheet-like on phones) to the filter bar in `src/DigiByte.Web/Pages/Transactions.razor`, hidden when there are no used addresses

**Checkpoint**: US1 and US2 work independently.

---

## Phase 5: User Story 3 - Filter activity by address (Priority: P2)

**Goal**: Choosing an address narrows the list; it combines with All/Received/Sent and search; one action clears it. Depends on the picker from US2 (T023), which is how an address is chosen.

**Independent Test**: With transactions on two addresses, pick one and only its transactions remain; clear it and all return.

### Tests for User Story 3 (write first, must fail)

- [x] T024 [P] [US3] In `tests/DigiByte.Wallet.Tests/ActivityAddressIndexTests.cs`: `Matches` is true for null/empty key, true when the key is in either list, false otherwise, encoding-independent
- [x] T025 [P] [US3] Same file: self-transfer matches the filter for both its funding and receiving address

### Implementation for User Story 3

- [x] T026 [US3] Add `Matches(transaction, addressKey)` to `src/DigiByte.Wallet/Services/ActivityAddressIndex.cs`
- [x] T027 [US3] In `src/DigiByte.Web/Pages/Transactions.razor` add `selectedAddressKey` state, apply it in `ApplyFilter()` together with the existing filter and search (AND), and add a one-action clear control plus the empty-state clear (FR-005, FR-006)
- [x] T028 [US3] Extend the search predicate in `ApplyFilter()` in `src/DigiByte.Web/Pages/Transactions.razor` to also match the wallet addresses and their labels (FR-009)

**Checkpoint**: All three stories work.

---

## Phase 6: Polish & Cross-Cutting

- [x] T029 [P] Check `docs/` (ARCHITECTURE.md, PROCESS_FLOWS.md) for a description of the activity screen and update it if one exists
- [x] T030 [P] Check locale resource files for UI strings and add any new labels ("used addresses", "+N more", clear filter) so `LocaleParityTests` still passes
- [x] T031 Verify SC-004: add a test building 500 transactions / 100 addresses through `Build` and `Matches` and assert it completes in under 1 second (SC-004), using a generous limit so slow machines do not fail it
- [x] T032 Run `dotnet build` and `dotnet test` for all affected test projects; fix regressions
- [ ] T033 Run the manual walkthrough in `specs/010-activity-wallet-addresses/quickstart.md` at phone and desktop widths, and confirm in the browser network and console output that the feature makes no new network requests and logs no addresses (FR-010)
- [ ] T034 Commit locally with Conventional Commits (`feat(web): ...`); do not push until asked

---

## Dependencies & Execution Order

- Setup (P1) → Foundational (P2) → user stories.
- US1 needs Foundational. US2 needs the helper file created in US1 (T015) but not US1's UI. US3's logic (T024–T026) needs only T015, but its page work (T027–T028) needs the picker from US2 (T023), so US3 follows US2.
- Within each story: tests first and failing, then code, then UI.
- T013 and T014 are the same method and run in order; T016 and T018 touch different files and are parallel.

## Parallel Examples

- US1 tests T006–T012 can be written together (two files).
- After T015: T016 and T018 in parallel; T019–T021 and T024–T025 in parallel.

## Implementation Strategy

1. MVP: Phases 1–3 (US1). Stop and check against the independent test.
2. Add US2 (picker), then US3 (filtering); each is demoable on its own.
3. Polish and the manual quickstart last.
