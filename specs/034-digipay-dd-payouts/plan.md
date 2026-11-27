# Implementation Plan: DigiDollar payouts to merchants

> **Superseded by [spec 035](../035-digipay-dd-merchant-keys/plan.md)** (2026-11-27). Kept for history only.

**Branch**: `034-digipay-dd-payouts` (planned; work is still on `main`) | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/034-digipay-dd-payouts/spec.md`

## Summary

DD paid to DigiPay stores lands in one shared node wallet (`digipay-hot`), and DigiPay can't send any of it out. This feature lets each store name its own DD payout address and forwards the store's DD there.

**Building blocks**
- **Ledger:** a per-store DD ledger in DigiPay's own database. On-chain addresses alone can't separate stores, because all of them share one wallet.
- **Worker:** a single payout worker. It credits incoming DD to the ledger, applies due address changes, decides when each store is paid, sends through the node wallet's existing `senddigidollar` route, and reconciles every send before it can be retried.
- **Re-sign-in:** a Digi-ID sign-in tied to the merchant's existing Digi-ID address, required to set or change a payout address.
- **Webhooks:** payout and address webhooks need a payload that isn't tied to a session.
- **Dashboard:** a new DigiDollar section.

**Network fees** are paid by the node wallet's DGB, as today. The node wallet must be able to cover every store's held DD before any payout goes out, so one store's payout can never use another store's DD.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`net10.0`).

**Primary Dependencies**:
- Existing: ASP.NET Core minimal APIs (`DigiByte.Pay.Api`), Blazor Web App with InteractiveServer (`DigiByte.Pay.Web`), EF Core (Npgsql in production, SQLite in dev and tests), `DigiByte.Crypto` (`DigiDollarAddressValidator`), `DigiByte.NodeApi` `/api/digidollar/*`.
- No new packages.

**Storage**:
- Production: Postgres with EF migrations (`yyyyMMddHHmmss_Name`).
- Dev and tests: SQLite with `EnsureCreated`, plus the hand-written shims in `Program.cs`.
- New tables: `DdAddresses`, `DdLedgerEntries`, `DdPayouts`, `DdPayoutItems`, `PayoutAddressChanges`, `StepUpGrants`, `MerchantDdSettings`, `OperatorSettings`.
- New `PayStore` columns: payout address, schedule and pause state.
- `WebhookDelivery` gains `PayloadJson`. See [data-model.md](data-model.md).

**Testing**:
- `tests/DigiByte.Pay.Api.Tests` (xUnit with `WebApplicationFactory`, temp SQLite). Adds a `FakeDdNode` HTTP handler for `DigiDollarNodeClient`, which no Pay test fakes today.
- `tests/DigiByte.Pay.Web.Tests` (bUnit) for the DigiDollar section.
- `tests/DigiByte.NodeApi.Tests` for the new send-lookup route.
- A testnet run following [quickstart.md](quickstart.md).

**Target Platform**:
- Pay.Api and Pay.Web on Railway.
- NodeApi plus digibyted, with the DigiDollar wallet `digipay-hot` loaded. Production doesn't run this today; see research R11.

**Project Type**: Web services. The API, the Blazor dashboard and the node gateway each deploy on their own.

**Performance Goals**:
- 95% of confirmed DD reaches the payout address within 15 minutes (SC-002). The worker runs every 30 seconds, and a payout needs 6 confirmations, roughly 90 seconds on DigiByte.
- Address-change alerts appear within 1 minute (SC-006).

**Constraints**:
- DD rules: every DD output is at least 1.00 DD, DD change is 0 or at least 1.00 DD, and only confirmed DD can be spent (research R2).
- `senddigidollar` has no idempotency key.
- Pay.Api runs as a single instance. The worker follows `WebhookRetrier`'s single-instance rule and also claims rows in the database.
- DGB flows must not depend on any of this (FR-036).

**Scale/Scope**:
- Few stores today, and DD is beta.
- About 6 new tables, 1 hosted service, about 10 endpoints, 1 dashboard section, 6 webhook events, 1 NodeApi route.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / rule | Applies? | Assessment |
|---|---|---|
| I. Self-custody | **Yes, with a note** | Wallet users' keys and seeds aren't touched. DigiPay holding merchant DD comes from the current DD design (one node wallet, `digipay-hot`), and this feature shortens it: by default DD is held only until it has 6 confirmations. The constitution doesn't address DigiPay holding merchant funds, and spec 001 already called node-wallet `senddigidollar` custodial. **PASS**, recorded in Complexity Tracking, with a follow-up to add a DigiPay custody clause to the constitution (R12). |
| II. In-browser cryptography | Yes | No user secret material is touched. The node signs payouts with the operator's own wallet keys. That is DigiPay's server moving funds DigiPay already holds, not a server taking part in a user's signing flow. **PASS** |
| III. Test-first (Crypto/Wallet) | Indirectly | `DigiByte.Crypto` and `DigiByte.Wallet` don't change. `DigiDollarAddressValidator` is reused as is, and its negative tests already exist. The Pay tests still cover wrong network, bad checksum, a DGB address, truncation and DigiPay's own addresses at the payout-address endpoint. **PASS** |
| IV. Boundary discipline | Yes | Address checks reuse `DigiByte.Crypto`'s validator, which Pay.Api and Pay.Web already reference. There is no second DD validator. Pay.Api reaches the node only through NodeApi over HTTP. No dependency direction is inverted. **PASS** |
| V. Resilience | **Yes** | The spec defines what merchants see during an outage (FR-034 to FR-037). In the design, an unreachable node means "waiting", never "failed". The Dashboard shows the stored balance with its timestamp. The worker sits outside `InvoiceMonitor`'s DGB path, so DGB can't block on it. **PASS** |
| Address handling | Yes | The payout address is checked in the Pay.Web form before it is shown back or sent (InteractiveServer runs the same `DigiByte.Crypto` check), and again in Pay.Api on save and before every send. **PASS** |
| Auth boundary | **Yes, security-relevant** | Payout-address changes accept only a `dps_` Dashboard session together with a fresh, single-use Digi-ID grant for that purpose and that merchant, signed by the merchant's stored Digi-ID address. `dgp_` API keys are refused. Operator pause uses a new header key (`X-DigiPay-Operator-Key`), never network position. The PR must be flagged security-relevant. **PASS** |
| Secret hygiene | Yes | `DIGIPAY_OPERATOR_KEY` and `NODEAPI_WRITE_KEY` come from the environment only. Logs and audit entries carry addresses and amounts, never keys or tokens. **PASS** |
| Docs | Yes | Update the in-app docs (`ApiDocs`, `Docs`, `Features`, `Home`), the SDK and sample READMEs, the Postman collections, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, `docs/RAILWAY_NODE.md`, `SELF_CUSTODY.md` and `CHANGELOG.md`. `Home.razor`'s "Non-custodial" claim must exclude DD. |

**Gate result: PASS.** One justified item is listed in Complexity Tracking.

**Re-check after Phase 1 design: PASS.**
- The contracts add no browser-reachable signing.
- The single new NodeApi route is wallet-scoped and needs the key (contract C4).
- The step-up grant is bound to its purpose, merchant and session, and works once (C1).
- The operator endpoints need a header key and fail closed when it isn't configured (C3).

The design does need one change to the spec: a DD output can't be below 1.00 DD, so the "final payout of any amount" on store deletion isn't possible. The spec now moves a remainder below 1.00 DD to the merchant's account-level balance instead (FR-015 and the store-deletion edge case; research R2).

## Project Structure

### Documentation (this feature)

```text
specs/034-digipay-dd-payouts/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── merchant-api.md  # C1/C2 dashboard + API-key endpoints
│   ├── operator-api.md  # C3 operator pause/resume
│   ├── nodeapi.md       # C4 NodeApi additions used by Pay.Api
│   └── webhooks.md      # C5 payout + address webhook events
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/DigiByte.Pay.Api/
├── Data/
│   ├── PayStore.cs                    # + payout address/schedule/pause columns
│   ├── DdAddress.cs                   # new: every DD address DigiPay created, owner + kind
│   ├── DdLedgerEntry.cs               # new: credits/debits per store (or merchant)
│   ├── DdPayout.cs, DdPayoutItem.cs   # new
│   ├── PayoutAddressChange.cs         # new
│   ├── StepUpGrant.cs                 # new
│   ├── MerchantDdSettings.cs          # new: payout settings for DD from deleted stores
│   ├── OperatorSetting.cs             # new: key/value (pause, scan cursor, last scan)
│   ├── WebhookDelivery.cs             # + PayloadJson, PayoutId
│   └── DigiPayDbContext.cs
├── Migrations/2026xxxx_AddDdPayouts.cs
├── Services/
│   ├── DigiDollarNodeClient.cs        # + SendAsync, ListSendsAsync, GetTxStatusAsync, GetHealthAsync, ListReceivesAsync
│   ├── DdLedgerScanner.cs             # new: wallet receive scan → ledger credits (source classification)
│   ├── DdLedgerService.cs             # new: balances, liability, hold reason, next payout time
│   ├── DdAddressBackfill.cs           # new: one-off DdAddress backfill for DD sessions from before payouts
│   ├── DdPayoutPlanner.cs             # new: schedule/threshold/min/hold rules → payout intents
│   ├── DdPayoutSender.cs              # new: send + reconcile (checking) + confirm
│   ├── DdPayoutWorker.cs              # new hosted service, 30 s loop, orchestrates the above
│   ├── PayoutAddressService.cs        # new: validate, request/cancel/apply change
│   ├── StepUpService.cs               # new: purpose-bound Digi-ID grants
│   ├── PayoutAlerts.cs                # new: structured operator alerts (log event ids)
│   ├── WebhookDispatcher.cs           # + DispatchEventAsync(store, eventName, payload)
│   ├── WebhookRetrier.cs              # + replay PayloadJson rows
│   └── InvoiceMonitor.cs              # DD sessions: register DdAddress on creation (no payout logic)
├── Auth/AuthChallengeStore.cs         # + Purpose, MerchantId binding for step-up
├── Endpoints/
│   ├── AuthEndpoints.cs               # step-up challenge/callback branch
│   ├── PayoutsEndpoints.cs            # new: C1/C2
│   ├── OperatorEndpoints.cs           # new: C3
│   ├── StoresEndpoints.cs             # DD-on requires payout address; delete guard
│   └── PaymentsEndpoints.cs           # DD session creation registers DdAddress
└── Program.cs                         # DI, hosted service, rate-limit groups, SQLite shims

src/DigiByte.Pay.Web/Components/Pages/
├── Merchant/DashboardSections.cs      # + "digidollar" section
├── Merchant/DigiDollarSection.razor   # new: balance, payout address, schedule, payouts list
├── Merchant/PayoutDetail.razor        # new: /merchant/payouts/{id}
├── Merchant/StepUpDialog.razor        # new: Digi-ID QR for re-sign-in
├── Dashboard.razor                    # banner, DD-toggle copy + guard, SessionRow currency/payout link
├── SessionDetail.razor                # paid-out status, "extra payment, refund the buyer" label
└── ApiDocs.razor, Docs.razor, Features.razor, Home.razor   # docs

src/DigiByte.NodeApi/
├── Endpoints/DigiDollarEndpoints.cs   # /send returns parsed txid; + GET /sends (wallet-scoped)
└── Middleware/WalletScopedRoutes.cs   # classify the new route

tests/DigiByte.Pay.Api.Tests/          # FakeDdNode + Payout*Tests, StepUpTests, LedgerScannerTests, OperatorTests
tests/DigiByte.Pay.Web.Tests/          # DigiDollarSectionTests
tests/DigiByte.NodeApi.Tests/          # DigiDollarSendLookupTests, route classification
```

**Structure decision**: the work stays inside the existing projects.
- Payout logic lives in Pay.Api services run by one new hosted service, alongside `InvoiceMonitor` and `WebhookRetrier`.
- The Dashboard gets one new routed section, following spec 032's section pattern.
- NodeApi gets one wallet-scoped route plus a parsed send response. The node is still reached only through NodeApi.

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|---|---|---|
| DigiPay keeps holding merchant DD (Principle I is silent on DigiPay custody) | DD can't be derived from a merchant key yet (spec Background), so DD payments have to land in DigiPay's node wallet. Payouts are how DigiPay hands it back. | Sending DD straight to the merchant needs key-derived DD addresses or merchant-held DD invoices, which don't exist yet and are out of scope (spec Assumptions). Doing nothing leaves merchant funds stuck. Follow-up R12: amend the constitution to describe DigiPay's custody of DD, with limits (hold briefly, keep it visible, cover it fully). |
| Own per-store ledger instead of reading on-chain balances | One shared node wallet means coin selection can spend any store's DD, so per-address balances stop meaning anything after the first payout. | Keeping one wallet per store would need a node wallet per merchant, adding operational load and multiplying fee reserves. |
