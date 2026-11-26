# Research: DigiDollar on every DigiPay payment surface

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-27

Each item records the decision, why, and what else was considered. File references are to `src/` as of `main` at `ad41c44`.

## What the code does today

- `PaySession` has one `Address` and one `Currency` (`"DGB"` default, `init`). DD sessions keep `AmountSatoshis = 0`, put the requested USD cents in `AmountCentsDd`, and reuse `ReceivedSatoshis` for cents received (`Data/PaySession.cs`).
- Status values: Pending, Seen, Paid, Confirmed, Expired, Underpaid, Refunded, Voided. There is no Overpaid or Late status.
- `POST /v1/pay/sessions` (`Endpoints/PaymentsEndpoints.cs:129-327`) accepts `currency` DGB or DD. DD needs `amountCentsDd` between 100 and 10,000,000 and `store.DigiDollarEnabled`, else `400 digidollar_not_enabled_for_store`. It calls `DigiDollarNodeClient.GenerateAddressAsync()`, and any exception returns `503 digidollar_node_unavailable`. Idempotency uses the `Idempotency-Key` header, per merchant, for 24 hours.
- `InvoiceMonitor` (10 s loop) branches purely on `session.Currency`.
  - DGB sessions are checked through the explorer.
  - DD sessions are checked through `GetBalanceAsync(address)`. DD never reaches Paid: confirmed cents at or above the minimum go straight to Confirmed.
  - Expired sessions are watched for 3 more days to catch late payments, which get the audit row `session.late_reconciled` (DGB only).
- Webhook events are `session.{status}` plus `session.refunded`/`session.voided`. The payload already carries `currency` and `amountCentsDd`.
- The SignalR `CheckoutStatusUpdate` (`Hubs/CheckoutHub.cs:30-37`) has no currency field.
- POS (`Pos.razor`, `Pos/PosEntry.cs`): till currency is DGB or a fiat code. A charge posts `{amount, storeId, label, fiatCurrency, fiatAmount, dgbPriceAtCreation}` with no currency and no idempotency key. The paid screen hardcodes `Currency="DGB"` (line 336).
  - `TerminalState.FormatAmount` divides by 1e8 for DD as well, so DD cents show wrong. This is a bug today.
  - Shift totals (`PosEndpoints.cs:245-345`) sum `AmountSatoshis`, so DD adds 0.
- Dashboard: the new-payment form posts a DGB amount only. The payment-link form has a DGB/fiat price mode. `DefaultCurrency` rejects `"DD"`, and a test pins that (`StoreDefaultCurrencyTests.cs:57`). `DailyStats` counts DGB revenue only. CSV export divides every amount by 1e8.
- Donation: `GET /stores/{id}/donation-address` returns the DGB address only. `/pay/button` (`Donate.razor`) takes `to`, `amount`, `label` and `name`, and validates `to` with the DGB-only `AddressValidator`, so a DD address is rejected.
- `DigiDollarNodeClient.IsHealthyAsync` exists but nothing calls it. `ApiError` has no message for `digidollar_node_unavailable`.
- Spec 034 (payouts) adds a `DdAddress` table that records every DD address DigiPay creates, with its owner and kind (`payment` or `donation`). Its ledger already has an `extra_refund` source for "DD in the currency the buyer didn't lock to (033)".

## R1. Where a buyer-choice payment keeps its second currency

- **Decision**: Add a child table `PaySessionOption`, one row per currency the payment can be paid in (address, amount, rate). The session's own `Currency`, `Address`, `AmountSatoshis` and `AmountCentsDd` always describe the currency **in effect**. That is DGB (or the store's DD default) until the buyer locks, then the locked currency. On a lock to DD, the session's fields are switched to the DD option's values in one transaction. `Currency` and `AmountSatoshis` change from `init` to `set`.
- **Rationale**:
  - Everything downstream already branches on `session.Currency` and reads `session.Address`: the monitor, DTOs, receipt, webhooks, terminal screens, stats and CSV. Keeping those fields equal to "what is being paid" means none of it needs to know about buyer choice. Only the monitor and checkout need to see the other option.
  - The option rows keep the abandoned currency's address, so extra funds sent to it can still be found (FR-025).
- **Alternatives considered**:
  - Extra nullable columns on `PaySession` (`DdOptionAddress` and so on). That's simpler for exactly two currencies, but it doubles the meaning of several columns and makes "which address is live" a conditional everywhere.
  - Two linked sessions per order. Rejected: FR-027 requires one payment with one order reference, and merchants' webhooks and idempotency keys would see two sessions.

