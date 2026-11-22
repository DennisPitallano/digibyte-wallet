---

description: "Task list for First-Receive Flow"
---

# Tasks: First-Receive Flow

**Input**: Design documents from `/specs/025-first-receive-flow/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/first-receive.md](contracts/first-receive.md), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution Principle III makes tests mandatory for every change to `DigiByte.Wallet/`: written first, failing before the code change, and covering negative paths. There is no web test project, so web-only changes are checked in the browser preview with quickstart.md.

**Organization**: Tasks are grouped by user story (US1–US4 from spec.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1–US4)

## Path Conventions

- Wallet logic: `src/DigiByte.Wallet/Services/`
- Web app: `src/DigiByte.Web/` (Pages, Components, Services, Layout, wwwroot, Program.cs)
- Tests: `tests/DigiByte.Wallet.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The branch, the value types and the text every later phase uses

- [X] T001 Create the feature branch `feature/first-receive-flow` from `main` and commit the existing `specs/025-first-receive-flow/` directory to it.
- [X] T002 [P] Create the value types in src/DigiByte.Wallet/Services/FirstReceiveRule.cs (types only, `Evaluate` throws `NotImplementedException`): `enum FirstReceiveState { Undecided, Empty, History, Celebrated }`, `enum HistorySource { CompleteLoad, PartialAddressLoad, IncompleteLoad, SavedSnapshot }`, `record FirstPayment(decimal AmountDgb, bool IsPending, IReadOnlyList<string> TxIds)`, `record FirstReceiveOutcome(FirstReceiveState State, FirstPayment? Payment)`, per contracts/first-receive.md.
- [X] T003 [P] Add the new translation keys to src/DigiByte.Web/wwwroot/locales/en.json and src/DigiByte.Web/wwwroot/locales/es.json (same keys in both, Spanish translated, not copied):
  - `home.empty.copy` ("Copy address"), `home.empty.copied`, `home.empty.receive_link` ("Open Receive"), `home.empty.qr_alt` ("QR code for your receiving address")
  - `activity.empty.receive` ("Receive")
  - `firstreceive.title` ("Your first DGB"), `firstreceive.received` ("{0} DGB received"), `firstreceive.pending` ("{0} DGB is on its way"), `firstreceive.received_hidden` ("Payment received"), `firstreceive.pending_hidden` ("A payment is on its way"), `firstreceive.done` ("Done"), `firstreceive.install` ("Install app")

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The first-receive state, its storage and the web coordinator that US2, US3 and US4 all call

**⚠️ CRITICAL**: US2, US3 and US4 cannot start until this phase is complete. **US1 does not depend on it** and can start after Phase 1.

### Tests (write first, must fail)

