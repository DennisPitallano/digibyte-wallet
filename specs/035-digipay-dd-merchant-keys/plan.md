# Implementation Plan: DigiDollar on the merchant's own keys, without a node

**Branch**: `feature/digipay-dd-buyer-choice` (PR #131, reworked before merge) | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/035-digipay-dd-merchant-keys/spec.md`

## Summary

Today:

- DigiPay asks a DigiDollar node wallet for every DD address.
- It reads DD balances from that node.
- Spec 034 then pays the DD out to merchants.

This plan replaces that with the way DigiPay already takes DGB:

- **The store's key**: the store keeps the merchant's DigiDollar account public key, an `xpub`/`tpub` at `m/86'/coin'/0'`.
- **Addresses**: DigiPay derives `0/index` Taproot addresses from that key in `DigiByte.Crypto`. Every store with the same key draws from one shared index sequence.
- **Watching**: the existing `InvoiceMonitor` reads DD arrivals from the public Esplora explorer through the wallet's `EsploraDigiDollarApi`, one instance per network.
- **Removed**: `DigiDollarNodeClient` and all of spec 034 (ledger, payouts, step-up, operator routes, payout webhooks and UI) are deleted from PR #131 before it merges.

The wallet gets:

- a "DigiDollar key for DigiPay" screen, mirroring `/settings/xpub`
- a deeper DD scan of 500 addresses

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**:

- ASP.NET Core minimal APIs (DigiByte.Pay.Api)
- Blazor Server (DigiByte.Pay.Web)
- Blazor WebAssembly (DigiByte.Web)
- NBitcoin (`ExtPubKey`, Taproot), already used by DigiByte.Crypto
- EF Core (Npgsql, SQLite in tests)

**Storage**: PostgreSQL, through EF Core migrations, with the hand-written SQLite shims in `Program.cs` for tests.

**Testing**:

- xUnit for Crypto, Wallet and Pay.Api (WebApplicationFactory `PayFactory`)
- bUnit for Pay.Web and the wallet web pages
- Playwright E2E (`tests/DigiByte.Pay.E2E`) with a fake explorer in place of the fake node

**Target Platform**:

- Linux containers (Railway) for Pay.Api and Pay.Web
- browser WASM for the wallet

**Project Type**: web services plus the WASM wallet, in one monorepo.

**Performance Goals**:

- A DD payment shows as seen within 1 minute of the buyer sending it (SC-004).
- Payments paid during an explorer outage show as paid within 5 minutes of it coming back (SC-005).

**Constraints**:

- No DigiDollar node and no private keys on the server.
- DGB never depends on the explorer's DD health.
- Explorer requests stay within what the DGB monitor already makes (research R5).

**Scale/Scope**: a few hundred open DD payments at a time at most. The removal of spec 034 is about 40 files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Applies | Result |
|---|---|---|
| I. Self-custody | Yes | **Pass, and better.** DigiPay holds no DD and no keys. The wallet only exports a public key. Spec 034's draft custody amendment (`specs/034-digipay-dd-payouts/constitution-amendment.md`) was never applied and is now dropped. |
| II. In-browser cryptography | Yes | **Pass.** The account public key is derived in the browser from the seed already there. The server derives only public keys and addresses. |
| III. Test-first for Crypto & Wallet | Yes | **Required.** The new public-key derivation and DD-key parsing in `DigiByte.Crypto` need happy-path tests and negative-path tests (wrong network, private key, bad checksum, truncated, wrong depth). The tests must also show that public derivation equals seed derivation. The wallet's DD key export and deeper DD scan need `DigiByte.Wallet.Tests`. |
| IV. Boundary discipline | Yes | **Pass.** DD derivation from a public key lives in `DigiByte.Crypto`, the lowest owner. Pay.Api already references DigiByte.Wallet and DigiByte.Crypto, so reusing `EsploraDigiDollarApi` adds no new reference direction. The NodeApi dependency is removed. |
| V. Resilience | Yes | **Pass.** The spec defines the explorer-down surface (US4). Creating a DD address needs no outside service. DGB keeps working. Health is cached, and requests to an explorer that is down back off (R6). One explorer with no fallback is a clarified decision. |
| Secret hygiene | Yes | A pasted `xprv`/`tprv` is refused before it's stored, and never logged or echoed (R2). |
| Address handling | Yes | The derived first address is shown to the merchant for confirmation. The key's network is enforced strictly, unlike DGB's lenient xpub parse (R2). |
| Auth boundary | Yes | Unchanged. Setting the key is an owner-only `dgp_*`/session action and is recorded in the audit log. The anonymous `options/dd` endpoint stays as it is. |
| Docs in the same PR | Yes | Update `ARCHITECTURE.md`, `PROCESS_FLOWS.md`, `CHANGELOG.md`, the in-app docs (`Docs`, `ApiDocs`, `Features`, `Home`), the SDK and sample READMEs and the Postman collections. Also revert spec 034's custody wording in `SELF_CUSTODY.md`, `docs/SECURITY.md` and `docs/RAILWAY_NODE.md`, where present. |

No violations, so Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: still passes.

- The data model adds two tables (`DdKeySequences` and a slimmed `DdAddresses`) and removes six.
- The contracts remove more API than they add.
- No new service, project or reference direction is added.

## Project Structure

### Documentation (this feature)

```text
specs/035-digipay-dd-merchant-keys/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── stores-api.md        # DD key on stores, preview, errors; removed payout/operator routes
│   ├── sessions-and-webhooks.md
│   └── wallet-dd-key.md     # wallet screen and deeper DD scan
└── tasks.md                 # /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Crypto/
├── KeyGeneration/DigiDollarAccountKey.cs        # NEW: parse xpub/tpub (strict network), derive 0/i Taproot DD address
└── KeyGeneration/DigiDollarKeyDerivation.cs     # + GetAccountExtPubKey()

src/DigiByte.Wallet/
├── Services/WalletService.cs                    # + GetDigiDollarAccountKeyAsync()
└── Services/DigiDollarService.cs                # + deep scan (gap 500)

src/DigiByte.Web/Pages/
├── ExportDigiDollarKey.razor                    # NEW: /settings/digidollar-key
└── Recovery.razor                               # + "Look further for DigiDollar"

src/DigiByte.Pay.Api/
├── Data/PayStore.cs                             # + DdAccountKey, DdAccountKeySetAt; − DdPayout* fields
├── Data/DdKeySequence.cs                        # NEW: shared next index per key
├── Data/DdAddress.cs                            # reshaped: key, index, kind, store, session
├── Services/DdAddressAllocator.cs               # NEW: replaces DigiDollarNodeClient.GenerateAddressAsync
├── Services/DdChainProvider.cs                  # NEW: EsploraDigiDollarApi per network
├── Services/DdAvailability.cs                   # health from the explorer
├── Services/InvoiceMonitor.cs                   # DD received/confirmations from explorer history
├── Services/DigiDollarNodeClient.cs             # DELETED
├── Services/Payouts/*                           # DELETED (all of spec 034)
├── Endpoints/StoresEndpoints.cs                 # DD key set/preview/remove; DD on needs key
├── Endpoints/PayoutsEndpoints.cs                # DELETED
├── Endpoints/OperatorEndpoints.cs               # payout routes removed
└── Migrations/                                  # AddDdPayouts removed; new AddDdMerchantKeys

src/DigiByte.Pay.Web/
├── Pages/Merchant/Dashboard.razor               # DD key card with first-address check; payouts UI removed
├── Pages/Merchant/DigiDollarSettingText.cs      # "straight to your wallet"
├── Components/Shared/DdPayoutsPanel.razor       # DELETED
└── Components/Shared/PayoutAlertsBanner.razor   # DELETED

tests/
├── DigiByte.Crypto.Tests/DigiDollarAccountKeyTests.cs           # NEW
├── DigiByte.Wallet.Tests/DigiDollarKeyExportTests.cs, DigiDollarDeepScanTests.cs
├── DigiByte.Pay.Api.Tests/FakeDdExplorer.cs                     # replaces FakeDdNode
├── DigiByte.Pay.Api.Tests/DdKeyTests.cs, DdAddressSequenceTests.cs, InvoiceMonitorDdExplorerTests.cs
├── DigiByte.Pay.Api.Tests/Payout*.cs                            # DELETED
├── DigiByte.Pay.Web.Tests/DdKeyCardTests.cs; DdPayoutsPanelTests.cs DELETED
└── DigiByte.Pay.E2E/scripts/fake-esplora.mjs                    # replaces fake-nodeapi.mjs
```

**Structure Decision**: no new projects. Crypto owns derivation. Wallet owns the explorer client and the wallet screens. Pay.Api owns allocation and watching. The work lands on PR #131's branch, so spec 034's code is removed before merge, not merged and then deleted.

## Complexity Tracking

None.
