# Implementation Plan: DigiDollar on every DigiPay payment surface

**Branch**: `feature/digipay-dd-everywhere` (planned; work is still on `main`) | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/033-digipay-dd-everywhere/spec.md`

## Summary

Today only API callers can create DigiDollar (DD) payments. This feature brings DD to:
- the POS till
- Dashboard payment links and one-off payments
- the store's tip page
- a new buyer choice of DGB or DD on fiat-priced payments, on hosted checkout and on the POS charge screen

Everything ships behind a server gate (`DIGIPAY_DD_SURFACES`) that stays off until spec 034's payouts are live, so DigiPay doesn't start holding more merchant DD with no way out (FR-032).

**Technical approach**
- **Payment options**: a buyer-choice payment stays one `PaySession` with child `PaySessionOption` rows, one per currency. The session's own currency, address and amount always describe the currency in effect. When funds are first seen on an option, the monitor locks the session to it by copying that option's values onto the session. Everything that already reads `session.Currency` (receipts, webhooks, terminal screens, stats) then works unchanged (research R1, R4).
- **DD address on demand**: the DD option's address is created the first time the buyer picks DD, through an anonymous, rate-limited endpoint that allows at most one DD option per session (R2). A non-USD price is converted at the USD rate DigiPay already has and fixed until expiry (R3).
- **Extra payments**: DD that arrives on the option the session didn't lock to is recorded as an extra payment and sent as a new webhook. Spec 034 pays it out labelled for refund (R4, R14).
- **Small existing bugs fixed on the way**: DD amounts on the POS terminal screens, DD in shift totals, stats and CSV export, and the missing message for `digidollar_node_unavailable`.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`net10.0`), plus vanilla JS for `wwwroot/embed/digipay.js`.

**Primary Dependencies**:
- Existing: ASP.NET Core minimal APIs and SignalR (`DigiByte.Pay.Api`), Blazor Web App with InteractiveServer (`DigiByte.Pay.Web`), EF Core (Npgsql in production, SQLite in dev and tests).
- `DigiByte.Crypto`: `DigiDollarAddressValidator`, `AddressValidator`.
- `DigiDollarNodeClient` to NodeApi `/api/digidollar/*`.
- `DgbPriceService` for USD rates.
- No new packages.

**Storage**:
- Postgres migration `AddDdEverywhere`:
  - new table `PaySessionOptions`
  - new `PaySession` columns: `BuyerChoice`, `LockedAt`, `ExtraCurrency`, `ExtraAmount`, `ExtraTxid`, `ExtraSeenAt`
  - new `PayStore` columns: `BuyerChoiceEnabled`, `DdDonationAddress`
  - new `PayMerchant` column: `DdChoiceNoticePending`
- Matching SQLite shims in `Program.cs`.
- Writes 034's `DdAddress` rows. See [data-model.md](data-model.md).

**Testing**:
- `tests/DigiByte.Pay.Api.Tests` (xUnit + `WebApplicationFactory`, with 034's `FakeDdNode` and a fake explorer for monitor ticks).
- `tests/DigiByte.Pay.Web.Tests` (bUnit).
- `src/DigiByte.Pay.Web/tests/js` (`node:test`).
- One Playwright case in `tests/DigiByte.Pay.E2E`.
- The testnet walk-through in [quickstart.md](quickstart.md).

**Target Platform**: Pay.Api and Pay.Web on Railway. They need NodeApi and digibyted with the DD wallet, the same production prerequisite as 034 (034 research R11).

**Project Type**: Web services: the API plus the Blazor Dashboard, POS and hosted pages.

**Performance Goals**:
- The DGB/DD switch shows the new amount and QR within 2 s (SC-008). In practice that's one NodeApi `address/new` call on the first switch and a cached read after that.
- A POS DD charge adds under 10 s compared with DGB (SC-002).

**Constraints**:
- DD amounts run from 1.00 to 100,000.00 in whole cents.
- DGB creation and monitoring must never wait on the DD node (Principle V, FR-015). The one node call at creation (stores whose default is DD) is skipped when `DdAvailability` reports down and is capped at 3 s otherwise.
- Existing API integrations see no behaviour change (SC-010).
- The monitor is single-instance (10 s loop), as today.

**Scale/Scope**:
- Few stores, and DD is beta.
- 1 new table, about 10 new columns, 3 new endpoints, 2 changed endpoints, 1 new webhook event.
- About 8 Razor components touched, plus the embed script.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / rule | Applies? | Assessment |
|---|---|---|
| I. Self-custody | **Yes, with a note** | Wallet users' keys aren't touched. This feature routes more merchant payments through DigiPay-held DD, which is the custody concern spec 034 records and that clarification Q3 answered: no DD surface goes live before payouts (FR-032, gate R8), and the Dashboard says plainly that DigiPay holds the DD (FR-031). **PASS**. It relies on 034's Complexity Tracking item and its constitution follow-up (034 R12). |
| II. In-browser cryptography | Yes | No signing, keys or seeds. The new anonymous endpoint only asks DigiPay's node for a receive address. **PASS** |
| III. Test-first (Crypto/Wallet) | Indirectly | No `DigiByte.Crypto` or `DigiByte.Wallet` changes. The tip page's new DD-address check reuses `DigiDollarAddressValidator` and gets negative tests in Pay.Web: wrong network, a DGB address and a truncated address. **PASS** |
| IV. Boundary discipline | Yes | Pay.Web and Pay.Api reuse `DigiByte.Crypto` validators. The node is reached only through NodeApi. The USD rate comes from the existing `DgbPriceService`, not a second rate source. **PASS** |
| V. Resilience | **Yes** | Every surface has a defined DD-down state (FR-014, research R10). DGB paths don't call the DD node: a buyer-choice session creates its DD option lazily, and the monitor skips DD errors per tick. **PASS** |
| Address handling | Yes | Every DD address the tip page shows is validated client-side before it's displayed. Merchants never type DD addresses in this feature. **PASS** |
| Auth boundary | **Yes** | Two new anonymous endpoints: the DD option and donation options. Neither is a merchant API (merchant data still needs `dgp_` or `dps_`) nor a wallet-to-backend call, the two cases the rule covers. Anonymous registration (`POST /merchants`) is the precedent for a public endpoint that writes. Rate limits count through `TrustedClient.AddressOf`, and Pay.Web forwards the buyer's address with the trusted-client headers. Both are rate-limited (`checkout-option`). They can only create a receive address bound to an existing session, or read public donation data. Neither can move funds, change merchant data or read keys. The notice dismiss endpoint needs a `dps_` session. No reliance on network position. Flag the PR as auth-relevant. **PASS** |
| Secret hygiene | Yes | No new secrets. `DIGIPAY_DD_SURFACES` is a plain flag. **PASS** |
| Docs | Yes | Update `ApiDocs.razor` (`buyerChoice`, options endpoint, webhook), `Docs.razor` and `Features.razor` (POS, links, tip page), the digipay.js README and embed demo, `docs/PROCESS_FLOWS.md` (buyer-choice flow), `docs/ARCHITECTURE.md`, the Postman collections and `CHANGELOG.md` (FR-019). |

**Gate result: PASS.** No new Complexity Tracking items beyond the custody item 034 already carries.

**Re-check after Phase 1 design: PASS.**
- The contracts ([C1](contracts/sessions-api.md) to [C4](contracts/embed-and-pages.md)) add no signing.
- Both anonymous endpoints are scoped and rate-limited.
- The gate keeps every new DD surface off until payouts exist.
- Existing request and response shapes only gain optional fields.

## Project Structure

### Documentation (this feature)

```text
specs/033-digipay-dd-everywhere/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── sessions-api.md  # C1 create/get, options/dd, SignalR, error messages
│   ├── stores-api.md    # C2 PATCH, /me, notice, donation endpoints
│   ├── webhooks.md      # C3 payload fields + session.extra_payment
│   └── embed-and-pages.md # C4 tip page, digipay.js, checkout UI rules
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/DigiByte.Pay.Api/
├── Data/
│   ├── PaySession.cs              # Currency/AmountSatoshis settable; BuyerChoice, LockedAt, Extra*
│   ├── PaySessionOption.cs        # new
│   ├── PayStore.cs                # BuyerChoiceEnabled, DdDonationAddress
│   ├── PayMerchant.cs             # DdChoiceNoticePending
│   └── DigiPayDbContext.cs        # PaySessionOptions, unique (SessionId, Currency)
├── Migrations/2026xxxx_AddDdEverywhere.cs   # + notice backfill
├── Services/
│   ├── SessionOptionService.cs    # new: create DGB/DD options, USD conversion, DdAddress registration
│   ├── DdAvailability.cs          # new: cached IsHealthyAsync
│   ├── DdSurfacesGate.cs          # new: reads DigiPay:DdSurfacesEnabled
│   ├── DgbPriceService.cs         # GetUsdRateAsync public
│   ├── InvoiceMonitor.cs          # unlocked multi-option check, lock, extra-payment detection
│   ├── WebhookDispatcher.cs       # buyerChoice/lockedAt/extra fields, session.extra_payment
│   └── DailyStats.cs              # DD revenue in cents
├── Hubs/CheckoutHub.cs            # CheckoutStatusUpdate + Currency, LockedAt, AmountCentsDd
├── Endpoints/
│   ├── PaymentsEndpoints.cs       # buyerChoice on create, DTO options/extra, POST options/dd, CSV units
│   ├── StoresEndpoints.cs         # defaultCurrency DD, buyerChoiceEnabled, donation-address?currency=DD
│   ├── PublicEndpoints.cs         # donation-options
│   ├── MerchantMeEndpoints.cs     # ddSurfacesEnabled, notice + dismiss
│   └── PosEndpoints.cs            # shift totals DD cents
└── Program.cs                     # DI, checkout-option rate limit, SQLite shims

src/DigiByte.Pay.Web/
├── Components/Pages/
│   ├── Pos.razor, Pos/PosEntry.cs # DD till, DGB/DD switch, 503 fallback, idempotency key, DTOs
│   ├── TerminalState.razor        # DD amounts from cents
│   ├── Checkout.razor             # switch, rate line, lock handling, DD unavailable
│   ├── Dashboard.razor            # link "dd" mode, new-payment currency, default currency DD,
│   │                              # DD setting copy (FR-017/031), buyer-choice toggle, notice,
│   │                              # donation tab DD options, extra-payment flag on rows
│   ├── SessionDetail.razor        # options, locked currency, extra payment
│   ├── Donate.razor               # store/currency/ddAmount, donation-options, DD validation
│   └── ApiDocs.razor, Docs.razor, Features.razor
├── Components/Shared/
│   ├── SalesOverview.razor        # DD revenue line
│   └── Ui/ApiError.cs             # new error messages
└── wwwroot/embed/digipay.js       # button(): store, currency, ddAmount

tests/DigiByte.Pay.Api.Tests/      # BuyerChoiceSession, DdOptionEndpoint, InvoiceMonitorBuyerChoice,
                                   # DonationOptions, PosShiftTotalsDd, DdSurfacesGate; StoreDefaultCurrency, DailyStats
tests/DigiByte.Pay.Web.Tests/      # PosTill, CheckoutRender, Dashboard, TerminalState, Donate
src/DigiByte.Pay.Web/tests/js/     # embed.test.mjs
tests/DigiByte.Pay.E2E/tests/      # pos.spec.mjs buyer-choice case
```

**Structure decision**: the work stays inside DigiPay's two existing projects. No NodeApi changes are needed, because `address/new`, `balance` and `health` already exist. One new service class each for options, availability and the gate keeps `PaymentsEndpoints` and `InvoiceMonitor` from growing further conditionals.

## Delivery order

Each step's PR carries its own docs updates (constitution, Documentation rule).

1. **Gate, availability, error messages and FR-017/FR-031 setting copy.** These are safe to ship first: with the gate off they change only text.
2. **Fixes to existing DD display**: `TerminalState` cents, shift totals, `DailyStats`, CSV. These ship inside step 3's stories (tasks US1 and US2).
3. **POS DD till and Dashboard DD link and new-payment forms** (Stories 1 and 2).
4. **Buyer choice**: data model, options endpoint, monitor lock and extra payments, checkout and POS switch, webhooks, notice (Story 3).
5. **Donations** (Story 4).
6. **Final docs check** across every surface.

Step 1 can land before 034. The step 2 fixes ride with the stories that need them (TerminalState and shift totals with the POS DD till, `DailyStats` and CSV with the DD link forms), so they land after 034 with steps 3 to 5 (research R14).

## Complexity Tracking

No new items. The custody item (DigiPay holds merchant DD) is tracked in spec 034's plan. This feature depends on it and is gated behind it.
