# Tasks: Requests That Know When They're Paid

**Input**: Design documents from `specs/036-paid-aware-requests/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/payment-link-fields.md](contracts/payment-link-fields.md), [contracts/request-watcher.md](contracts/request-watcher.md), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution principle III: every change in `DigiByte.Wallet` ships with tests written first. There is no bUnit or Playwright for wallet pages, so web pages are checked with [quickstart.md](quickstart.md) in the browser preview.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with the tasks next to it (a different file, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1 to US5)

## Path Conventions

- Wallet logic: `src/DigiByte.Wallet/Services/`; models: `src/DigiByte.Wallet/Models/`
- Web app: `src/DigiByte.Web/` (Pages, Components/Shared, Services, Layout, wwwroot, Program.cs)
- Tests: `tests/DigiByte.Wallet.Tests/` (harness: `ChangeAddressTests.Env` and `FakeChain`; `HistorySyncEnv` / `HistorySyncTestDoubles` for history-sync tests)
- Run tests without the live benchmarks: `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"`

## Rules that apply to every task

- **A test task is written before the task that makes it pass, and must fail before that task.** Where the member under test does not exist yet, the test project won't compile until the implementing task is done. That is expected, and it is the only time the build may be red.
- **The build and the suite are green after every implementation task**, meaning after each pair of a test task and the task that makes it pass.
- **No task assigns `WalletInfo.NextReceivingIndex`** apart from the two writers spec 027 allows. Request hand-outs touch only `HandedOutTop` and the watched list (data-model "ReceiveScanSet rules").
- **Nothing about a request (label, message, amount, status) goes into a request to a server operated by this project.** Explorer lookups send only the address (FR-028, FR-030).
- **Every new piece of text** gets a key in both `en.json` and `es.json` under `request.*` or `payments.*`, with Spanish translated, not copied.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The branch, a known-good baseline, and the wording later phases use.