## R2. When the DD address for a buyer-choice payment is created

- **Decision**: Lazily, the first time the buyer picks DD, through a new anonymous endpoint `POST /v1/pay/sessions/{id}/options/dd`. It returns the existing option if there is one, so there is at most one DD address per session (FR-023). The DGB address is allocated at creation, as today.
- **Rationale**: The spec assumption says payments paid in DGB never use up a DD address. Node calls happen only on demand, and a DD outage can't block creating DGB-priced payments (Principle V).
- **Abuse limits**: it's anonymous because the checkout page is anonymous. Rate limits count through `TrustedClient.AddressOf`, as every `PerIp` policy does, so Pay.Web's server-side calls must send the trusted-client headers with the buyer's address, or every buyer shares Pay.Web's own bucket. It can create at most one address per session, only for sessions that are Pending, unlocked, unexpired and have buyer choice. It uses a new per-IP rate-limit policy, `checkout-option` (30 a minute).
- **Alternatives considered**:
  - Creating both addresses at session creation. Rejected: it burns DD addresses on every POS sale and makes DGB creation depend on the DD node.
  - Merchant-authenticated creation. Not possible, because the buyer's browser has no merchant credentials.

## R3. Converting a non-USD price to DD

- **Decision**:
  - `DD cents = ceil(FiatAmount × 100 / usdRate(FiatCurrency))`, where `usdRate` is the units of that currency per 1 USD. The rate comes from `DgbPriceService`'s existing USD-based rates table (`GetUsdRateAsync`, which becomes public).
  - For USD the rate is exactly 1.
  - The rate and the time it was taken are stored on the option row and shown on checkout.
  - The amount is fixed until the session expires (clarification Q1).
  - If no rate is available, or the result falls outside 100 to 10,000,000 cents, the endpoint returns `409 dd_option_unavailable` with a reason, and checkout disables the DD choice for that payment.
- **Rationale**: The project already uses this rates table for fiat pricing. Rounding up to the cent means the merchant never receives less than the price.
- **Alternatives considered**: deriving USD through the DGB price (`dgbUsd / dgbFiat`). Rejected: it adds DGB's volatility and two quotes to a fiat-to-fiat conversion.

## R4. Detecting the lock and extra payments

- **Decision**:
  - For a buyer-choice session that hasn't locked, `InvoiceMonitor` checks **every option's address** each tick. That's the explorer for DGB and `GetBalanceAsync` for DD. The first option with any funds seen (0 confirmations) wins:
    - the session's fields switch to that option (R1)
    - `LockedAt` is set
    - the normal status flow carries on from there
  - After the lock, and until the 3-day late window ends (spec FR-025), the other options' addresses are checked on each tick. Funds found there are recorded on the session as an extra payment (`ExtraCurrency`, `ExtraAmount`, `ExtraTxid`, `ExtraSeenAt`). They raise the new webhook `session.extra_payment` and the audit row `session.extra_payment`, and the Dashboard shows them.
  - If both addresses show funds in the same tick, DGB wins the lock, so the DD becomes the extra and can be paid out for refund through 034.
- **Rationale**: This reuses the existing loop and grace window. A DD-node error during the unlocked check is logged and skipped, so the DGB option keeps being watched (FR-015).
- **Alternatives considered**: locking only on the buyer's click. Rejected: a buyer can scan a QR without clicking, and the spec locks on funds seen.

## R5. Who gets buyer choice

- **Decision**: A session gets buyer choice when **all** of these hold:
  - the request has `buyerChoice: true`
  - the store has `DigiDollarEnabled` and `BuyerChoiceEnabled`
  - the DD surfaces gate is on (R8)
  - the request has `currency` DGB (or none)
  - the request has `fiatCurrency` and `fiatAmount` (the price is in a regular currency)

  Otherwise the flag is ignored and the session is created as it is today.
  - The POS and Dashboard always send `buyerChoice: true` on fiat-priced payments.
  - API integrations opt in by sending it (FR-028).
  - The session's starting currency (FR-021) is DD when the store's default currency is DD. The DD option is then created at session creation, because it's what the buyer sees first (the spec's assumption allows this exception).
  - Before calling the node, creation checks `DdAvailability` (R10). If it reports down, the session starts on DGB straight away. Otherwise the `GenerateAddressAsync` call gets a 3-second timeout, and on failure the session starts on DGB. A POS charge never waits the client's full 15 s on a down node (SC-002, Principle V).
