---

description: "Task list for 033 DigiDollar on every DigiPay payment surface"
---

# Tasks: DigiDollar on every DigiPay payment surface

**Input**: Design documents from `specs/033-digipay-dd-everywhere/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution's merge gate requires `dotnet test` on the affected projects, and plan research R13 names the suites. Within each story, write the tests first and watch them fail.

**Organization**: Tasks are grouped by user story. The spec numbers its stories as follows:

| Story | Title | Priority |
|---|---|---|
| US1 | POS DD till | P1 |
| US2 | DD payment link and one-off payment | P2 |
| US3 | Buyer picks DGB or DD at checkout | P2 |
| US4 | DD donations | P3 |
| US5 | The setting says what it does | P2 |

**Hard dependency**: spec 034 (`feature/digipay-dd-payouts`) must be merged before US3 and US4, which write its `DdAddress` rows. Phase 2, US5, US1 and US2 don't touch those tables and can be built first; US1 and US2 stay hidden behind `DIGIPAY_DD_SURFACES`, which isn't turned on until 034's payouts are live (plan, Delivery order; changed 2026-11-27 at the user's request).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US5 as above
- Paths are from the repository root.

---

## Phase 1: Setup

**Purpose**: Get the branch and the test doubles ready.

- [ ] T001 Rebase `feature/digipay-dd-everywhere` onto `main` once spec 034 is merged. Confirm `src/DigiByte.Pay.Api/Data/DdAddress.cs` and `tests/DigiByte.Pay.Api.Tests` `FakeDdNode` exist. If 034 isn't merged yet, stop after Phase 2 and US5.
- [X] T002 [P] Extend the fake explorer used by `tests/DigiByte.Pay.Api.Tests/PayTestHost.cs` (`FakeReceiver`) so a test can script transactions per address and trigger one `InvoiceMonitor` tick on demand (needed by T036 and T037).
- [X] T003 [P] Extend `FakeDdNode` in `tests/DigiByte.Pay.Api.Tests` with scriptable `address/new` results (a sequence of addresses, or failure), per-address `balance` responses, and a `health` toggle.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The gate, the DD availability check, error messages and the schema that every story uses.

**⚠️ No user story work starts until this phase is done.**

- [X] T004 Add `DdSurfacesGate` in `src/DigiByte.Pay.Api/Services/DdSurfacesGate.cs`. It reads config `DigiPay:DdSurfacesEnabled` (env `DIGIPAY_DD_SURFACES`), default `false`, and exposes `bool Enabled`. Register it as a singleton in `src/DigiByte.Pay.Api/Program.cs` (research R8).
- [X] T005 Add `DdAvailability` in `src/DigiByte.Pay.Api/Services/DdAvailability.cs`. It wraps `DigiDollarNodeClient.IsHealthyAsync()` with a 30-second `IMemoryCache` entry and a 3-second timeout, and returns false on any exception. Register it as a singleton in `src/DigiByte.Pay.Api/Program.cs` (research R10).
- [X] T006 Add rate-limit policy `checkout-option` (`PerIp(ctx, 30, TimeSpan.FromMinutes(1))`) next to the existing policies in `src/DigiByte.Pay.Api/Program.cs`.
- [X] T007 Schema in `src/DigiByte.Pay.Api/Data/`:
  - New `PaySessionOption.cs` with `SessionId`, `Currency` (`"DGB"` or `"DD"`), `Address`, `AmountSatoshis` ("0 for DD"), `AmountCentsDd` ("DD only. 100 to 10,000,000"), `UsdRate` ("units of FiatCurrency per 1 USD; 1 for USD"), `RateAt` and `CreatedAt`.
  - `PaySession`: change `Currency` and `AmountSatoshis` from `init` to `set`, and add `BuyerChoice` (bool, default false), `LockedAt`, `ExtraCurrency`, `ExtraAmount`, `ExtraTxid` and `ExtraSeenAt`.
  - `PayStore`: add `BuyerChoiceEnabled` (bool, default true) and `DdDonationAddress` (`string?`).
  - `PayMerchant`: add `DdChoiceNoticePending` (bool, default false).
- [X] T008 Configure `PaySessionOptions` in `src/DigiByte.Pay.Api/Data/DigiPayDbContext.cs`: FK to `PaySession` with cascade delete, and a **unique index (`SessionId`, `Currency`)**.
- [X] T009 Add the EF migration `src/DigiByte.Pay.Api/Migrations/<timestamp>_AddDdEverywhere.cs` for T007 and T008. Its data step sets `DdChoiceNoticePending = true` for every merchant with any store where `DigiDollarEnabled = true` (research R12).
- [X] T010 Add the matching SQLite column and table shims for T007 in `src/DigiByte.Pay.Api/Program.cs`, following the existing DD-column shims.
- [X] T011 Make `GetUsdRateAsync(code)` public on `src/DigiByte.Pay.Api/Services/DgbPriceService.cs`, returning `null` when no rate is available. USD returns exactly 1 (research R3).
- [X] T012 [P] Add error messages to `src/DigiByte.Pay.Web/Components/Shared/Ui/ApiError.cs` (contract C1):
  - `digidollar_node_unavailable` → "DigiDollar is temporarily unavailable. Try again, or take this payment in DGB."
  - `dd_option_unavailable` with `no_rate` → "DigiDollar isn't available for this payment right now."
  - `dd_option_unavailable` with `out_of_range` → "This amount is outside DigiDollar's limits ($1.00 to $100,000.00)."
- [X] T013 Expose `ddSurfacesEnabled` (from T004) and `ddChoiceNoticePending` on `GET /v1/pay/me` in `src/DigiByte.Pay.Api/Endpoints/MerchantMeEndpoints.cs`. Store DTOs also get `buyerChoiceEnabled`, `ddDonationAddress` and `defaultCurrencyEffective` (`"USD"` when the stored default is `"DD"` but DD is off). Add the matching fields to the Dashboard's store and me DTOs in `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`, and to the POS's DTOs in `src/DigiByte.Pay.Web/Components/Pages/Pos.razor` (contract C2).
- [X] T014 [P] Test `tests/DigiByte.Pay.Api.Tests/DdSurfacesGateTests.cs`: with the gate off, `/v1/pay/me` reports `ddSurfacesEnabled:false`, and explicit API `currency:"DD"` sessions still work as today (SC-004). A DD session created while DD is on still reaches Confirmed after the merchant turns DD off (FR-002).

**Checkpoint**: The schema and shared services are in place. With the gate off, nothing changes for anyone.

---

## Phase 3: User Story 1 - Charge in DigiDollar at the POS till (P1) 🎯 MVP

**Goal**: A cashier at a store with DigiDollar on can set the till to DD, charge in dollars, and see DD on every screen and in shift totals.

**Independent Test**: Quickstart step 3, plus the DD-down part of step 10.

### Tests for User Story 1

- [X] T015 [P] [US1] Test `tests/DigiByte.Pay.Api.Tests/PosShiftTotalsDdTests.cs`: Paid and Confirmed DD sessions add `ReceivedSatoshis` (cents) to `collectedDdCents` and add 0 to `collectedDgb`. The Expired and Underpaid split works the same way.
- [X] T016 [P] [US1] Tests in `tests/DigiByte.Pay.Web.Tests/PosTillTests.cs`:
  - DD appears in the currency choice only when the store has DD on and `ddSurfacesEnabled`.
  - A DD charge posts `currency:"DD"`, `amountCentsDd` and an `Idempotency-Key` header.
  - A saved DD till currency on a store with DD off falls back to the store default and shows a toast.
  - A `503 digidollar_node_unavailable` keeps the typed amount and offers "Charge in DGB".
  - Switching the till to DD keeps a USD amount as is and clears an amount typed in another fiat currency or in DGB, never converting it (spec edge case).
- [X] T017 [P] [US1] Test `tests/DigiByte.Pay.Web.Tests/TerminalStateTests.cs` (new): `Currency="DD"` with `ReceivedSatoshis=1250` renders "12.50 DD". DGB rendering is unchanged.

### Implementation for User Story 1

- [X] T018 [P] [US1] Add `Dd = "DD"` to `src/DigiByte.Pay.Web/Components/Pages/Pos/PosEntry.cs`. Also add `IsDd`, make `DecimalsFor("DD")` return 2, and have `NormalizeCurrency` accept `"DD"`.
- [X] T019 [P] [US1] Fix `FormatAmount` in `src/DigiByte.Pay.Web/Components/Pages/TerminalState.razor`: DD amounts are cents, formatted `0.00` with the suffix " DD".
- [X] T020 [P] [US1] Add `collectedDdCents` (and the DD split for expired and underpaid) to shift totals in `src/DigiByte.Pay.Api/Endpoints/PosEndpoints.cs`. Filter the DGB sums to `Currency == "DGB"`.
- [X] T021 [US1] Update `src/DigiByte.Pay.Web/Components/Pages/Pos.razor` for the DD till:
  - Show DD in the `CurrencyPicker` only when the store has DigiDollar on and `ddSurfacesEnabled` is set.
  - A DD till takes dollars with 2 decimals and has no price lookup or drift warning.
  - Reject amounts outside 1.00 to 100,000.00 before submitting, showing the limit (FR-016).
  - Switching the till to DD keeps a USD amount as is and clears any other amount (another fiat currency or DGB). It never converts it (spec edge case and Assumptions).
- [X] T022 [US1] Update `ChargeAsync` in `src/DigiByte.Pay.Web/Components/Pages/Pos.razor`:
  - A DD charge posts `{currency:"DD", amountCentsDd, storeId, label}`.
  - Every charge, DGB included, sends an `Idempotency-Key` header (a new GUID per charge attempt, reused on retry).
  - `CreatedSession` and `SessionStatusDto` gain `Currency` and `AmountCentsDd`.
- [X] T023 [US1] Make the due, waiting, paid (`ConfirmationProgress` at around line 336), late and result screens in `src/DigiByte.Pay.Web/Components/Pages/Pos.razor` use the session's `Currency` and amounts instead of the hardcoded DGB. Show shift totals' DD on its own line.
- [X] T024 [US1] On load in `src/DigiByte.Pay.Web/Components/Pages/Pos.razor`: if the saved till currency is DD and the store no longer has DigiDollar on, or the gate is off, switch to the store default and show a toast explaining why (FR-006).
- [X] T025 [US1] Handle `503 digidollar_node_unavailable` on charge in `src/DigiByte.Pay.Web/Components/Pages/Pos.razor`:
  - show the `ApiError` message
  - keep the typed amount
  - offer one-tap "Charge in DGB", and "Charge in {code}" when the store default is fiat
  - leave no session behind (FR-014, SC-005)
- [X] T026 [US1] Docs for the DD till, in the same PR: the POS section of `src/DigiByte.Pay.Web/Components/Pages/Docs.razor` and `Features.razor`, and `CHANGELOG.md` (constitution, Documentation rule).

**Checkpoint**: The POS takes DD end to end. That's the MVP.

---

## Phase 4: User Story 2 - DD payment link and one-off payment (P2)

**Goal**: Dashboard payment links and the one-off payment form can be priced in DD, DD can be the store's default currency, and stats and CSV show DD correctly.

**Independent Test**: Quickstart step 9, plus a $40 DD link paid in DD that shows 40.00 DD paid on the Dashboard.

### Tests for User Story 2

- [X] T027 [P] [US2] Update `tests/DigiByte.Pay.Api.Tests/StoreDefaultCurrencyTests.cs`:
  - `defaultCurrency:"DD"` is accepted only when the store has `digiDollarEnabled` and the gate is on, and is otherwise `400 digidollar_not_enabled_for_store`. This replaces the assertion at line 57 that DD is always rejected.
  - `defaultCurrencyEffective` is `"USD"` while DD is off.
- [X] T028 [P] [US2] Add DD revenue cases to `tests/DigiByte.Pay.Api.Tests/DailyStatsTests.cs`: DD sessions add to `revenueDdCents` and never to DGB revenue.
- [X] T029 [P] [US2] Tests in `tests/DigiByte.Pay.Web.Tests/DashboardTests.cs`:
  - The link form shows a DD price mode only with DD on and the gate on, and posts `currency:"DD"` with `amountCentsDd`.
  - The new-payment form's DGB/DD switch works.
  - A stored DD default opens the link form on USD while DD is off.
  - Switching either form to DD keeps a USD amount as is and clears an amount in another fiat currency or in DGB.

### Implementation for User Story 2

- [X] T030 [P] [US2] Accept `defaultCurrency:"DD"` in `TryNormaliseCurrency` and the PATCH handler in `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs`, but only when `DigiDollarEnabled` and `DdSurfacesGate.Enabled`. Otherwise return `400 digidollar_not_enabled_for_store`. Never clear the stored value when DD is turned off (research R7).
- [X] T031 [P] [US2] Add DD revenue in cents per day to `src/DigiByte.Pay.Api/Services/DailyStats.cs` and its endpoint DTO. Show it as its own line in `src/DigiByte.Pay.Web/Components/Shared/SalesOverview.razor`.
- [X] T032 [P] [US2] In the CSV export in `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs`, write the amount in the session's own unit (DD from cents) and add a `currency` column.
- [X] T033 [US2] Update `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`:
  - The payment-link price mode gains `"dd"` (a USD amount, 2 decimals, limits 1.00 to 100,000.00, no price lookup). `CreatePaymentLinkAsync` posts `currency:"DD"` and `amountCentsDd` in that mode.
  - The new-payment form gains a DGB/DD switch with the same rules, and `CreateSessionAsync` posts it.
  - Both are shown only with DD on and `ddSurfacesEnabled`.
  - Switching either form to DD keeps a USD amount as is and clears any other amount. It never converts it.
- [X] T034 [US2] Update `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`:
  - The default-currency picker offers DD only with DD on and the gate on.
  - The link form and POS start from `defaultCurrencyEffective`.
  - Payment rows, the session list and `src/DigiByte.Pay.Web/Components/Pages/SessionDetail.razor` show DD amounts in DD.
  - The 503 handling from T025 is reused for the link and new-payment forms.
- [X] T035 [US2] Docs, in the same PR: DD links and one-off payments in `src/DigiByte.Pay.Web/Components/Pages/Docs.razor`; `defaultCurrency:"DD"` and the CSV `currency` column in `ApiDocs.razor`; `CHANGELOG.md`.

**Checkpoint**: Links, one-off payments and stats handle DD.

---

## Phase 5: User Story 3 - Buyer picks DGB or DD at checkout (P2)

**Goal**: A fiat-priced payment from the POS, a link, a one-off payment or an opted-in API call lets the buyer pay in DGB or DD. It locks on first funds and stays one payment.

**Independent Test**: Quickstart steps 4 to 8 and 13.

### Tests for User Story 3

- [X] T036 [P] [US3] Test `tests/DigiByte.Pay.Api.Tests/BuyerChoiceSessionTests.cs`:
  - `buyerChoice:true` is honoured only when every condition in research R5 holds, and is otherwise ignored (no error, empty `options`).
  - A DGB option is created at creation.
  - A store whose default is DD starts on DD, and falls back to DGB when the node fails. When `DdAvailability` reports down it starts on DGB without calling the node, and a node that doesn't answer is cut off after 3 s (SC-002).
  - An API request without `buyerChoice` gets the old response shape plus empty `options` (SC-010).
  - Payments with an amount typed in DGB never get the choice (FR-026).
- [X] T037 [P] [US3] Test `tests/DigiByte.Pay.Api.Tests/DdOptionEndpointTests.cs` (contract C1, `POST /sessions/{id}/options/dd`):
  - creates once, and returns the same option on repeat and concurrent calls
  - USD amounts at 1:1
  - PHP rounds **up** to the cent and stores `UsdRate` and `RateAt`
  - every 409 reason: `no_choice`, `locked`, `not_pending`, `expired`, `dd_disabled`, `no_rate`, `out_of_range`
  - a 503 when the node fails
  - writes a `DdAddress` row (`Kind = payment`, `SessionId`)
  - rate-limited
- [X] T038 [P] [US3] Test `tests/DigiByte.Pay.Api.Tests/InvoiceMonitorBuyerChoiceTests.cs`:
  - Unlocked sessions watch both options. The first funds seen lock the session (`Currency`, `Address` and amounts copied from the option, `LockedAt` set).
  - DGB wins a same-tick tie.
  - A DD node error doesn't stop DGB detection.
  - After the lock, funds on the other option set `Extra*` once and send `session.extra_payment` once. Top-ups update `ExtraAmount` without another event.
  - Detection keeps running through the 3-day late window.
  - Switching never changes `ExpiresAt`.
- [X] T039 [P] [US3] Extend `tests/DigiByte.Pay.Api.Tests/WebhookDispatchTests.cs`: the session payload carries `buyerChoice`, `lockedAt` and `extra`, and events after the lock report the paid currency (contract C3).
- [X] T040 [P] [US3] Tests in `tests/DigiByte.Pay.Web.Tests/CheckoutRenderTests.cs`:
  - The switch shows when `buyerChoice && !lockedAt`.
  - Switching to DD calls the options endpoint and redraws the amount and QR.
  - A converted amount shows the rate line.
  - A 409 or 503 keeps DGB and shows the message.
  - `ddAvailable:false` hides DD, including `ddReason:"dd_disabled"` when a DD option already exists.
  - A buyer on DD who hasn't paid moves back to DGB when DD becomes unavailable.
  - Once locked, the locked currency shows with no switch.
- [X] T041 [P] [US3] Add a fiat-till charge test to `tests/DigiByte.Pay.Web.Tests/PosTillTests.cs`: it posts `buyerChoice:true`, the charge screen switches DGB/DD, and the same DD address is shown after switching back.

### Implementation for User Story 3

- [X] T042 [US3] Add `SessionOptionService` in `src/DigiByte.Pay.Api/Services/SessionOptionService.cs`:
  - `CreateDgbOptionAsync(session)`, which copies the session's DGB address and amount.
  - `GetOrCreateDdOptionAsync(session)`:
    - Allowed only for sessions that are Pending, unlocked and unexpired, with the store's DD on and the gate on.
    - Gets a USD rate from `DgbPriceService.GetUsdRateAsync`. `AmountCentsDd = ceil(FiatAmount × 100 / UsdRate)`, which must be "100 to 10,000,000".
    - Calls `DigiDollarNodeClient.GenerateAddressAsync`.
    - Inserts `PaySessionOption` and a `DdAddress` (`Kind = payment`, `SessionId`, `StoreId`) in one transaction. On a unique-index conflict, re-reads the existing row.
    - Returns typed failures matching contract C1's reasons.
  - `LockTo(session, option)`, which copies `Currency`, `Address`, `AmountSatoshis` and `AmountCentsDd` from the option and sets `LockedAt`.
- [X] T043 [US3] Update session creation in `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs`:
  - Accept `buyerChoice` in `CreateSessionRequest`.
  - Apply research R5's conditions, set `BuyerChoice`, and call `CreateDgbOptionAsync`.
  - When the store's effective default is DD, also create the DD option and start the session on it (copy its address and amounts to the session's live fields) without locking: `LockedAt` stays null. Skip this when `DdAvailability` reports down, and cap the `GenerateAddressAsync` call at 3 s. On any failure, stay on DGB.
- [X] T044 [US3] Extend `ToDto` in `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs` with `buyerChoice`, `lockedAt`, `options` (`OptionDto` with `uri` from the existing BIP21 builder) and `extra`. Add `ddAvailable` and `ddReason` to the public `GET /v1/pay/sessions/{id}` response when `buyerChoice` is set: false with `dd_disabled` when the store has DigiDollar off or the gate is off, false with `unavailable` when `DdAvailability` reports down (contract C1).
- [X] T045 [US3] Map anonymous `POST /v1/pay/sessions/{id}/options/dd` in `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs` with `.RequireRateLimiting("checkout-option")`. It calls `SessionOptionService` and maps results to the statuses in contract C1. The policy counts through `TrustedClient.AddressOf`. Check that Pay.Web's `PayApi` HttpClient sends `X-DigiPay-Client-Key` and `X-DigiPay-Client-Address` (the buyer's address) on checkout and POS calls, and add them if not.
- [X] T046 [US3] Update `src/DigiByte.Pay.Api/Services/InvoiceMonitor.cs`:
  - For `BuyerChoice && LockedAt == null`, inspect every option: DGB through the explorer, DD through `GetBalanceAsync`, catching and logging DD errors per tick.
  - On the first funds, call `LockTo` and continue the normal status flow in the same tick. DGB wins a tie.
  - For locked sessions (Seen, Paid, Confirmed, or Expired inside the late window), check the non-locked options. Set `ExtraCurrency`, `ExtraAmount`, `ExtraTxid` and `ExtraSeenAt`, write audit `session.extra_payment`, and dispatch `session.extra_payment` only the first time.
- [X] T047 [P] [US3] Add `buyerChoice`, `lockedAt` and `extra` to the session payload in `src/DigiByte.Pay.Api/Services/WebhookDispatcher.cs`, plus the `session.extra_payment` event name (contract C3).
- [X] T048 [P] [US3] Add `Currency`, `LockedAt` and `AmountCentsDd` to `CheckoutStatusUpdate` in `src/DigiByte.Pay.Api/Hubs/CheckoutHub.cs`, and to every place `InvoiceMonitor` publishes it (research R11).
- [X] T049 [US3] Accept `buyerChoiceEnabled` (bool?, absent = keep, audit key `buyer_choice`) in PATCH in `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs` (FR-029).
- [X] T050 [US3] Add `POST /v1/pay/me/notices/dd-choice/dismiss` (`dps_` session only, 204, idempotent) in `src/DigiByte.Pay.Api/Endpoints/MerchantMeEndpoints.cs`. It clears `DdChoiceNoticePending` (FR-030).
- [X] T051 [US3] Update `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor`:
  - The DTO gets `BuyerChoice`, `LockedAt`, `Options`, `Extra` and `DdAvailable`.
  - Show a DGB/DD switch while unlocked. Picking DD calls the options endpoint and renders that option's amount, address and BIP21 QR. Switching back reuses the stored option.
  - Show a rate line for converted amounts.
  - Handle 409 and 503 with the `ApiError` message and stay on DGB.
  - When `ddAvailable` is false on an unlocked session, hide DD whether or not a DD option exists. If the buyer was looking at DD, move them back to DGB with a note. A session already locked to DD is unaffected.
  - Re-read the session when a SignalR `Currency` change arrives, and replace the switch with the locked currency.
- [X] T052 [US3] Update `src/DigiByte.Pay.Web/Components/Pages/Pos.razor`:
  - A fiat-till charge posts `buyerChoice:true`.
  - The charge screen shows a one-tap DGB/DD switch that calls the options endpoint and redraws the QR.
  - The screen follows the lock.
  - `SessionStatusDto` gains `BuyerChoice`, `LockedAt` and `Options`.
- [X] T053 [US3] Update `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`:
  - Fiat-priced links and one-off payments post `buyerChoice:true`.
  - Add a "Let buyers choose DGB or DD at checkout" toggle under the DigiDollar setting, shown with DD on.
  - Add the one-time notice when `ddChoiceNoticePending && ddSurfacesEnabled`, with "Turn off for {store}" (PATCH `buyerChoiceEnabled:false`) and "Got it" (the dismiss endpoint).
  - Flag payment rows that have an `extra` payment: "Extra DD/DGB received, refund the buyer".
- [X] T054 [US3] Update `src/DigiByte.Pay.Web/Components/Pages/SessionDetail.razor` to show the options offered, the locked currency and time, the rate for a converted DD amount, and any extra payment with its txid.
- [X] T055 [US3] Docs, in the same PR: `buyerChoice`, `options`/`extra`/`lockedAt`, `ddAvailable`/`ddReason`, `POST /sessions/{id}/options/dd` with its 409/503 errors, `session.extra_payment` and `buyerChoiceEnabled` in `src/DigiByte.Pay.Web/Components/Pages/ApiDocs.razor`; buyer choice in `Docs.razor`; the lock and extra-payment flow in `docs/PROCESS_FLOWS.md`; `docs/ARCHITECTURE.md`; the Postman collections; `CHANGELOG.md`.

**Checkpoint**: One fiat-priced link or POS charge can be paid by a buyer holding only DGB or only DD (SC-007).

---

## Phase 6: User Story 5 - The setting says what it actually does (P2)

**Goal**: The DigiDollar setting text and messages are accurate at every stage. This phase can ship before spec 034 and before Phase 3 (plan, Delivery order).

**Independent Test**: Quickstart steps 1 and 2. Every place the text lists offers DD, and no other place does (SC-006).

- [X] T056 [P] [US5] Test in `tests/DigiByte.Pay.Web.Tests/DashboardTests.cs`:
  - With `ddSurfacesEnabled:false`, the setting text lists the API only and says DigiPay holds the DD and it can't be withdrawn yet.
  - With it on, it lists the POS till, payment links and one-off payments, buyer choice at checkout, the donation page and the API, and says 1 DD = 1 USD and that DigiPay pays network fees.
  - On and off messages name the places.
- [X] T057 [US5] Rewrite the DigiDollar setting text at around lines 1155-1163 of `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`, driven by `ddSurfacesEnabled` (FR-017, FR-031). It always says plainly that DigiPay holds the DD buyers pay until it's paid out (and, before payouts, that it can't be withdrawn yet). Once spec 034 is merged, follow 034 FR-040 for the post-payout wording.
- [X] T058 [US5] Make the messages in `SaveDigiDollarEnabledAsync` (`src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`, around lines 2377-2379) name where DD is now offered or no longer offered (FR-018).
- [X] T059 [US5] Docs, in the same PR: the DigiDollar setting section of `src/DigiByte.Pay.Web/Components/Pages/Docs.razor` (where DD is offered, that DigiPay holds the DD until it's paid out) and `CHANGELOG.md`.

**Checkpoint**: The setting text matches what DD actually does, gate on or off.

---

## Phase 7: User Story 4 - Accept DigiDollar donations (P3)

**Goal**: The tip page can offer DD from a fixed, per-store DD donation address, open on either currency, and drop DD when the store turns it off.

**Independent Test**: Quickstart steps 11 and 12, plus the tip-page part of step 10.

### Tests for User Story 4

- [ ] T060 [P] [US4] Test `tests/DigiByte.Pay.Api.Tests/DonationOptionsTests.cs`:
  - `GET /stores/{id}/donation-address?currency=DD` creates `DdDonationAddress` once (with a `DdAddress` row, `Kind = donation`), returns the same address after that, and gives 400 with DD off or the gate off, and 503 when the node fails.
  - `GET /public/stores/{id}/donation-options` returns `ddAddress` only with DD on, the gate on and the address existing, returns `ddAvailable:false` when the node is down, gives 404 for an unknown store, and never derives a DGB address.
- [ ] T061 [P] [US4] Test `tests/DigiByte.Pay.Web.Tests/DonateTests.cs` (new):
  - Without `store`, the page behaves as today.
  - With `store`, the switch shows only when `ddAddress` is present, and `currency=DD` opens on DD with `ddAmount`.
  - A DGB `amount` is never shown as DD.
  - The DD tab is disabled when `ddAvailable` is false.
  - An invalid DD address is not rendered: wrong network, a DGB address, or truncated (address-handling rule).
- [ ] T062 [P] [US4] Add cases to `src/DigiByte.Pay.Web/tests/js/embed.test.mjs`: `DigiPay.button({store, currency, ddAmount})` and `data-store`, `data-currency` and `data-dd-amount` build the right `/pay/button` URL, and `address` is optional when `store` is given.

### Implementation for User Story 4

- [ ] T063 [US4] Extend `GET /v1/pay/stores/{id}/donation-address` in `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs` with `currency=DD`:
  - Gate on, DD on.
  - Create `DdDonationAddress` on first call through `GenerateAddressAsync`, and insert a `DdAddress` with `Kind = donation`.
  - Return `{address, mode:"dd"}`, or 503 on node failure.
  - The address is never rotated or cleared (data-model).
- [ ] T064 [US4] Add anonymous `GET /v1/pay/public/stores/{id}/donation-options` in `src/DigiByte.Pay.Api/Endpoints/PublicEndpoints.cs` with `.RequireRateLimiting("checkout-option")`. It returns `{storeName, network, dgbAddress, ddAddress, ddAvailable, ddReason}` as in contract C2 and never writes anything. Like T045, it counts through `TrustedClient.AddressOf`, so the tip page's calls from Pay.Web must forward the buyer's address with the trusted-client headers.
- [ ] T065 [US4] Update `src/DigiByte.Pay.Web/Components/Pages/Donate.razor`:
  - New query parameters `store`, `currency` and `ddAmount`. When `store` is set, load donation-options.
  - A DGB/DD switch.
  - Validate the DD address with `DigiDollarAddressValidator.IsValid(address, network)` before rendering, and fall back to DGB if it fails.
  - The DD QR is `digibyte:{dd}?amount={usd}&currency=USD`.
  - Unit labels per currency.
  - The DD tab is disabled with "DigiDollar is temporarily unavailable" when `ddAvailable` is false (contract C4).
- [ ] T066 [P] [US4] Add `store`, `currency` and `ddAmount` options, and `data-store`, `data-currency` and `data-dd-amount` auto-mount attributes, to `button()` in `src/DigiByte.Pay.Web/wwwroot/embed/digipay.js`. Make `address` optional when `store` is given.
- [ ] T067 [US4] Update the donation tab in `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor` (around lines 492-560). With DD on and the gate on, add:
  - an "Accept DD" option, which calls `donation-address?currency=DD`
  - an opening-currency choice
  - a suggested DD amount
  - snippet and URL output that include `store`, `currency` and `ddAmount`, with the label "Tip in DGB or DD"
- [ ] T068 [US4] Docs, in the same PR: the donation endpoints in `src/DigiByte.Pay.Web/Components/Pages/ApiDocs.razor`; DD on the tip page in `Docs.razor`; the new `button()` options in the digipay.js README and `src/DigiByte.Pay.Web/wwwroot/embed/demo.html`; `CHANGELOG.md`.

  None of this appears otherwise (FR-012, FR-013).

**Checkpoint**: Every DD surface in the spec works.

---

## Phase 8: Polish & cross-cutting concerns

- [ ] T069 [P] Final docs check across every surface: read `ApiDocs.razor`, `Docs.razor`, `Features.razor`, `docs/PROCESS_FLOWS.md`, `docs/ARCHITECTURE.md`, the digipay.js README and `CHANGELOG.md` together and fix any gaps or contradictions left between stories (FR-019).
- [ ] T070 [P] Add a Playwright case to `tests/DigiByte.Pay.E2E/tests/pos.spec.mjs`: a fiat charge's DGB/DD switch shows a different QR per currency and the same DD address after switching back. Assert the new amount and QR appear within 2 s of the tap (SC-008), and that a DD till charge reaches the QR within 10 s more than a DGB charge (SC-002).
- [ ] T071 Run `dotnet build`, then `dotnet test tests/DigiByte.Pay.Api.Tests` and `dotnet test tests/DigiByte.Pay.Web.Tests`, then `npm test --prefix src/DigiByte.Pay.Web`. Fix any failures.
- [ ] T072 Follow [quickstart.md](quickstart.md) steps 1 to 14 on testnet. Record results in the PR, including the SC-002 and SC-008 timings on testnet, and flag the PR as auth-relevant: two new anonymous endpoints (plan, Constitution Check).

---

## Dependencies & execution order

### Phase dependencies

- **Phase 1 (Setup)**: T001 needs spec 034 merged for anything past Phase 2. T002 and T003 can start straight away.
- **Phase 2 (Foundational)**: blocks every story.
- **US5 (Phase 6)**: needs only Phase 2, and is the safest thing to ship first.
- **US1 (Phase 3)**: Phase 2 plus 034.
- **US2 (Phase 4)**: Phase 2 plus 034. It reuses T025's 503 handling (T034).
- **US3 (Phase 5)**: Phase 2 plus 034. T052 extends the POS work from US1, and T053 extends the Dashboard forms from US2. It can be built without them, but those two tasks touch the same files, so run them after US1 and US2 to avoid conflicts.
- **US4 (Phase 7)**: Phase 2 plus 034. Independent of US1 to US3.
- **Phase 8 (Polish)**: after the stories being shipped.

### Within each story

Tests, then data and services, then endpoints, then UI, then that story's docs task, which ships in the same PR (constitution, Documentation rule). The docs tasks share `Docs.razor`, `ApiDocs.razor` and `CHANGELOG.md`, so they never run in parallel with each other. `Pos.razor` and `Dashboard.razor` are shared files, so tasks on them run one after another, never in parallel.

### Parallel opportunities

- Phase 1: T002 and T003.
- Phase 2: T012 and T014 next to the schema work. T004, T005 and T006 all touch `Program.cs`, so run them in sequence.
- US1: T015, T016 and T017 (tests), then T018, T019 and T020 together.
- US2: T027, T028 and T029 (tests), then T030, T031 and T032 together.
- US3: T036 to T041 (tests) together, then T047 and T048 next to T042.
- US4: T060, T061 and T062 together, and T066 next to T063 to T065.
- US4 and US5 can run alongside US1 to US3. They touch different areas, apart from `Dashboard.razor` (T057, T058 and T067), which must be sequenced with T033, T034 and T053.

## Parallel example: User Story 3

```text
Task: "T036 BuyerChoiceSessionTests in tests/DigiByte.Pay.Api.Tests/BuyerChoiceSessionTests.cs"
Task: "T037 DdOptionEndpointTests in tests/DigiByte.Pay.Api.Tests/DdOptionEndpointTests.cs"
Task: "T038 InvoiceMonitorBuyerChoiceTests in tests/DigiByte.Pay.Api.Tests/InvoiceMonitorBuyerChoiceTests.cs"
Task: "T040 CheckoutRenderTests in tests/DigiByte.Pay.Web.Tests/CheckoutRenderTests.cs"

# then
Task: "T047 WebhookDispatcher payload in src/DigiByte.Pay.Api/Services/WebhookDispatcher.cs"
Task: "T048 CheckoutStatusUpdate in src/DigiByte.Pay.Api/Hubs/CheckoutHub.cs"
```

## Implementation strategy

### First merge (before spec 034)

Phase 2 and US5 (T004 to T014, T056 to T059). With the gate off, this changes only the setting text and messages, and T059 documents it in the same PR. It makes today's API-only DD described accurately straight away (FR-017, FR-031).

### MVP (after spec 034)

Phase 1, then US1 (the POS DD till). Validate with quickstart step 3 on testnet. The gate stays off in production.

### Incremental delivery

US2 (links), then US3 (buyer choice), then US4 (donations), then Polish. Set `DIGIPAY_DD_SURFACES=true` in production only after 034's payouts are live there (quickstart, Release step).

## Notes

- [P] means a different file and no dependency on an incomplete task.
- Commit after each task or logical group, using Conventional Commits (`feat(digipay): …`, `test(digipay): …`).
- If scope changes during implementation, update `plan.md` and `tasks.md` rather than merging it silently (constitution, Spec-Kit workflow).