- [X] T004 [P] Write tests/DigiByte.Wallet.Tests/FirstReceiveRuleTests.cs covering every row of the transition table in data-model.md:
  - `Undecided` + `CompleteLoad`, no transactions → `Empty`, no payment
  - `Undecided` + `CompleteLoad`, with transactions → `History`, no payment
  - `Undecided` + `IncompleteLoad` (empty and non-empty) → `Undecided`
  - `Undecided` + `SavedSnapshot` with transactions → `History`; without → `Undecided`
  - `Empty` + `CompleteLoad`, no transactions → `Empty`
  - `Empty` + `CompleteLoad`, one unconfirmed receipt → `Empty`, payment with that amount, `IsPending` true, that TxId
  - `Empty` + `CompleteLoad`, two receipts, one confirmed → one payment, amount = sum, `IsPending` false, both TxIds
  - `Empty` + `CompleteLoad`, a receipt confirmed days ago with many confirmations → payment (age does not matter)
  - `Empty` + `CompleteLoad`, transactions but none with direction Received and amount above zero → `History`, no payment
  - `Empty` + `IncompleteLoad` containing a receipt → `Empty`, no payment
  - `Empty` + `SavedSnapshot` containing a receipt → `Empty`, no payment ("a saved snapshot can only cause undecided → history")
  - `History` and `Celebrated` + any source with a new receipt → unchanged, no payment
  - `Undecided` + `PartialAddressLoad`, no transactions → `Undecided` (the wallet's addresses are not all known yet, so "empty" is not proven)
  - `Undecided` + `PartialAddressLoad`, with transactions → `History`
  - `Empty` + `PartialAddressLoad` with a receipt → payment, exactly as for `CompleteLoad`
  - `Evaluate` never returns `Celebrated` from any other state
- [X] T005 [P] Write tests/DigiByte.Wallet.Tests/FirstReceiveStoreTests.cs against an in-memory `ISecureStorage`:
  - no key reads as `Undecided`; an unreadable or unknown stored value reads as `Undecided`
  - `SetAsync` round-trips `Empty`, `History`, `Celebrated` as the stored values `empty`, `history`, `celebrated`
  - `SetAsync(Undecided)` writes nothing
  - `SetAsync(Empty)` on a wallet already `History` or `Celebrated` leaves it unchanged; `SetAsync(Celebrated)` is accepted only from `Empty`
  - `StateKey("abc")` is `firstReceive:abc`
  - `IsFirstSessionEndedAsync` is false until `EndFirstSessionAsync`, which stores `1` under `firstSession:ended` and is idempotent
  - `DeleteAsync` removes the wallet's key and leaves `firstSession:ended` in place
- [X] T006 [P] Add a test to tests/DigiByte.Wallet.Tests/MultiWalletTests.cs: after `WalletService.DeleteWalletAsync(walletId)`, storage has no `firstReceive:{walletId}` key, another wallet's `firstReceive:` key is untouched, and `firstSession:ended` is still present.
- [X] T007 [P] Write tests/DigiByte.Wallet.Tests/HistoryLoadTests.cs for a new `WalletService.LoadTransactionHistoryAsync(skip, take)` that returns `HistoryLoad(Transactions, Complete, AddressesSettled)`, using the `SummaryExplorer` / `HistorySyncEnv` doubles:
  - a load that reads the chain returns `Complete` true; one whose explorer read throws returns `Complete` false with only the locally tracked transactions
  - **two overlapping loads, the first failing and the second succeeding (and the reverse)**: each caller gets its own `Complete` value, whatever order they finish in (today the shared `LastHistoryIncomplete` property is reset by the second call, so the first caller can read a failed load as complete)
  - `AddressesSettled` is false while address discovery for the session has not finished or has failed, and true once it has completed; always true for private-key wallets, which have no discovery; a watch-only wallet has an HD key and address discovery like a seed wallet, so it follows the same rule (cover it with a watch-only session)
  - `GetTransactionHistoryAsync` still returns the same list as before and still sets `LastHistoryIncomplete`, so existing callers are unaffected

### Implementation

- [X] T008 Implement `FirstReceiveRule.Evaluate` in src/DigiByte.Wallet/Services/FirstReceiveRule.cs until T004 passes. A "DGB receipt" is a `TransactionRecord` with `Direction == TransactionDirection.Received` and an amount above zero; `IsPending` is true only when every receipt in the load has 0 confirmations.
- [X] T009 Implement `FirstReceiveStore` in src/DigiByte.Wallet/Services/FirstReceiveStore.cs (`GetAsync`, `SetAsync`, `DeleteAsync`, `IsFirstSessionEndedAsync`, `EndFirstSessionAsync`, `static StateKey`) until T005 passes. All storage calls are wrapped so a storage failure reads as `Undecided` / not ended and never throws to the caller.
- [X] T010 In `WalletService.DeleteWalletAsync(string walletId)` in src/DigiByte.Wallet/Services/WalletService.cs, also remove `FirstReceiveStore.StateKey(walletId)` next to the existing `WalletSnapshotKeys` removal, until T006 passes.
- [X] T011 In src/DigiByte.Wallet/Services/WalletService.cs, add `record HistoryLoad(List<TransactionRecord> Transactions, bool Complete, bool AddressesSettled)` and `LoadTransactionHistoryAsync(int skip = 0, int take = 50)`, which carries completeness in a local variable for that call; make `GetTransactionHistoryAsync` call it and set `LastHistoryIncomplete` from its result, until T007 passes. `AddressesSettled` reads the session's address-discovery task: true when it has completed successfully, or when the session has no HD key (a private-key wallet); false while it is running, not started or failed. Seed and watch-only sessions both have an HD key and both use this rule. No change to what is fetched.
- [X] T012 Create `FirstReceiveCoordinator` in src/DigiByte.Web/Services/FirstReceiveCoordinator.cs per contracts/first-receive.md: `InitializeAsync` (loads the marker only, for now), `MarkCreatedEmptyAsync`, `ObserveAsync` (applies `FirstReceiveRule`, saves a changed state, calls `EndFirstSessionAsync` when `transactions` is not empty, stores and returns a pending `FirstPayment`), `Pending(walletId)`, `MarkShownAsync` (state → `Celebrated`, clears pending), `FirstSessionEnded`, `OtherDialogOpen`, `event Action? Changed`. Serialise `ObserveAsync` and `MarkShownAsync` per wallet with a `SemaphoreSlim`. Register it as scoped in src/DigiByte.Web/Program.cs together with `FirstReceiveStore`.
- [X] T013 In src/DigiByte.Web/Layout/MainLayout.razor, inject the coordinator, call `InitializeAsync()` once in `OnInitializedAsync`, and set `OtherDialogOpen` whenever `showVersionInfo` changes (opened by the badge, opened by the automatic "What's New", closed).

**Checkpoint**: `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~FirstReceive|FullyQualifiedName~HistoryLoad"` passes; the app builds and behaves exactly as before.

---

## Phase 3: User Story 1 - Share an address straight from an empty Home (Priority: P1) 🎯 MVP

**Goal**: No introduction slides; the empty Home shows the QR code, address, Copy address and a Receive link; Activity's empty state has a Receive button.

**Independent Test**: Create a new wallet, arrive on Home, and confirm that with no dismissals the QR code, the address and Copy address are visible, that Copy address puts the full receiving address on the clipboard, and that the link opens the Receive page (quickstart.md, section 2, steps 1 to 5).

### Implementation for User Story 1

- [X] T014 [US1] Remove the introduction slides (FR-001, FR-002): delete src/DigiByte.Web/Components/Shared/OnboardingWalkthrough.razor and the `<OnboardingWalkthrough />` line in src/DigiByte.Web/Pages/Home.razor. Leave the `dgb-onboarding-done` browser key alone.
- [X] T015 [P] [US1] Create src/DigiByte.Web/Components/Wallet/ReceiveEmptyState.razor per the UI contract: parameter `Address`; renders nothing while it is empty; QR code generated with QRCoder at ECC level H (the same call as `ShowAddressQr` in Home.razor) with alt text from `home.empty.qr_alt`; the full address as selectable monospace text that wraps; a **Copy address** button using `ClipboardService` and the existing copied toast; a link to `/receive` with text from `home.empty.receive_link`. Inject `LocalizationService` for the text. No explanatory sentence of its own. Regenerate the QR code when `Address` changes.
- [X] T016 [US1] In src/DigiByte.Web/Pages/Home.razor, replace the non-failed branch of the `transactions.Count == 0` block (the grey "No transactions yet / Send or receive DGB to get started" card) with `<ReceiveEmptyState Address="@receivingAddress" />`, keeping `<BakuMessage MessageId="home.empty" />` above it and keeping the `fetchFailed` "Unable to load data" card with **Try again** exactly as it is (FR-003, FR-004, FR-006 to FR-009). Confirm it renders for watch-only and private-key wallets.
- [X] T017 [P] [US1] Reword the `home.empty` entry in src/DigiByte.Web/Services/BakuCatalog.cs (same ID, severity and pose): title "Get your first DGB", body "Share this address or QR code to receive." English only (FR-005, FR-032).
- [X] T018 [P] [US1] In src/DigiByte.Web/Pages/Transactions.razor, add a **Receive** button (text from `activity.empty.receive`, navigates to `/receive`) to the empty state only when `all.Count == 0`, the load did not fail and no search or address filter is active; no QR code there (FR-010). Inject `LocalizationService` for that one string.
- [ ] T019 [US1] Preview check: run quickstart.md section 2, steps 1 to 5, at phone width in light and dark mode, including the mascot set to Off and the network turned off. Fix anything that fails.

**Checkpoint**: US1 is shippable on its own.

---

## Phase 4: User Story 2 - The first payment is celebrated once (Priority: P1)

**Goal**: The first incoming payment to a wallet that started empty shows a one-time celebration on Home, Receive or Activity; Receive finds the payment by itself.

**Independent Test**: With a new, empty wallet, send it a payment from another wallet. Confirm the celebration appears once with the right amount, closes on request, and does not appear again after further payments, a reload, or locking and unlocking (quickstart.md, section 2, steps 6 to 15).

### Tests for User Story 2 (write first, must fail)

- [X] T020 [P] [US2] Write tests/DigiByte.Wallet.Tests/NewArrivalsTests.cs for `NewArrivals.Find(known, transactions, utcNow)`: `known == null` returns nothing; a received transaction not in `known` with 2 or fewer confirmations is returned; one with more than 2 confirmations is returned only if newer than 15 minutes; sent transactions are never returned; a transaction already in `known` is never returned.
- [X] T021 [P] [US2] Write tests/DigiByte.Wallet.Tests/RefreshPacerTests.cs: the first tick checks; after `Report(false)` repeatedly the gaps between checks are 1, 2, 4, 4 ticks (15 s, 30 s, 60 s, 60 s); `Report(true)` returns to 1 tick; `Reset()` returns to 1 tick and makes the next tick check.

### Implementation for User Story 2

- [X] T022 [P] [US2] Implement `NewArrivals` in src/DigiByte.Wallet/Services/NewArrivals.cs by moving the selection rule out of `AnnounceNewReceivedAsync` in Home.razor unchanged, until T020 passes.
- [X] T023 [P] [US2] Implement `RefreshPacer` in src/DigiByte.Wallet/Services/RefreshPacer.cs (`ShouldCheck`, `Report`, `Reset`) by moving the `_idleStreak` / `_ticksUntilNextFetch` logic out of Home.razor's poller unchanged, until T021 passes.
- [X] T024 [US2] Create `AutoRefreshLoop` in src/DigiByte.Web/Services/AutoRefreshLoop.cs per the contract (`Start(Func<Task<bool>>)`, `Stop`, `NotifyManualRefresh`; 15 s `System.Threading.Timer`; skip a tick when a check is running, when `domHelpers.isHidden` is true, or when the pacer says no; swallow exceptions from the check). Register it as transient in src/DigiByte.Web/Program.cs, since each page owns its own loop.
- [X] T025 [US2] Create `IncomingPaymentAnnouncer` in src/DigiByte.Web/Services/IncomingPaymentAnnouncer.cs (`AnnounceAsync(walletId, transactions, hideAmounts, celebratedTxIds)` shows the existing success toast text and the `domHelpers.haptic` call from Home, excluding `celebratedTxIds`). Register it as **transient** in src/DigiByte.Web/Program.cs: each page that checks owns one, so its list of seen transaction IDs lives exactly as long as Home's `knownTxIds` does today. It starts empty when the page opens, the first list is recorded without announcing, and it is cleared when `walletId` differs from the previous call (a wallet switch). Do not keep the list across pages, locks or wallet switches: a payment that arrived while the page was closed must stay silent, as it is today.
- [X] T026 [US2] Switch src/DigiByte.Web/Pages/Home.razor to the shared pieces with no behaviour change: replace `_autoRefreshTimer`, `_idleStreak`, `_ticksUntilNextFetch` and `OnAutoRefreshTickAsync` with an `AutoRefreshLoop` (its check returns whether `_lastDataSignature` changed; manual refresh and pull-to-refresh call `NotifyManualRefresh`); replace `knownTxIds` and `AnnounceNewReceivedAsync` with the announcer (the two places that set `knownTxIds = null` on a wallet switch are covered by the announcer clearing its list when the wallet ID changes). Preview check before continuing: Home still refreshes every 15 s, pauses in a hidden tab, backs off when quiet, and shows the "payment received" toast once per payment.
- [X] T027 [P] [US2] Create `InstallAppState` in src/DigiByte.Web/Services/InstallAppState.cs by moving `InstallMode`, `InstallState` and `ComputeInstallMode` out of src/DigiByte.Web/Pages/Settings.razor unchanged, plus `Task<InstallMode> GetModeAsync()` reading `pwaInstall.getState`. Register it as scoped in Program.cs. Update Settings.razor to use it, and make Settings open its existing steps dialog when the URL has `?install=1` (then remove the query from the address bar). Preview check: the Install app entry behaves as before.
- [X] T028 [P] [US2] Add the `receive.first` mascot entry ("Your first DGB has arrived!", info severity, the `approve` pose) to src/DigiByte.Web/Services/BakuCatalog.cs, and in src/DigiByte.Web/wwwroot/css/app.css make sure the celebration's confetti, rings and coin animation are covered by the existing `prefers-reduced-motion` rules (add selectors if they are not).
- [X] T029 [US2] Create src/DigiByte.Web/Components/Shared/FirstReceiveCelebration.razor per the UI contract:
  - parameter `Blocked`; subscribes to the coordinator's `Changed`; appears when `Pending(activeWalletId)` is set, `Blocked` is false and `OtherDialogOpen` is false, and calls `MarkShownAsync` as it appears
  - heading from `firstreceive.title`; amount line from `firstreceive.pending` or `firstreceive.received` with the DGB amount formatted as `0.########`, plus the fiat value when a price is in `MemoryCacheService`; when `pref:hideBalance` is `1`, the `_hidden` variants with no amount
  - `<BakuMessage MessageId="send.done" Compact="true" />`-style mascot line using a new `receive.first` catalogue entry (English only), which the mascot rules hide when it is Off or Guardian only
  - **Done** button; **Install app** button only when `InstallAppState.GetModeAsync()` is not `Hidden`: `Native` calls `pwaInstall.prompt`, the steps modes navigate to `/settings?install=1`; both close the overlay first
  - `role="dialog"`, `aria-modal="true"`, `aria-labelledby` its heading, focus moves to **Done**, Escape closes, focus returns to the page
  - reuse the confetti and ring styles of TxSuccessOverlay.razor; closes when the page is left or the wallet locks
- [X] T030 [US2] In src/DigiByte.Web/Pages/CreateWallet.razor, call `FirstReceiveCoordinator.MarkCreatedEmptyAsync(wallet.Id)` immediately after `WalletService.CreateWalletAsync(...)` returns (FR-011). Do not add it to RecoverWallet.razor or ImportXpub.razor.
- [X] T031 [US2] In src/DigiByte.Web/Pages/Home.razor, after each network history load in `LoadTransactionsAsync` call `ObserveAsync(walletId, transactions, source)` using `WalletService.LoadTransactionHistoryAsync` and mapping its result: `IncompleteLoad` when the call threw or `Complete` is false; `PartialAddressLoad` when `Complete` is true and `AddressesSettled` is false; otherwise `CompleteLoad`; pass the returned payment's `TxIds` to the announcer so no toast shows for them (FR-022); render `<FirstReceiveCelebration Blocked="..." />` with `Blocked` true while any of `showScanner`, `selectedTx != null`, `showWalletInfo`, `showAddressQr`, `showMore` or `showWalletPicker` is true (FR-023).
- [X] T032 [US2] In src/DigiByte.Web/Pages/Transactions.razor, call `ObserveAsync` in `LoadAsync` after the history call (same source rule as T031) and render `<FirstReceiveCelebration Blocked="..." />` with `Blocked` true while `selected != null` (the transaction detail) or `showAddressPicker` is true; those are the page's only dialogs. Activity gets no automatic loop.
- [X] T033 [US2] In src/DigiByte.Web/Pages/Receive.razor, make Receive notice a first payment when it opens (FR-013):
  - add a private `CheckForPaymentsAsync()` that calls `WalletService.LoadTransactionHistoryAsync(0, 50)`, returns false without touching the screen when it throws or `Complete` is false, and otherwise calls `ObserveAsync` (same source rule as T031) and the announcer, applies the history, and returns whether the newest TxId or the count changed
  - in `LoadHistoryAsync`, call `ObserveAsync` and the announcer after the existing network history call, with the same source rule
  - `LoadHistoryAsync` returns early for private-key wallets, so for a private-key wallet call `CheckForPaymentsAsync()` once in `OnInitializedAsync`; every wallet type is then checked when Receive opens
  - render `<FirstReceiveCelebration Blocked="false" />`: Receive has no dialog of its own (its address list is an inline fold, not a dialog), so only the layout's "What's New" window can hold the celebration back
- [X] T034 [US2] In src/DigiByte.Web/Pages/Receive.razor, add the automatic check (FR-024 to FR-026): start an `AutoRefreshLoop` in `OnInitializedAsync` whose check is `CheckForPaymentsAsync` from T033; skip it for a locked wallet; implement `IAsyncDisposable` and stop the loop on leave. A failed check changes nothing on screen and shows no error.
- [ ] T035 [US2] Preview check: run quickstart.md section 2, steps 6 to 15 (payment while Receive is open, second payment toast, payment while closed, hidden balance, reduced motion, mascot Off, Install app in both modes, wallet created offline), and section 3 (Receive open for 5 minutes costs about what Home does). Also time SC-005 once on Home and once on Receive: from the payment row appearing to the celebration being on screen must be under 2 seconds. Fix anything that fails.

**Checkpoint**: A new wallet gets exactly one celebration, on whichever of the three pages sees the payment first.

---

## Phase 5: User Story 3 - No celebration for a wallet that already has history (Priority: P1)

**Goal**: Recovered, imported, watch-only and pre-existing wallets with history never see the celebration.

**Independent Test**: Recover a wallet that has past transactions, wait for history to load, and confirm no celebration appears then or when a new payment arrives later (quickstart.md, section 2, steps 16 to 18 and the celebration half of step 19).

### Implementation for User Story 3

- [X] T036 [US3] In src/DigiByte.Web/Pages/Home.razor `LoadFromCacheAsync`, src/DigiByte.Web/Pages/Transactions.razor `OnInitializedAsync` and src/DigiByte.Web/Pages/Receive.razor `LoadHistoryAsync`, call `ObserveAsync(walletId, savedTransactions, HistorySource.SavedSnapshot)` when a saved snapshot is applied, so an existing wallet with saved history is recorded as `History` before any network load.
- [X] T037 [US3] Review every call site added in T031 to T033 and T036 against FR-015: no path reads the shared `LastHistoryIncomplete` property to decide the source (only the `HistoryLoad` returned by that call), no path passes `CompleteLoad` after an exception, a timeout (`WithTimeout` in Home.razor), `Complete` false or `AddressesSettled` false, and a wallet switch mid-load cannot record the result against the wrong wallet (capture the wallet ID before the await and compare after, as Receive already does).
- [ ] T038 [US3] Preview check: run quickstart.md section 2, steps 16 to 18 (recover with history, recover truly empty then pay, watch-only with history) and the celebration half of step 19 (an existing install with a funded wallet shows no celebration), and step 20 for the per-wallet celebration with several wallets. Fix anything that fails.

**Checkpoint**: Stories 2 and 3 together are safe to ship.

---

## Phase 6: User Story 4 - A quieter wallet until it has something in it (Priority: P2)

**Goal**: Until some wallet on the device has a transaction, the install banner is hidden on pages inside the wallet and the sponsors strip is hidden on Home.

**Independent Test**: With an empty wallet, open Home, Receive and Activity and confirm the install banner is on none of them, that Home shows no sponsors strip, and that the checklist is shown. Receive a payment and confirm both return.

### Implementation for User Story 4

- [X] T039 [US4] Extend `FirstReceiveCoordinator.InitializeAsync` in src/DigiByte.Web/Services/FirstReceiveCoordinator.cs: when `firstSession:ended` is absent, load each wallet's saved snapshot once through `WalletSnapshotCache.LoadAsync`; if any has transactions, call `EndFirstSessionAsync` and raise `Changed` (research R9). `FirstSessionEnded` reads false until this finishes.
- [X] T040 [P] [US4] In src/DigiByte.Web/Components/PwaInstallBanner.razor, inject the coordinator and show the banner only when its existing rules pass and (`IsAlwaysShowPage()` or `FirstSessionEnded`); subscribe to `Changed` and unsubscribe on dispose (FR-029, FR-030).
- [X] T041 [P] [US4] In src/DigiByte.Web/Pages/Home.razor, render `<SponsorsStrip />` only when `FirstSessionEnded` is true, and re-render on the coordinator's `Changed` (FR-029). The getting-started checklist is untouched (FR-031).
- [ ] T042 [US4] Preview check: quickstart.md section 2, steps 1, 3 and 8 (hidden while empty, back after the first payment), the banner half of step 19 (an existing funded install shows both from the first page opened) and step 20 (switching between a funded and an empty wallet changes neither). Also confirm the banner still shows on `/welcome`, `/recover-wallet` and `/import-xpub`, and that Settings' Install app entry is unchanged.

**Checkpoint**: All four stories work.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T043 [P] Spanish check (FR-032, SC-007): run quickstart.md section 4; every key added in T003 is used, none is missing from es.json, and the mascot lines stay in English.
- [ ] T044 [P] Accessibility pass on `ReceiveEmptyState` and `FirstReceiveCelebration` (FR-019): keyboard-only use, focus in and out of the celebration, screen-reader announcement, 44 px touch targets, contrast in light and dark mode.
- [X] T045 [P] Update docs/ARCHITECTURE.md: remove the walkthrough, and describe the first-receive state, the coordinator, the shared refresh loop and the first-session marker.
- [X] T046 [P] Add to the Unreleased section of CHANGELOG.md, in the file's existing style: **Added** (the empty Home's address and QR code; the first-payment celebration; Receive checks for payments while open; Receive button on an empty Activity), **Changed** (install banner and sponsors strip wait for the first payment), **Removed** (the introduction slides). State that no key, seed or password handling changed.
- [X] T047 Run the whole wallet suite (`dotnet test tests/DigiByte.Wallet.Tests`) and a release build of src/DigiByte.Web; fix any failure or warning introduced by this feature.
- [ ] T048 Run all of quickstart.md once more from a clean browser profile and record the results for the pull request. Also verify the two "must not" rules: with the browser's network panel open on Receive for two checks, every request goes to a host Home's refresh already uses and is of a kind Home already makes (FR-033); and the diff shows no change to `CreateWalletAsync`, `CreateWalletFromWifAsync`, `CreateWatchOnlyWalletAsync`, unlock, signing or any storage of secrets (FR-034).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: needs T002. Blocks US2, US3 and US4.
- **US1 (Phase 3)**: needs only T003. Independent of Phase 2.
- **US2 (Phase 4)**: needs Phase 2.
- **US3 (Phase 5)**: needs Phase 2 and the observe calls from T031 to T033.
- **US4 (Phase 6)**: needs Phase 2. Its marker is set by the observe calls from US2 and US3, so check it after them.
- **Polish (Phase 7)**: after the stories being shipped.