- **Rationale**: Existing integrations see no change (SC-010). Payments with an amount typed in DGB have no fiat price, so they're excluded (FR-026).

## R6. DD on the POS till

- **Decision**:
  - `PosEntry` gains `Dd = "DD"` with 2 decimals. `NormalizeCurrency` accepts `"DD"`.
  - The picker shows DD only when the store has DigiDollar on and the gate is on.
  - A DD till posts `{currency:"DD", amountCentsDd, storeId, label}` with an `Idempotency-Key`, which the POS doesn't send today.
  - A fiat till posts `buyerChoice:true`. The charge screen shows a DGB/DD switch that calls the options endpoint (R2) and redraws the QR.
  - `CreatedSession`/`SessionStatusDto` gain `Currency`, `AmountCentsDd`, `BuyerChoice`, `Options` and `LockedAt`.
  - The paid, waiting and late screens pass the session's currency through.
  - `TerminalState.FormatAmount` formats DD from cents.
  - Shift totals gain `collectedDdCents`.
  - A saved DD till currency on a store without DD falls back to the store default, with a toast (FR-006).
  - On `503 digidollar_node_unavailable` the till keeps the typed amount and offers "Charge in DGB" and, if the store default is fiat, "Charge in {code}" (FR-014).
- **Rationale**: These are the smallest changes to the existing till state machine. The amount-entry rules for 2-decimal currencies already exist for fiat.

## R7. Dashboard payment link, new payment and default currency

- **Decision**:
  - The link form's price mode gains `"dd"` (USD amount, 2 decimals, no price lookup). The new-payment form gains a DGB/DD currency switch.
  - Fiat-priced links send `buyerChoice:true`.
  - `PATCH /stores/{id}` accepts `defaultCurrency:"DD"` only when the store has DigiDollar on and the gate is on, and otherwise returns `400 digidollar_not_enabled_for_store`.
  - A saved `"DD"` default on a store with DigiDollar off is read as `"USD"` by the Dashboard and POS (FR-008). The stored value is left alone, so turning DD back on restores it.
  - `DailyStats` gains DD revenue in cents. `SalesOverview` shows it as its own line and never adds it into DGB.
  - CSV export writes DD amounts from cents with the unit `DD`.
- **Alternatives considered**: clearing `DefaultCurrency` when DD is turned off. Rejected: it loses the merchant's choice for a reversible toggle.

## R8. The FR-032 gate (no DD surfaces before payouts)

- **Decision**: A server setting `DigiPay:DdSurfacesEnabled` (env `DIGIPAY_DD_SURFACES`), default `false`. While it's off:
  - the API ignores `buyerChoice`
  - it rejects `defaultCurrency:"DD"`
  - the options and donation-options endpoints report DD as unavailable
  - `/v1/pay/me` returns `ddSurfacesEnabled:false`, so the Dashboard and POS hide every new DD control

  Explicit API `currency:"DD"` keeps working as it does today. The setting is turned on in production only once spec 034's payouts are live, and that's a release step in the quickstart.
- **Rationale**: Spec 034 needs the production DD node wallet (034 research R11), which doesn't exist yet. A code-merge ordering alone wouldn't hold back production.
- **Alternatives considered**: checking for a store payout address at runtime. Rejected: it would light up DD per store before the operator has confirmed payouts work in production.

## R9. The store's DD donation address

- **Decision**:
  - `PayStore.DdDonationAddress` (string?) is created once, on demand, by `GET /stores/{id}/donation-address?currency=DD` (merchant auth). It's generated through `GenerateAddressAsync` and registered as a 034 `DdAddress` with `Kind = donation`.
  - The tip page gets a new optional `store` query parameter. When it's present, the page calls a new anonymous `GET /v1/pay/public/stores/{id}/donation-options`, which returns:
    - `{ dgbAddress?, ddAddress?, ddAvailable, ddReason? }`
    - `ddAddress` only when the store has DD on and the gate is on
    - `ddAvailable` from the cached node health (R10)
  - New query parameters `currency` (`DGB` or `DD`) and `ddAmount` set the starting currency and the suggested DD amount (FR-012).
  - Old buttons (`to` only) keep working, DGB only.
  - `Donate.razor` validates any DD address it renders with `DigiDollarAddressValidator` for the store's network (address-handling rule).
  - `digipay.js` `button()` gains `store`, `currency` and `ddAmount` options.