- [X] T001 Create branch `feature/paid-aware-requests` from `main` and commit `specs/036-paid-aware-requests/`. Do not commit `.claude/settings.local.json`, `.claude/launch.json`, `.claude/scheduled_tasks.lock`, `digipay-verify.db*` or `docs/media/x-multisig-signing.html`.
- [X] T002 Run `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"` and record the pass count as the baseline. It must be all-pass before any change.
- [X] T003 [P] Add the US1 and US5 translation keys to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`:
  - `request.status.*`: `waiting`, `arriving`, `partly_paid`, `paid`, `overpaid`, `expired`, `partly_paid_expired`, `cancelled`, `not_tracked`.
  - `request.arriving_note` ("+{0} DGB arriving"), `request.received_of` ("{0} of {1} DGB received"), `request.any_amount_received`, `request.stale` ("Not up to date").
  - `request.not_tracked_hint`, `request.single_key_hint`.
  - `request.limit_reached` ("You have {0} unused addresses waiting ({1} of them open requests). Cancelling a request doesn't free its address; new requests are possible once some are paid."), `request.limit_open_list`.
  - `request.late_note`, `request.after_cancel_note`.
  - `request.filter.open`, `request.filter.paid`, `request.filter.expired`, `request.filter.all`.
  - `request.cancel`, `request.cancel_confirm`, `request.remove`, `request.remove_confirm` (says money already received stays in the wallet and Activity).
  - `request.message_label`, `request.payments_title`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The request record and its per-wallet storage, which every story reads and writes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 [P] Write `tests/DigiByte.Wallet.Tests/RequestModelJsonTests.cs`:
  - Legacy JSON (only `Id`, `Address`, `AmountDgb`, `Label`, `Message`, `CreatedAt`, `ExpiresAt`, `Status:"Pending"`) deserializes with `Tracked == false`, empty `Payments` and `OpenedSincePaid == false`.
  - A request with every new field round-trips unchanged.
  - The `PaymentRequestStatus` values `Waiting`, `PaymentArriving`, `PartlyPaid`, `Paid`, `Overpaid`, `Expired`, `PartlyPaidExpired`, `Cancelled`, `NotTracked` serialize by name.
  - The stored value for the old `Pending` status (the number `0`, since the wallet wrote enums as numbers) reads as `PaymentRequestStatus.Waiting`. A legacy record has `Tracked == false`, so `RequestStatusCalculator` (T014) gives it `NotTracked`; this test asserts only the raw value that is read.
- [X] T005 Extend `src/DigiByte.Wallet/Models/PaymentRequest.cs` and add `src/DigiByte.Wallet/Models/RequestPayment.cs`, exactly as data-model.md "PaymentRequest (changed)" and "RequestPayment (new)" describe:
  - `AddressIndex int?`; `Tracked bool` ("Defaults to false when the field is missing in stored JSON").
  - `FiatAmount decimal?`, `FiatCurrency string?` ("ISO code from FiatCurrencies"), `FiatPrice decimal?`.
  - `CancelledAt DateTime?`, `RenewedFromId string?`.
  - `Payments List<RequestPayment>`, `PaidAnnouncedAt DateTime?`, `LockNotifiedAt DateTime?`, `OpenedSincePaid bool`, `LastCheckedAt DateTime?`.
  - The enum values listed in T004, keeping `Pending` for JSON reading only.
  - `RequestPayment(TxId, AmountSatoshis long, Confirmed bool, FirstSeenAt DateTime UTC, Late bool)`.
  - Makes T004 pass.
- [X] T006 [P] Write `tests/DigiByte.Wallet.Tests/RequestMigrationTests.cs` for a pure `RequestMigration.Split(legacy, walletAddresses)`:
  - Legacy requests whose `Address` is in the wallet's known receive addresses move out with `Tracked = false` and `Status = NotTracked`. The rest stay.
  - An empty legacy list returns nothing to move.
  - Running it twice moves nothing the second time.
  - Unreadable legacy JSON is left untouched and moves nothing (negative path).
- [X] T007 Implement `src/DigiByte.Wallet/Services/RequestMigration.cs` (data-model "Migration", steps 1 to 4). Makes T006 pass.
- [X] T008 Write `tests/DigiByte.Wallet.Tests/PaymentRequestServiceTests.cs` (storage only, using an in-memory `ISecureStorage`):
  - `GetAllAsync(walletId)` reads `payment_requests:{walletId}`, and runs the migration once per wallet, removing the legacy key `payment_requests` when nothing is left in it.
  - Two wallets' lists are kept apart.
  - `ApplyAsync` stores the computed status, payments, `LastCheckedAt`, `PaidAnnouncedAt` and `LockNotifiedAt`, and never clears a `PaidAnnouncedAt` or `LockNotifiedAt` already stored.
  - `MarkOpenedAsync` sets `OpenedSincePaid = true`.
- [X] T009 Rework `src/DigiByte.Wallet/Services/PaymentRequestService.cs` to per-wallet storage, with `GetAllAsync(walletId)`, `ApplyAsync`, `MarkOpenedAsync` and a per-wallet cache, as in contracts/request-watcher.md "PaymentRequestService". Keep a temporary `CreateAsync(walletId, address, amountDgb, label, message)` that creates `Tracked = false` requests so `Payments.razor` keeps working until T018. Update `src/DigiByte.Web/Pages/Payments.razor` to pass the active wallet id. Makes T008 pass.

**Checkpoint**: Requests are stored per wallet, and old requests read as "Not tracked". The page behaves as today.

---

## Phase 3: User Story 1 – See whether a request was paid (Priority: P1) 🎯 MVP

**Goal**: Each new request has its own address, and shows Waiting, Payment arriving, Partly paid (with a "+… arriving" note), Paid or Overpaid from the payments to that address. Receive's address never moves.

**Independent Test**: quickstart scenarios 1, 3 (status only), 4 and 14. Two requests have different addresses, Receive is unchanged, and partial, full and over payments each show the right status.

### The address hand-out (tests first)

- [X] T010 [P] [US1] Extend `tests/DigiByte.Wallet.Tests/ReceiveScanSetTests.cs` with `HandedOutTop` cases from data-model "ReceiveScanSet rules":
  - `HandedOutTop = null` gives exactly today's `NextNewIndex`, `BaseCount` and `Indexes`.
  - `NextNewIndex = max(shown + 1, Anchor, top + 1)`.
  - `BaseCount` counts `top + 1` only when `top` is not a watched entry.
  - `CanHandOutNew` turns false when `NextNewIndex == Anchor + 20`.
- [X] T011 [US1] Add `HandedOutTop int?` to `src/DigiByte.Wallet/Models/WalletInfo.cs` ("Null means it equals NextReceivingIndex… never lowered"). Pass it into `src/DigiByte.Wallet/Services/ReceiveScanSet.cs` (`From(...)` and its call sites in WalletService), apply the three formulas, and copy it in `PersistAddressIndexesAsync`. Makes T010 pass.
- [X] T012 [P] [US1] Write `tests/DigiByte.Wallet.Tests/RequestAddressHandOutTests.cs` using `ChangeAddressTests.Env` and `FakeChain`:
  - `HandOutRequestAddressAsync` returns a new SegWit address and index each call, and never changes `GetReceivingAddressAsync()`.
  - A later `GenerateNewReceivingAddressAsync` (Receive) skips the indexes given to requests, and raises `HandedOutTop`.
  - A non-contiguous index becomes an unpaid watched entry, and a contiguous one extends the base run.
  - Refusal paths:
    - After 20 unpaid hand-outs (requests and Receive together) it throws `ReceiveAddressException(TooManyUnusedPastLastUsed or TooManyUnusedPastLastPaid)`.
    - A failed save throws `SaveFailed` and restores `HandedOutTop` and the watched list exactly.
    - A single-key wallet throws `SingleAddress`.
  - After a reload from storage, the handed-out request addresses are in `ReceiveScanSet.Indexes`, which covers SC-005.
  - `NextReceivingIndex` is unchanged by request hand-outs.
- [X] T013 [US1] In `src/DigiByte.Wallet/Services/WalletService.cs`, implement:
  - `HandOutRequestAddressAsync()`: same gate (`AddressOnShowGate`), discovery retry, failures and persist-or-undo as `GenerateNewReceivingAddressAsync`.
  - `RecordHandOut(info, index)`.
  - Raising `HandedOutTop` in `GenerateNewReceivingAddressAsync`.
  - These follow contracts/request-watcher.md "WalletService". Makes T012 pass.

### The status (tests first)

- [X] T014 [P] [US1] Write `tests/DigiByte.Wallet.Tests/RequestStatusCalculatorTests.cs` covering every branch of data-model "Status rules":
  - Not tracked.
  - Any-amount: nothing received, pending only, confirmed.
  - Waiting; PaymentArriving when nothing is confirmed and something is pending.
  - PartlyPaid with an `ArrivingSatoshis` note (60 confirmed + 40 pending of 100 → PartlyPaid, arriving 40).
  - Paid at exactly the amount asked; one satoshi short is PartlyPaid; Overpaid.
  - Two payments adding up.
  - A transaction dropped from the list stops counting, and the status goes back (FR-009).
  - `FirstSeenAt` is kept by transaction id across recomputes.
  - `BecamePaid` is true only on the first transition while `PaidAnnouncedAt` is null.
  - `BecameArriving` is true when confirmed + pending first reaches the amount asked.
  - Only outputs to the request's own address count; other outputs in the same transaction are ignored.
- [X] T015 [US1] Implement `src/DigiByte.Wallet/Services/RequestStatusCalculator.cs` (pure, `Compute(request, txsToAddress, nowUtc) → RequestCheck`), including the expiry and cancel branches so later stories need no change here. Makes T014 pass.
- [X] T016 [US1] Test first, in `tests/DigiByte.Wallet.Tests/TransactionsChangedTests.cs` (uses `HistorySyncEnv`): after a refresh that finds new or changed transactions, `WalletService.TransactionsChanged` fires once with the wallet addresses those transactions touch, and a refresh with no changes does not fire it. Then add the event to `src/DigiByte.Wallet/Services/WalletService.cs`, raised where the history sync's results are applied.

### Watching and showing

- [X] T017 [US1] Test first: in `tests/DigiByte.Wallet.Tests/TieredRefreshTests.cs`, an address passed to `WalletService.SetExtraHotAddresses` is hot, and clearing the set makes it cold again. Then add `SetExtraHotAddresses(IReadOnlyCollection<string>)` to `src/DigiByte.Wallet/Services/WalletService.cs` and read it in `BuildHotHints`. `WalletService` must not reference `PaymentRequestService` (that would make the two depend on each other, since T018 calls `WalletService`).
- [X] T018 [US1] Replace the temporary create path in `src/DigiByte.Wallet/Services/PaymentRequestService.cs` with `CreateAsync(walletId, NewRequest)`. It:
  - Calls `HandOutRequestAddressAsync`, and on `SingleAddress` makes a `Tracked = false` request on the shared address.
  - Sets `Tracked`, `AddressIndex`, `Status = Waiting`.
  - Writes the label through `AddressLabelService.SetLabelAsync(walletId, address, label)` (research R9, FR-008).
  - Add the tests for this method to `PaymentRequestServiceTests.cs` first: tracked create, single-key create, label written, and the limit refusal passed through unchanged.
- [X] T019 [US1] Create `src/DigiByte.Web/Services/RequestWatcher.cs` (app-wide; register in `src/DigiByte.Web/Program.cs` next to the other app-wide services). Start it at app load for the last active wallet, since requests and their addresses are readable while locked (research R3), and switch it on unlock or a wallet switch.
  - It checks live requests of the active wallet by stored address through `IBlockchainService.GetAddressTransactionsAsync`, runs `RequestStatusCalculator`, saves through `ApplyAsync`, and raises `Changed`.
  - Pace: 3 s for the `Focus(requestId)` request, 30 s visible, 60 s hidden, with `RefreshPacer` backoff on errors. It keeps running while the tab is hidden.
  - It never calls a method that needs the wallet unlocked.
  - A failed check keeps the stored status and leaves `LastCheckedAt` alone.
  - It subscribes to `WalletService.TransactionsChanged` (T016) and re-checks any request, whatever its state, whose address is in the set. It also checks a request once whenever `Focus(id)` is called for it.
  - It calls `WalletService.SetExtraHotAddresses` with the addresses of open requests whenever its live set changes (T017).
  - It switches its set on a wallet switch.
- [X] T020 [US1] Create `src/DigiByte.Web/Components/Shared/RequestStatusPill.razor`: the status label, the "+{0} DGB arriving" note, and "Not up to date" when `LastCheckedAt` is more than 2 minutes old while online.
- [X] T021 [US1] Rework the list and detail parts of `src/DigiByte.Web/Pages/Payments.razor`:
  - **Create**: use `CreateAsync`. On the limit refusal, show `request.limit_reached` with the wallet's unused-address count from the refusal and the number of open requests from `PaymentRequestService`, and a link to the Open list (FR-005).
  - **List rows**: label, amount, date and the pill.
  - **Detail**: status, "{received} of {asked}", each payment with its time and late note, and the message (FR-007).
  - Call `RequestWatcher.Focus` while a request is open, and `MarkOpenedAsync` when a paid request is opened.
  - Not-tracked requests keep their QR code and links and show `request.not_tracked_hint` or `request.single_key_hint`.
- [ ] T022 [US1] Run quickstart scenarios 1, 3 (statuses only), 4 and 14 on testnet in the browser preview, and record the results in this file under "Implementation record".

**Checkpoint**: US1 works on its own. Requests know when they're paid; you see it by opening the list.

---

## Phase 4: User Story 2 – Know the moment it's paid (Priority: P2)

**Goal**: "Payment arriving" on screen, then a full-screen Paid card with vibration. A tappable toast elsewhere in the wallet; a system notification when hidden; a notice on the lock screen. A "paid since you were last here" summary and a Request tab count.

**Independent Test**: quickstart scenarios 2, 8, 9 and 10.

- [X] T023 [P] [US2] Add the translation keys to `en.json` and `es.json`:
  - `request.arriving_title`.
  - `request.paid_title`, `request.paid_done`, `request.paid_toast` ("{0} paid · {1} DGB").
  - `request.paid_away.one`, `request.paid_away.other` ("{0} requests paid since you were last here").
  - `request.notify.title`, `request.notify.body_plain` ("A request was paid"), `request.notify.body_details`.
  - `request.lock_notice`.
  - `settings.request_notify_details`, `settings.request_notify_details_hint`.
  - `nav.request.count` (aria).
- [X] T024 [P] [US2] Write `tests/DigiByte.Wallet.Tests/RequestAnnouncementRuleTests.cs` for a pure `RequestAnnouncementRule.Decide(request, ViewState { RequestOnScreen, Visible, Locked }, firstCheckSinceStart)` returning `Overlay`, `Toast`, `SystemNotification`, `LockedNotification`, `LockNotice`, `AwaySummary` or `None`. Cases:
  - `PaidAnnouncedAt` already set → None; not Paid or Overpaid → None.
  - Request on screen and unlocked → Overlay.
  - Visible on another screen and unlocked → Toast.
  - Hidden and unlocked → SystemNotification.
  - Locked and visible → LockNotice.
  - Locked and hidden, `LockNotifiedAt` null → LockedNotification (send the FR-013 system notification and set `LockNotifiedAt`; FR-014b). Locked and hidden, `LockNotifiedAt` already set → LockNotice, so the notification is sent at most once while locked.
  - LockNotice and LockedNotification never mark the request announced: `PaidAnnouncedAt` stays null, so the summary still follows an unlock.
  - First check since start, unlock or wallet switch, while unlocked → AwaySummary.
  - Precedence: locked beats everything (LockedNotification or LockNotice); then AwaySummary; then Overlay, Toast, SystemNotification.
  - Sequence cases: first check while locked and visible → LockNotice, not announced; then unlock → AwaySummary, which sets `PaidAnnouncedAt`. Locked and hidden → LockedNotification once; the next checks while still locked → LockNotice; then unlock → AwaySummary.
  - Every combination returns exactly one value.
- [X] T025 [US2] Implement `src/DigiByte.Wallet/Services/RequestAnnouncementRule.cs`. Makes T024 pass.
- [X] T026 [US2] Extend `src/DigiByte.Web/Services/RequestWatcher.cs` with the events `PaymentArriving`, `RequestPaid` and `PaidWhileAway`, and `PaidUnopened` (contracts/request-watcher.md).
  - `RequestPaid` fires once per request: set `PaidAnnouncedAt` in the same save as the status.
  - The first completed check after an app start, an unlock or a wallet switch collects requests that became Paid or Overpaid without an announcement into one `PaidWhileAway(count)`, and does not raise `RequestPaid` for them (FR-014a).
  - Each paid request is announced in exactly one of the ways in contracts/request-watcher.md, chosen by `RequestAnnouncementRule.Decide` (T025); the watcher has no routing logic of its own.
  - Just before announcing, re-read the request from storage and do nothing if `PaidAnnouncedAt` is already set, or, for a `LockedNotification`, if `LockNotifiedAt` is already set (another tab got there first). Set the field in the same save as the status, before raising the event.
- [X] T027 [P] [US2] Create `src/DigiByte.Web/Components/Shared/RequestPaidOverlay.razor` on the `modal-overlay` + `celebrate-card` pattern (see `FirstReceiveCelebration.razor`).
  - Shows the amount received and the label, and calls `domHelpers.haptic` (pattern `[15,40,25]`).
  - Closes only when the person taps Done, never on a timer (FR-011).
  - Respects hide-amounts mode like `FirstReceiveCelebration`.
- [X] T028 [US2] In `src/DigiByte.Web/Pages/Payments.razor`:
  - Show "Payment arriving" on `PaymentArriving` for the request on screen, with no vibration.
  - Show `RequestPaidOverlay` on `RequestPaid` for the request on screen.
  - Partial payments only update the pill (FR-015).
- [X] T029 [P] [US2] Add an optional tap action to `src/DigiByte.Web/Services/NotificationService.cs` (`Show(..., Action? onTap = null)`) and handle it in `src/DigiByte.Web/Components/Shared/ToastContainer.razor`. Existing callers are unchanged.
- [X] T030 [P] [US2] Create `src/DigiByte.Web/wwwroot/js/request-notify.js` with `permission()`, `ask()` and `show(title, body)`. It uses `navigator.serviceWorker.ready` → `registration.showNotification` when available, else `new Notification`, and does nothing where unsupported. No push subscription. Reference it in `src/DigiByte.Web/wwwroot/index.html` the way `dom-helpers.js` is.
- [X] T031 [US2] Create `src/DigiByte.Web/Services/RequestAnnouncer.cs` (app-wide). It subscribes to `RequestWatcher` and acts on the way `RequestAnnouncementRule.Decide` chose, with no routing logic of its own. The toast and the system notification follow hide-amounts mode, as the overlay does.
  - The request is on screen and unlocked → leave it to the page.
  - Another wallet screen is visible and unlocked → a toast with a tap action that opens the request (FR-012).
  - The wallet is hidden (unlocked → SystemNotification; locked → LockedNotification, which also sets `LockNotifiedAt` and leaves `PaidAnnouncedAt` null) → `request-notify.show` with `body_plain`, or `body_details` (label and amount, never the address) when localStorage `dgb-request-notify-details` is `"true"` (FR-013).
  - The lock screen is showing → leave it to Unlock; do not set `PaidAnnouncedAt`, so the summary still appears after unlocking (FR-014a).
  - `PaidWhileAway` → one sticky toast that opens the list.
  - Ask for notification permission once, after the first request is created, and set `dgb-request-notify-asked`.
- [X] T032 [P] [US2] Add the "Show amount and label in notifications" switch to `src/DigiByte.Web/Pages/Settings.razor`, stored in localStorage as `dgb-request-notify-details` (default off).
- [X] T033 [P] [US2] In `src/DigiByte.Web/Pages/Unlock.razor`, add a "Request paid" notice card using the existing notice-card pattern (lines 56-73), fed by `RequestWatcher.PaidUnopened` (requests that are Paid or Overpaid and not yet opened since, whether announced or not). Show details only when the setting is on. Never show the full-screen card here (FR-014b).
- [X] T034 [P] [US2] Create `src/DigiByte.Web/Services/RequestAttentionState.cs` (a count with a `Changed` event, like `MultisigAttentionState`). Show its count on the personal Request tab in `src/DigiByte.Web/Layout/NavMenu.razor` with `nav-badge-count` and `CountBadge.Text`, and include the count in the tab's aria label.
- [X] T035 [US2] Add `Payments.razor`, `Unlock.razor`, `RequestPaidOverlay.razor` and `Settings.razor` to `LocaleFilesTests.InScopeFiles` in `tests/DigiByte.Wallet.Tests/LocaleFilesTests.cs`, if they aren't there, and make the locale tests pass.
- [ ] T036 [US2] Run quickstart scenarios 2, 8, 9 and 10, and record the results.

**Checkpoint**: US1 and US2 work. Paying a request shows up right away, wherever the person is.

---

## Phase 5: User Story 3 – Ask in my own currency (Priority: P3)

**Goal**: Request an amount in the person's currency. The DGB amount is fixed for 15 minutes, with a countdown, expiry and renew. The link carries the price lock, and Send and Pay show it.

**Independent Test**: quickstart scenarios 5, 6, 7 and 13 (the price part).

- [X] T037 [P] [US3] Add the translation keys to `en.json` and `es.json`:
  - `request.fiat_amount`, `request.price_used` ("1 DGB = {0}"), `request.time_left`.
  - `request.renew` ("Renew at today's price"), `request.renew_rest`.
  - `request.price_unavailable`.
  - `send.price_held` ("≈ {0} · Price held until {1}"), `send.price_expired` ("The price for this request expired at {0}. The amount may be out of date.").
  - `pay.price_held`, `pay.price_expired`.
- [X] T038 [P] [US3] Write `tests/DigiByte.Wallet.Tests/RequestAmountsTests.cs`:
  - Currency amount ÷ price, rounded half-up to 8 decimals.
  - Decimals for the currency come from `FiatCurrencies.DecimalsFor`.
  - Zero or negative price, or an unknown currency, is rejected.
  - For a partly paid, expired request, "the rest" = the missing DGB × the current price.
  - Expiry = `CreatedAt + 15 min`.
- [X] T039 [US3] Implement `src/DigiByte.Wallet/Services/RequestAmounts.cs`. Makes T038 pass.
- [X] T040 [P] [US3] Extend `tests/DigiByte.Wallet.Tests/RequestStatusCalculatorTests.cs` with expiry cases:
  - Expired with nothing received.
  - PartlyPaidExpired.
  - A payment after expiry is flagged `Late` and can still reach Paid.
  - Paid before expiry stays Paid after the clock passes `ExpiresAt`.
  - Moving the clock backward or forward never turns Paid into Expired.
  - Requests in DGB never expire (FR-019).
  - Fix `RequestStatusCalculator.cs` if any case fails.
- [X] T041 [P] [US3] Write the link tests in `tests/DigiByte.Wallet.Tests/PaymentLinkTests.cs` and `PaymentRequestTests.cs` from contracts/payment-link-fields.md "Tests":
  - Round trip of `fiat`, `currency`, `expires`.
  - Each key missing, malformed, with an unknown currency, or `expires ≤ 0` → the link stays valid and all three are dropped.
  - A comma decimal is rejected.
  - A DGB request writes none of them.
  - Add `ScanRouterTests.cs` and `ScanHandOffTests.cs` cases showing the three values are carried through.
- [X] T042 [US3] Implement the price-lock keys in:
  - `src/DigiByte.Wallet/Models/PaymentRequest.cs` (`ToUri`/`FromUri`).
  - `src/DigiByte.Wallet/Services/PaymentLink.cs` (`PaymentLinkData` gains `FiatAmount`, `FiatCurrency`, `PriceExpiresAt`; `TryRead` reads the group and drops it on any fault without invalidating the link).
  - `src/DigiByte.Web/Services/ScanRouter.cs`, `src/DigiByte.Web/Services/ScanHandOff.cs` and `src/DigiByte.Web/Services/PaymentHandoff.cs`, so the values reach Send.
  - Makes T041 pass.
- [X] T043 [US3] Create `src/DigiByte.Web/Services/PriceQuoteService.cs`. It returns `PriceQuote(Price, Currency, FetchedAt)` or null, never throws, wraps `IBlockchainService.GetDgbPriceAsync(AppState.FiatCurrency)` through `MemoryCacheService` with the pages' key `price:{currency}`, and refetches a quote older than 5 minutes before a request is made. Register it in `Program.cs`.
- [X] T044 [US3] Add `NewRequest` with a currency amount and a `PriceQuote` to `PaymentRequestService.CreateAsync`. It sets `AmountDgb` from `RequestAmounts`, plus `FiatAmount`, `FiatCurrency`, `FiatPrice` and `ExpiresAt`. Add `RenewAsync(walletId, requestId, quote)`: a new request for the same currency amount, or for the rest when the old one is PartlyPaidExpired, with `RenewedFromId` set; the old request stays in the list. Write the tests in `PaymentRequestServiceTests.cs` first.
- [X] T045 [US3] In `src/DigiByte.Web/Pages/Payments.razor`:
  - A currency request shows the currency amount, DGB amount, price and a live countdown.
  - Expired and PartlyPaidExpired requests hide the QR code and links and show the renew button (FR-018).
  - With no price, the currency option is disabled with `request.price_unavailable` (FR-021).
  - The watcher's live set includes requests that expired or were cancelled in the last 24 hours (research R3).
- [X] T046 [P] [US3] In `src/DigiByte.Web/Pages/Send.razor`, show `send.price_held`, or `send.price_expired` once `now ≥ PriceExpiresAt`, when a handed-off or scanned link carries the price-lock fields (`TryTakeHandoff`, `OnQrScanned`). Never block sending.
- [X] T047 [P] [US3] In `src/DigiByte.Web/Pages/Pay.razor`, on the no-wallet card, show `pay.price_held` or `pay.price_expired` from the read link.
- [ ] T048 [US3] Run quickstart scenarios 5, 6, 7 and 13, and record the results.

**Checkpoint**: Currency requests work end to end, including on the payer's side.

---

## Phase 6: User Story 4 – Ask for money in a few taps (Priority: P4)

**Goal**: The Request tab opens on a large keypad, remembers DGB or the person's currency, has "Add a note", and reaches the list in one tap.

**Independent Test**: quickstart scenario 17, plus US4 acceptance scenarios 1 to 5.

- [ ] T049 [P] [US4] Add the translation keys to `en.json` and `es.json`: `request.keypad.request`, `request.keypad.any_amount`, `request.keypad.add_note`, `request.keypad.list`, `request.keypad.currency_switch` (aria), `request.keypad.clear`, `request.keypad.backspace`.
- [ ] T050 [P] [US4] Write `tests/DigiByte.Wallet.Tests/RequestKeypadEntryTests.cs`, modelled on the rules of DigiPay's `PosEntry` (`src/DigiByte.Pay.Web/Components/Pages/Pos/PosEntry.cs`):
  - Press digits and the decimal point; no second point.
  - No more decimals than the currency allows (8 for DGB, `FiatCurrencies.DecimalsFor` for the person's currency).
  - Leading zero rules; backspace; clear.
  - An amount that doesn't fit is rejected.
  - An empty value means "any amount".
  - Parsing always uses the invariant culture.
- [ ] T051 [US4] Implement `src/DigiByte.Wallet/Services/RequestKeypadEntry.cs`. Makes T050 pass. Do not change DigiPay.
- [ ] T052 [US4] Create `src/DigiByte.Web/Components/Shared/RequestKeypad.razor`:
  - Large amount display with the DGB equivalent while in the person's currency.
  - DGB / person's-currency switch, starting from localStorage `dgb-request-currency` ("DGB" or "FIAT"). It starts in DGB the first time and whenever no price is available, without overwriting the remembered choice (FR-016a).
  - The digit grid, a "Request" button (an empty amount means any amount), an "Add a note" section for label and message (FR-023), and a list button.
- [ ] T053 [US4] Make `src/DigiByte.Web/Pages/Payments.razor` open on `RequestKeypad` (FR-022). Request → create → show the QR code in one step. Remove the old form and the "+" button; keep the DigiPay merchant card below the list.
- [ ] T054 [US4] Run quickstart scenario 17 and US4's acceptance scenarios, and record the results.

---

## Phase 7: User Story 5 – Keep the list tidy (Priority: P5)

**Goal**: Cancel, remove, and the Open / Paid / Expired filters.

**Independent Test**: quickstart scenario 12, and US5 acceptance scenarios 1 to 4.

- [ ] T055 [P] [US5] Add tests to `PaymentRequestServiceTests.cs`:
  - `CancelAsync` sets `CancelledAt`.
  - `RemoveAsync` removes the request from the list but leaves its address label, and the address stays in `ReceiveScanSet.Indexes` after a reload (FR-002, FR-026, SC-005).
  - Cancelling a request does not change `CanHandOutNew` (FR-005).
  - Add a test to `RequestStatusCalculatorTests.cs`: a cancelled request that later receives money shows the late payment and reaches Paid when the total is met (FR-020).
- [ ] T056 [US5] Implement `CancelAsync` and `RemoveAsync` in `src/DigiByte.Wallet/Services/PaymentRequestService.cs`. Makes T055 pass.
- [ ] T057 [US5] In `src/DigiByte.Web/Pages/Payments.razor`:
  - Filter chips (data-model "Filters"): Open = Waiting, PaymentArriving, PartlyPaid; Paid = Paid, Overpaid; Expired = Expired, PartlyPaidExpired, Cancelled; All includes NotTracked.
  - Cancel with confirmation; a cancelled request hides its QR code and links (FR-025).
  - Remove with confirmation, using `request.remove_confirm`.
  - The limit message's link opens the Open filter.
- [ ] T058 [US5] Run quickstart scenarios 11 and 12, and record the results.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T059 [P] Add a "Request a payment" flow to `docs/PROCESS_FLOWS.md` covering: own address per request, status, the paid moment, the price lock, and watching while locked.
- [ ] T060 [P] Add a note to `specs/027-deep-receive-search/data-model.md` under "Moving the address on show": `HandedOutTop` is a third field, raised by Receive and request hand-outs, and `NextReceivingIndex` still has only its two writers. Link to `specs/036-paid-aware-requests/data-model.md`.
- [ ] T061 Run the full suite (`dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"`) and `dotnet build DigiByte.slnx`. It must be all-pass, at or above the T002 baseline.
- [ ] T062 Run quickstart scenarios 15 (Spanish), 16 (network traffic: no request detail sent to a project server, SC-006) and 18 (multisig Pending tab unchanged), and record the results.
- [ ] T063 Update `CHANGELOG.md` under the next version with the Request tab changes.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → user stories.
- **US1 (Phase 3)** depends on Foundational. Every other story needs US1's hand-out, calculator and watcher.
- **US2 (Phase 4)** depends on US1 (it needs the watcher's checks).
- **US3 (Phase 5)** depends on US1. It can run alongside US2: it touches different files, except `Payments.razor` and `RequestWatcher.cs`, where you have to merge carefully.
- **US4 (Phase 6)** depends on US1. Its currency switch is only enabled when US3's `PriceQuoteService` exists; without US3 the keypad is DGB-only.
- **US5 (Phase 7)** depends on US1.
- **Polish** comes last.

### Within each story

- Test tasks come before the task that makes them pass: T004→T005, T006→T007, T008→T009, T010→T011, T012→T013, T014→T015, T016 (test and code in one task), T024→T025, T038→T039, T041→T042, T050→T051, T055→T056.
- Wallet library first, then web services, then pages, then the quickstart run.

### Parallel opportunities

- T003 alongside T004 and T006.
- T010, T012 and T014: different test files, so they can be written together.
- In US2: T024 alongside T027, T029, T030, T032, T033, T034.
- In US3: T038, T040, T041, then T046 and T047.
- In US4: T049, T050.

## Parallel Example: User Story 1

```text
Write together: T010 (ReceiveScanSetTests), T012 (RequestAddressHandOutTests), T014 (RequestStatusCalculatorTests)
Then:           T011 → T013 (same file area in WalletService), T015 alongside them
```

## Implementation Strategy

### MVP first (US1 only)

Phases 1 to 3. Requests get their own address and an accurate status in the list. Ship it: it is already more useful than today, and it is the base for everything else.

### Then, in order

US2 (the paid moment) → US3 (own currency) → US4 (keypad) → US5 (tidy list). Each phase ends with a quickstart run and leaves the tab working.

## Requirement Coverage

| Requirement | Tasks |
|---|---|
| FR-001, FR-003 | T010–T013, T018 |
| FR-002, SC-005 | T012, T016, T017, T055 |
| FR-004, FR-009 | T014, T015, T020 |
| FR-005 | T012, T013, T021, T055, T057 |
| FR-006 | T004–T009, T018, T021 |
| FR-007 | T021 |
| FR-008 | T018 |
| FR-010, FR-011, FR-015 | T024, T025, T026–T028 |
| FR-012 | T024, T029, T031 |
| FR-013, FR-014 | T030–T032 |
| FR-014a | T024, T026, T031, T034 |
| FR-014b | T019, T024, T025, T031, T033 |
| FR-016, FR-016a | T052 |
| FR-017–FR-019, FR-021 | T038–T040, T043–T045 |
| FR-020 | T016, T019, T040, T055 |
| FR-022–FR-024 | T052, T053, T057 |
| FR-025, FR-026 | T055–T057 |
| FR-027 | T021 (actions kept) |
| FR-027a | T041, T042, T046, T047 |
| FR-028–FR-031 | Rules above, T019, T043, T062 |
| FR-032 | T062 |
| SC-002 | T019, T036 |
| SC-003 | T054 |
| SC-006 | T062 |
| SC-007 | T019, T020, T048 |

## Implementation record

### 2026-11-28: MVP (Phases 1 to 3, T001 to T021)

- Baseline before any change: 3,662 tests passing. After T021: 3,738 passing, none failing.
- Commits on `feature/paid-aware-requests`: spec docs, then T001–T009, T010–T013, T014–T017, T018–T021.
- Small departures from the task text:
  - `RequestPayment` lives in `Models/PaymentRequest.cs`, not its own file.
  - The old status was stored as the number `0`, not the string `"Pending"` (the wallet wrote enums as numbers); `Waiting` is `0` so old requests read as `Waiting` and show as Not tracked.
  - `ApplyAsync` takes a `RequestUpdate` record rather than the calculator's `RequestCheck`, so storage does not depend on the calculator.
  - The temporary create path from T009 stays as `CreateUntrackedAsync`; single-key wallets use it (FR-006).
  - The T016 tests are in `TransactionsChangedTests.cs` (they use `HistorySyncEnv`).
  - `IRequestAddressSource` (implemented by `WalletService`) is what `PaymentRequestService` depends on, so the two services never reference each other.
- Found while checking in the browser: `/payments` opened while locked (a reload) showed its form and crashed on Create. It now returns early like Receive does, and `AuthGuard` sends the person to unlock.

### T022 (quickstart scenarios 1, 3, 4, 14): partly run, not ticked

Run on 2026-11-28 in the browser preview (`digibyte-web-clean`, Development, testnet), with a new test wallet:

- Scenario 1 (own address, Receive unchanged): **passed**. Receive kept `dgbt1qtvru…mu0yl3`; the request got `dgbt1qgzuz…`. The detail view showed the status pill, the message and the payments list; the list row showed the pill.
- Scenarios 3 and 4 (partial, full and over payment): **not run with real payments**. Locally the testnet explorer is blocked by CORS, so the Development build falls back to `MockBlockchainService`, which returns the same two demo payments (500 and 750.5 DGB) for every address. The watcher picked those up within seconds and showed "Overpaid" with "1,250.5 of 2.5 DGB received", which is correct for that data, but it proves only the plumbing. The status rules themselves are covered by `RequestStatusCalculatorTests` (19 cases).
- Scenario 14 (old requests show Not tracked): covered by `RequestMigrationTests` and `PaymentRequestServiceTests`; not run in the browser (needs a device with pre-update requests).
- To finish T022: run scenarios 1, 3 and 4 against a reachable testnet explorer (a deployed build, or a local one that isn't blocked by CORS) with a funded second wallet.

### 2026-11-28: US2, the paid moment (T023 to T035)

- After T035: 3,760 tests passing, none failing.
- Departures from the task text:
  - Extra translation keys: `request.arriving_hint`, `request.paid_toast_plain`, `request.lock_notice_details`; the tab count is `nav.request.count.one` / `.other` (singular and plural) rather than one `nav.request.count`.
  - `request.notify.title` is the product name in both languages, so it is on `LocaleParityTests.SameInBoth`.
  - Both service workers gained a `notificationclick` handler that brings the wallet to the front (or opens `/payments`).
  - A toast's tap action opens `payments?request={id}`; the Request page opens that request.
- Found in the browser: on app load the lock screen did not show the notice for a request already saved as paid and unopened, because the watcher only signalled on a status change. It now also signals when the paid-unopened list changes.

### T036 (quickstart scenarios 2, 8, 9, 10): partly run, not ticked

Run on 2026-11-28 in the browser preview with the same test wallet. Payments came from the Development build's demo data source (see T022), which "pays" every request at once:

- Scenario 2 (Paid card on the request's own screen): **passed** with demo data. A new request ("Coffee") showed the full-screen card with "Paid!", the amount and the label; Done closed it and the request showed its status. Vibration not checked (desktop). "Payment arriving" not seen, because the demo payments are already confirmed.
- Scenario 8 (paid while locked): **passed** with a request reset in storage to paid, unopened and unannounced: the lock screen showed "Request paid" with no amount.
- Scenario 10 (summary and tab count): **passed**: after unlocking, one toast "1 request paid since you were last here", the Request tab badge "1" and its label "Request a payment. 1 paid request to look at". Tapping the toast opened the list; opening the request cleared the count and did not show the card again.
- Scenario 9 (system notifications, hidden tab, details setting): **not run**. It needs notification permission granted in a real browser and the tab in the background.
- Still to do for T036: scenario 9, and scenarios 2 and 8 with real testnet payments (including "Payment arriving" before the first confirmation and vibration on a phone).