### User Story Dependencies

- **US1**: none. T016 edits Home.razor, as do T026, T031, T036 and T041; do them in that order to avoid conflicts.
- **US2**: none on US1. Inside it: T022 and T023 before T024 to T026; T025 before T026; T026 before T031; T025 before T033; T024 before T034; T033 before T034; T027 and T028 before T029; T029 before T031 to T033.
- **US3**: builds on US2's call sites. The rule that protects it (T008) is already in Phase 2, so US2 never ships without the "has history" protection for complete loads; US3 adds the saved-snapshot path and the audit.
- **US4**: independent of US1; best checked after US2.

### Within Each User Story

- Tests first, failing, then the implementation that makes them pass.
- Wallet library before web services, web services before pages.
- The preview check is the last task of each story.

### Parallel Opportunities

- Phase 1: T002 and T003.
- Phase 2: T004, T005, T006 and T007 (four test files); then T008 and T009. T010 and T011 both edit WalletService.cs, so run them one after the other.
- US1: T015, T017 and T018 (three different files); T014 and T016 both edit Home.razor.
- US2: T020 and T021; T022, T023, T027 and T028 (four different sets of files).
- US4: T040 and T041.
- Polish: T043 to T046.

---

## Parallel Example: User Story 2

```text
# Tests first, together:
T020 NewArrivalsTests.cs
T021 RefreshPacerTests.cs

# Then the three moves, together (different files):
T022 NewArrivals.cs
T023 RefreshPacer.cs
T027 InstallAppState.cs + Settings.razor
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1.
2. Phase 3 (US1). It needs nothing from Phase 2.
3. Stop and validate: the empty Home shares an address with one tap. This can ship alone.

### Incremental Delivery

1. US1 → ship.
2. Phase 2, then US2 and US3 together → ship. Do not ship US2 without US3's T036 and T037.
3. US4 → ship.
4. Polish before each release.

### Smaller first release

If the first release should stay small, leave out T034 (Receive's automatic check) and keep T033. The celebration then appears on Home (which already checks by itself) and on Receive or Activity when they are opened or refreshed. T021, T023, T024 and the poller part of T026 can wait with T034; skip section 3 of the quickstart and step 6's "without touching anything" in T035.

---

## Notes

- [P] tasks touch different files and do not depend on an unfinished task.
- Home.razor is edited by six tasks (T014, T016, T026, T031, T036, T041); never run two of them at once.
- Commit after each task or logical group; do not push until asked.
- No task changes `DigiByte.Crypto`, signing, secret storage or any server project.

## Implementation status (2026-11-22)

Code and automated tests are done. The checks below are still open because they could not be completed in the local preview: the testnet explorers refuse requests from `localhost` (CORS), so no balance, history or payment can be loaded there, and the preview pane refuses clipboard writes.

- **T019** (US1 preview): checked at phone width in dark mode with the network unreachable: no introduction slides, the address card (QR code, full address, Copy address, Open Receive) under "Unable to load data", the page scrolls, Activity's empty state. Not checked: light mode, the mascot set to Off, a successful copy (the pane refused the clipboard; the refusal now shows a notice instead of failing the page), and the empty state with the network working.
- **T035** (US2 preview): the celebration was checked through the preview on `/test-animations` (both wordings, the mascot line, focus on Done, Escape, Install app opening the steps in Settings). Not checked: a real first payment on Home, Receive or Activity, the "payment received" notice on Receive, hidden balance, reduced motion on a device (the style rule is present), and the 2-second and request-count measurements.
- **T038** (US3 preview): not run; it needs wallets with real history. The rules are covered by `FirstReceiveRuleTests`, `FirstReceiveCoordinatorTests` and `HistoryLoadTests`.
- **T042** (US4 preview): checked that the install banner and the sponsors strip are hidden for a device with no transactions, on Home, Receive, Activity and Settings, and that the checklist shows. Not checked: both returning after a first payment, an existing funded install, switching wallets.
- **T043** (Spanish), **T044** (accessibility pass) and **T048** (full quickstart from a clean profile, network panel check): not run.

What implementation found and changed is listed in research.md under "Found during implementation".