- **Rationale**: A static `to=` link can't know DigiDollar was turned off later (Story 4, scenario 3). Looking it up by store id keeps the page accurate, and DGB-only buttons don't change.
- **Alternatives considered**: putting the DD address in the query string. Rejected for that reason.

## R10. Knowing whether DD is available

- **Decision**: A new singleton `DdAvailability` wraps `DigiDollarNodeClient.IsHealthyAsync()` with a 30-second cache and a 3-second timeout. It's used by:
  - the public session GET (`ddAvailable` and `ddReason`). `ddAvailable` is also false, with `ddReason:"dd_disabled"`, when the store has DigiDollar off or the gate is off, so a store turning DD off removes the DD choice even after an option was created (spec edge case). Otherwise the node-down reason is `unavailable`.
  - donation-options
  - `/v1/pay/me`

  Session creation still relies on the real call failing (503) rather than on the cache.
  - `ApiError` maps `digidollar_node_unavailable` to "DigiDollar is temporarily unavailable. Try again, or take this payment in DGB."
  - When `ddAvailable` is false, checkout hides the DD choice for an unlocked session, whether or not a DD option exists. A buyer looking at DD who hasn't paid is moved back to DGB with a note (edge case). A locked DD session isn't affected.
- **Rationale**: DGB checkout and session creation never wait on the DD health check (Principle V).

## R11. Telling the checkout page about the lock

- **Decision**: `CheckoutStatusUpdate` gains `Currency`, `LockedAt` and `AmountCentsDd`. Checkout re-reads the session on any update where `Currency` changed. The polling fallback picks it up from the GET anyway.
- **Rationale**: The checkout must switch to showing the locked currency and hide the switch (Story 3, scenario 5). Old clients ignore unknown SignalR fields.

## R12. The one-time notice (FR-030) and the buyer-choice setting (FR-029)

- **Decision**:
  - `PayStore.BuyerChoiceEnabled` (bool, default `true`), patched through `PATCH /stores/{id}` as `buyerChoiceEnabled`.
  - `PayMerchant.DdChoiceNoticePending` (bool). The migration sets it to true for merchants who have any store with `DigiDollarEnabled = true`.
  - The Dashboard shows the notice only when it's pending **and** the gate is on, with "Turn off for {store}" and "Got it". Dismissing calls `POST /v1/pay/me/notices/dd-choice/dismiss`, which clears the flag for good.
- **Rationale**: A per-merchant flag set once at migration matches "one-time" exactly. Merchants who turn DD on later learn about buyer choice from the setting text (FR-017), so they don't need the notice.

## R13. Tests

- **Decision**:
  - **Pay.Api (xUnit + `WebApplicationFactory`)**: uses the `FakeDdNode` handler from 034. If 033 lands first, it adds that handler. A fake explorer drives `InvoiceMonitor` ticks for lock and extra-payment cases. New suites:
    - `BuyerChoiceSessionTests`
    - `DdOptionEndpointTests`
    - `InvoiceMonitorBuyerChoiceTests`
    - `DonationOptionsTests`
    - `PosShiftTotalsDdTests`
    - `DdSurfacesGateTests`
    - updates to `StoreDefaultCurrencyTests` (DD allowed only with DD on and the gate on) and `DailyStatsTests`
  - **Pay.Web (bUnit)**: `PosTillTests` (DD till, switch, 503 fallback), `CheckoutRenderTests` (switch, rate line, locked state, DD unavailable), `DashboardTests` (link DD mode, notice, setting text), and a `TerminalState` DD amount case.
  - **JS (`node:test`)**: `embed.test.mjs` for the new `button()` options.
  - **E2E (Playwright)**: one `pos.spec.mjs` case for the DGB/DD switch on a fiat charge.
- **Rationale**: This follows the existing test projects. No `DigiByte.Crypto` or `DigiByte.Wallet` code changes, so Principle III's merge gate doesn't apply, but address validation on the tip page is still covered by a negative test (DGB address passed as `ddAddress`).

## R14. Ordering with spec 034

- **Decision**: 034 merges first. 033 writes `DdAddress` rows for DD option addresses (`Kind = payment` with `SessionId`) and the donation address (`Kind = donation`). 034's ledger scanner then credits them, and classifies DD on a non-locked option as `extra_refund`.
- **Rationale**: Without those rows, 034's ledger can't attribute the DD to a store. If 033 had to go first, it would need its own minimal `DdAddress` table, duplicating 034's work, so it doesn't.
