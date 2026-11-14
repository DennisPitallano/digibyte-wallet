---

description: "Task list for Your Addresses on the Receive Page"
---

# Tasks: Your Addresses on the Receive Page

**Input**: Design documents from `/specs/012-receive-address-list/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/receive-address-list-ui.md, quickstart.md

**Tests**: Included. Constitution principle III requires tests in the same PR for any change to `DigiByte.Wallet`, written first and failing before the code change. The page work is checked by hand in the preview (quickstart).

**Organization**: Grouped by user story. US1 is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 to US5 from spec.md
- All paths are relative to the worktree root. Work on branch `feature/receive-address-list` (not `main`, not the spec 010 branch).

## Phase 1: Setup

- [x] T001 Confirm the branch is `feature/receive-address-list`, then run `dotnet build` and `dotnet test tests/DigiByte.Wallet.Tests` to record the baseline; the live-explorer benchmark tests fail without network access and are expected to
- [x] T002 [P] Read `src/DigiByte.Web/Pages/Receive.razor` and `src/DigiByte.Wallet/Services/ActivityAddressIndex.cs` end to end and note the page state (`address`, `addressLabel`, `requestAmount`, `requestNote`) that switching must keep or reset

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The helper every story reads. Status logic with a loaded history comes in US2; here the list is built with every status `Unknown`.

- [x] T003 [P] Create `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs` with failing tests for `ReceiveAddressList.NextWindow(currentCount, generatedCount)` ("min(currentCount + 10, generatedCount)") and `WindowStart(currentIndex, shownCount)` ("never below 0"), including `generatedCount` smaller than 10 and exactly 10
- [x] T004 [P] In `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs`, add failing tests for `ReceiveAddressList.Build(...)`: entries keep the newest-first order given; `Label` comes from the label lookup (normalized key); `IsOnShow` is set only on the entry matching the on-show address compared in normalized form (bech32 checksum variants match); empty input returns an empty list; `history` null gives `Status = Unknown` for every entry
- [x] T005 Create `src/DigiByte.Wallet/Services/ReceiveAddressList.cs` with `ReceiveAddressStatus` (`Used`, `Unused`, `Unknown`), `ReceiveAddressEntry` (Index, Address, Label, Status, TransactionCount, IsOnShow per data-model.md), `NextWindow`, `WindowStart`, `Build` and `StatusOf(address, history)` (the status and count of one address, so the privacy notice can use the address on show without deriving the list; history null means every status is `Unknown`; "Unknown is never presented as Unused")
- [x] T006 Add a test in `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs`, using the wallet test environment (`using static DigiByte.Wallet.Tests.ChangeAddressTests;`, see `tests/DigiByte.Wallet.Tests/ReceiveAddressTests.cs`), that pins research R1: `GetReceivingAddressesAsync(count, startIndex)` over indexes 0 to `NextReceivingIndex` gives the address `GetReceivingAddressAsync()` returns as the last one, and after `GenerateNewReceivingAddressAsync()` the range is exactly one longer with the new address last

**Checkpoint**: Helper and window math ready and tested.

---

## Phase 3: User Story 1 - See and switch between my receiving addresses (Priority: P1) 🎯 MVP

**Goal**: A "Your addresses" list on Receive (HD wallets): the newest 10, "Show more" adds 10, choosing one puts it in the QR code, copy, share and download.

**Independent Test**: Generate three addresses, open Receive, open the folded "Your addresses" row, choose the first in the list, and confirm the QR code, copy and share carry it; the amount entered earlier is kept.

### Implementation for User Story 1

- [x] T007 [US1] In `src/DigiByte.Web/Pages/Receive.razor`, for HD wallets only (`!isWifWallet`), add the list state: `ShownCount` "Starts at 10; 'Show more' adds 10 until Index 0 is listed, then the control is gone" and `OnShowIndex` "starts at the newest (current) index each visit; changing it is never saved"; derive the newest 10 with `WalletService.GetReceivingAddressesAsync(count, startIndex)` using `ReceiveAddressList.WindowStart` the first time the folded row is opened (research R9), and keep derived addresses for the visit so "Show more" never derives the same index twice
- [x] T008 [US1] In `src/DigiByte.Web/Pages/Receive.razor`, render a folded "Your addresses (N)" row under the QR card (folded on every visit, tap to open, open or closed state not saved, N = number of generated addresses) and, when opened, the list: newest first, shortened address, the entry on show marked, a "Show more" button that disappears once index 0 is listed; hide the whole list when the wallet is single-key (the legacy/SegWit switch stays as it is)
- [x] T009 [US1] In `src/DigiByte.Web/Pages/Receive.razor`, make choosing an entry set the on-show address, rebuild the QR code and the payload (so copy, share and download use it) and reload its name into the name box, leaving `requestAmount` and `requestNote` untouched and writing nothing to storage or to `NextReceivingIndex`
- [x] T010 [US1] In `src/DigiByte.Web/Pages/Receive.razor`, keep the page opening on the newest address (today's `GenerateAddress` behavior) and check that leaving and returning to Receive after switching shows the newest address again (FR-012)

**Checkpoint**: US1 works alone and is demoable.

---

## Phase 4: User Story 2 - Know which addresses are unused or used (Priority: P1)

**Goal**: Each listed address shows used (with its transaction count), unused, or unknown, from history the wallet already loads.

**Independent Test**: With one paid and one untouched address, each shows the right status; with no history available every status is unknown, none unused.

### Tests for User Story 2 (write first, must fail)

- [x] T011 [P] [US2] In `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs`: with a loaded history, an address found in `WalletAddresses` or `WalletReceivedAddresses` is `Used` with the right `TransactionCount`; an address not found is `Unused`; `StatusOf(address, history)` gives the same status and count as the matching entry from `Build`
- [x] T012 [P] [US2] In `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs`: a bech32 checksum variant of a listed address in the history still marks it `Used` (counted once); an empty loaded history (empty list, not null) gives `Unused` for every entry while a null history gives `Unknown`
- [x] T013 [P] [US2] In `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs`: records that carry no wallet address data (older cached records) leave the status as `Unused` only if at least one record in the history has address data, otherwise `Unknown` (research R3 known limit)

### Implementation for User Story 2

- [x] T014 [US2] Extend `ReceiveAddressList.Build` in `src/DigiByte.Wallet/Services/ReceiveAddressList.cs` and `StatusOf` with the status rules from data-model.md, reusing `ActivityAddressIndex.Build` for the counts
- [x] T015 [US2] In `src/DigiByte.Web/Pages/Receive.razor`, inject `ISecureStorage`, read the cached history `cache:transactions:<walletId>` once for the visit when the page opens (not only when the list is opened, so the privacy notice can use it), and show a status label per entry ("Used · N", "Unused", or a muted "Status unknown" while the status is not known; it is replaced when the history arrives); the list and the QR code must appear without waiting for this
- [x] T016 [US2] In `src/DigiByte.Web/Pages/Receive.razor`, when the cache is missing or holds no address data, load the history once in the background with `WalletService.GetTransactionHistoryAsync(0, 1000)` (its own 30 second cache applies), refresh the status labels when it returns, and use the result only when it carries wallet-address data (an empty or data-less answer cannot be told apart from an unreachable explorer, so every status stays `Unknown`, see research R13); make no other network request
- [x] T017 [US2] In `src/DigiByte.Web/Pages/Receive.razor`, show a short non-blocking notice under the QR code ("This address has been used before. A new address is better for privacy.") only when the address on show has status `Used`; it must not be shown for `Unused` or `Unknown`, takes the status of the address on show from `ReceiveAddressList.StatusOf` and the cached history directly (so it works while the list is folded and was never opened, without deriving any list entry), must update when the user switches address, and must never block or delay copy, share or download (FR-013)

**Checkpoint**: US1 and US2 work independently.

---

## Phase 5: User Story 3 - Name an address (Priority: P2)

**Goal**: Names can be set, changed and removed from the list; they are the same names as on Activity, and the existing name box under the QR code stays and edits the same name.

**Independent Test**: Name an address in the list, then see it in the box under the QR code and in the Activity picker; rename it on Activity and see it here.

- [x] T018 [US3] In `src/DigiByte.Web/Pages/Receive.razor`, load the whole label store once with `AddressLabelService.GetAllAsync(walletId)`, build the lookup with `ActivityAddressIndex.BuildLabelLookup`, and show each name in its list entry (long names shortened in the list, full in the editor)
- [x] T019 [US3] In `src/DigiByte.Web/Pages/Receive.razor`, add a pencil per entry that opens an inline name editor (Enter saves, Escape cancels, an empty name removes the label, the text box takes focus when it opens, and the name is limited to 50 characters like the existing name box under the QR code), using `AddressLabelService.SetLabelAsync(walletId, entry.Address, text)` with the address as generated, then refresh the lookup and the list
- [x] T020 [US3] In `src/DigiByte.Web/Pages/Receive.razor`, make the existing name box under the QR code (`SaveLabel`, `ClearLabel`) refresh the same lookup after it saves, and make the box show the new name when a name is set from the list for the address on show

**Checkpoint**: US3 works with US1.

---

## Phase 6: User Story 4 - Generate a new address (Priority: P2)

**Goal**: "Generate new address" still works and fits the list; the limit and save-failure messages are unchanged.

**Independent Test**: Generate a new address and see it on top and shown; with the limit reached see the existing message and an unchanged list.

- [x] T021 [US4] In `src/DigiByte.Web/Pages/Receive.razor`, after `GenerateNewReceivingAddressAsync()` succeeds add the new address as the newest entry, set it as on show, and keep `ShownCount` so the visible window grows by one; on `InvalidOperationException` keep today's inline message, and leave the list and the on-show address unchanged
- [x] T022 [P] [US4] Confirm the existing tests that cover the limit and the save failure still pass unchanged (`tests/DigiByte.Wallet.Tests/AddressDiscoveryReliabilityTests.cs`, `tests/DigiByte.Wallet.Tests/ReceiveAddressTests.cs`); add a test in `tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs` only if none checks that a failed generate leaves `NextReceivingIndex` unchanged

**Checkpoint**: US4 works with US1.

---

## Phase 7: User Story 5 - Open Activity for an address (Priority: P3)

**Goal**: A used address can open Activity filtered to it; the action is hidden on unused and unknown addresses.

**Independent Test**: Choose "View activity" on a used address and see Activity filtered to it; unused addresses have no such action.

- [x] T023 [US5] In `src/DigiByte.Web/Pages/Receive.razor`, show a "View activity" action only on entries whose status is `Used`; it writes `pref:activityAddress:<walletId>` (the address as generated) to `ISecureStorage` and navigates to `/transactions` with `NavigationManager`; storage errors are ignored and the navigation still happens
- [x] T024 [US5] Verify in the preview that Activity opens with that address chosen (the restore in `src/DigiByte.Web/Pages/Transactions.razor` only applies when the address still appears in the history); no change to `Transactions.razor` is expected

**Checkpoint**: All five stories work.

---

## Phase 8: Polish & Cross-Cutting

- [x] T025 [P] Check `docs/` (ARCHITECTURE.md, PROCESS_FLOWS.md) and the Help page for a description of the Receive screen and update it if one exists
- [x] T026 [P] Check the locale resource files and `LocaleParityTests` for any new UI strings ("Your addresses", "Show more", "Used", "Unused", "View activity", "Name this address") and add them if the page uses localized text; the Receive page may use plain English like the surrounding code
- [x] T027 Run `dotnet build` and `dotnet test tests/DigiByte.Wallet.Tests`; only the live-explorer benchmark tests may fail
- [x] T028 Check a watch-only (xpub) wallet on Receive in the preview (the page treats it as an HD wallet): the list shows, switching works, and generating a new address works or behaves exactly as Receive does today for that wallet; fix or hide the list for that wallet type if derivation is not possible, and note the outcome in `research.md`; also try a multisig wallet if one can be created, and note what Receive does for it
- [x] T029 Run the manual steps in `specs/012-receive-address-list/quickstart.md` in the preview (HD wallet, single-key wallet, phone width 375 px, no network); confirm the page makes no new network request and logs no addresses (FR-010), that 50 generated addresses are usable in under 3 seconds (SC-004), that any generated address can be put in the QR code within 2 taps (newest 10) or 3 taps (after "Show more") from opening Receive (SC-001), and that a wallet with only one generated address shows that one address and nothing looks broken (story 1, scenario 5), and that switching to an older address and reloading leaves the newest address as the wallet's current one (FR-011)
- [x] T030 Commit locally with Conventional Commits (`feat(web): ...`); do not push or open a PR until asked

---

## Dependencies & Execution Order

- Setup (Phase 1) then Foundational (Phase 2, blocks the stories).
- US1 needs Foundational. US2 needs Foundational and the list from US1 to show status labels (its tests T011 to T013 only need Foundational). US3 needs the list from US1. US4 needs the list from US1. US5 needs US2 (it needs the `Used` status). The privacy notice (T017) needs the status from T015 and T016 and the switching from T009.
- Within a story: tests first and failing, then the helper, then the page.
- T007 to T010 edit the same file in order; T011 to T013 can be written together; T018 and T021 touch the same file as T007 to T010, so they run after US1.

## Parallel Examples

- T003 and T004 (one test file, written together) while T002 reads the page.
- T011, T012 and T013 together.
- T022 and T025/T026 in parallel with page work.

## Implementation Strategy

1. MVP: Phases 1 to 3 (US1). Stop and check against the independent test in the preview.
2. Add US2 (status), then US3 (names), US4 (generate), US5 (view activity); each is demoable on its own.
3. Polish and the manual quickstart last. Do not implement until the spec, plan and tasks are agreed.
