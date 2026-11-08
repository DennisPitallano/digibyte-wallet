# Implementation Plan: Digi-ID Address Funds Safety

**Branch**: `feature/digiid-funds-safety` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-digiid-funds-safety/spec.md`

## Summary

Two parts, both client-side in the existing Blazor WebAssembly wallet:

1. **Warn.** One shared warning component, placed beside every Digi-ID address (Identity pending-approval panel, Identity success panel, login detail modal, Help entry). It states the three facts from FR-002 and links to a new "Recover funds" section on the Identity page.
2. **Recover.** A "Recover funds" section on the Identity page. For an unlocked HD wallet it:
   - derives each Digi-ID address from the domain (login history plus hand-typed domains),
   - looks each address up in its own request (`GetVerifiedUtxosAsync`, which throws on failure instead of returning "empty"),
   - lists what it found, and
   - sweeps each address in its own transaction to a different fresh receive address, behind one confirmation.

   Sent sweeps are saved (no keys) and their status is shown until they confirm or fail.

The existing recovery sweeps (`SweepExternalFundsAsync` and friends) cannot be reused as they are. They swallow lookup errors, merge all inputs into one transaction, and pay one fixed address. All three break FR-007, FR-004b and FR-008a. The new code reuses the lower-level pieces only: `DigiByteTransactionBuilder.BuildSendAllTransaction`, `HdKeyDerivation.DeriveDigiIdKey`, `TransactionTracker`, and the fee floor.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`), Blazor WebAssembly, Razor components

**Primary Dependencies**: NBitcoin (existing), the existing `IBlockchainService` stack (`FallbackBlockchainService` over Esplora/Blockbook/NodeApi), `ISecureStorage` (encrypted IndexedDB), `LocalizationService`

**Storage**: Sweep records in `ISecureStorage` under `digiid_sweeps_{walletId}`. The login history stays where it is (`digiid_login_history`, written by `Identity.razor`). No new backend storage.

**Testing**: xUnit 2.9.3 in `tests/DigiByte.Crypto.Tests` and `tests/DigiByte.Wallet.Tests` (existing projects, existing fakes such as `MockBlockchainService`)

**Target Platform**: Browser (PWA), mainnet. The same code path serves other networks through `CurrentNetwork`.

**Project Type**: Web application (static Blazor WASM PWA plus independent services). Only the wallet projects change.

**Performance Goals**: SC-002, 50 sites checked in under 30 s. Each address is a separate request, run with a small concurrency cap (4). SC-003, sweep done in under 2 minutes excluding confirmation.

**Constraints**:
- Private keys must never be stored, logged or shown (FR-015, Principle I).
- No batched lookups of several Digi-ID addresses (FR-004b).
- One destination per swept address, and at most 19 swept addresses per confirmation, and the Receive page must end on a fresh unused address (see research R-4).
- Minimum relay fee floor of 100 sat/vB already enforced elsewhere in `WalletService`.
- No CSP change and no new third-party host.

**Scale/Scope**: up to 50 login-history sites, 1 UI section, 1 shared warning component, 4 warning placements, 2 new service classes, 1 new crypto helper, 2 locale files, 1 Help entry

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / rule | Assessment | Result |
|---|---|---|
| I. Self-custody | Keys are derived in memory from the unlocked session and used only to sign. Records contain no key, seed or PIN. Nothing new leaves the device except the raw signed transaction, as in any send. | PASS |
| II. In-browser crypto | Derivation and signing run in WASM through `DigiByte.Crypto`/NBitcoin. No server is added to any signing flow. | PASS |
| III. Test-first for crypto and wallet logic | Tests land in `DigiByte.Crypto.Tests` (domain normalisation) and `DigiByte.Wallet.Tests` (scan, plan, sweep, store, locked/watch-only/WIF refusals), including negative paths. See tasks. | PASS (tests required in the same PR) |
| IV. Boundary discipline | Normalisation sits in `DigiByte.Crypto` next to `DigiIdService`. Scan/plan/sweep/store sit in `DigiByte.Wallet`. UI stays in `DigiByte.Web`. No new reference direction. | PASS |
| V. Resilience | No new backend. Lookup failure shows "could not check" and a retry. Core flows (balance, receive, send, history) never wait on this feature. | PASS |
| CSP / third-party hosts | Unchanged. | PASS |
| Secret hygiene | Sweep record schema has no secrets, and logging avoids raw transactions. | PASS |
| Address handling | Entered domains are validated and normalised. Destinations are the wallet's own derived addresses, so they need no user-entered-address check. | PASS |
| Documentation | `docs/PROCESS_FLOWS.md`, `CHANGELOG.md` and the Help entry are updated in the same PR. | PASS (tasks) |

No violations, so no Complexity Tracking section.

**Post-design re-check**: unchanged. The design adds no dependency direction, no new server role and no new stored secret.

## Project Structure

### Documentation (this feature)

```text
specs/009-digiid-funds-safety/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── wallet-service.md
│   └── ui.md
├── checklists/requirements.md
└── tasks.md              # created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DigiByte.Crypto/
│   └── DigiId/DigiIdService.cs            # + DigiIdDomain.Normalize (shared by login and manual entry)
├── DigiByte.Wallet/
│   ├── Models/DigiIdFundsModels.cs        # DigiIdFundFinding, DigiIdSweepPlan(Item), DigiIdSweepRecord, status enum
│   ├── Services/
│   │   ├── DigiIdSweepPlanner.cs          # pure: fee, dust, net, destination assignment, per-round cap
│   │   ├── DigiIdSweepStore.cs            # persisted sweep records (ISecureStorage), state transitions
│   │   └── WalletService.cs               # + ScanDigiIdFundsAsync, PlanDigiIdSweepAsync, ExecuteDigiIdSweepAsync,
│   │                                      #   RefreshDigiIdSweepsAsync (key handling stays inside this class)
└── DigiByte.Web/
    ├── Components/Shared/
    │   ├── DigiIdAddressWarning.razor     # shared warning + link
    │   ├── DigiIdRecoverFunds.razor       # check, results, review, sweep, status
    │   └── DigiIdLoginDetailModal.razor   # + warning
    ├── Pages/Identity.razor               # + warning x2, + <DigiIdRecoverFunds id="recover-funds">
    ├── Pages/Help.razor                   # + entry
    ├── wwwroot/locales/{en,es}.json       # + identity.addrWarning.*, identity.recover.*
    └── Program.cs                         # + AddScoped<DigiIdSweepStore>() (WalletService takes it as an optional ctor parameter)

tests/
├── DigiByte.Crypto.Tests/DigiIdDomainTests.cs
└── DigiByte.Wallet.Tests/
    ├── DigiIdSweepPlannerTests.cs
    ├── DigiIdSweepStoreTests.cs
    └── DigiIdFundsRecoveryTests.cs        # WalletService scan/plan/execute with a fake IBlockchainService
docs/PROCESS_FLOWS.md, CHANGELOG.md
```

**Structure Decision**: Keep key-touching code inside `WalletService`, which already owns the unlocked session, so no private key crosses a class boundary. Put everything that has no key (fee math, destination assignment, record state machine) in two small classes that are easy to test.

## Phase Summary

- **Phase 0**: [research.md](research.md) records the decisions and the traps found in the existing code.
- **Phase 1**: [data-model.md](data-model.md), [contracts/](contracts/) and [quickstart.md](quickstart.md).
- **Phase 2**: `/speckit-tasks` (not part of this command).
