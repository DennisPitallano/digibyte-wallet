# Implementation Plan: Multisig Management Revamp

**Branch**: `016-multisig-management-revamp` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/016-multisig-management-revamp/spec.md`

## Summary

Fix three multisig defects (wallet scoping, rename, truncated co-signer keys) and revamp the multisig UX: a compact dashboard, a guided Send flow with paste/scan/Max/review, a unified Activity/history view, and a richer Pending queue. The work is almost entirely client-side: a persistence/model extension in `DigiByte.Wallet` (owner wallet id on configs, drafts, pending items; rename; backup stamp; activity merge; validation helpers) with unit tests first, and Blazor page/component work in `DigiByte.Web`. No backend, key-handling, or CSP change is required.

## Technical Context

**Language/Version**: C# / .NET (matches existing solution), Blazor WebAssembly PWA, Tailwind utility classes already in use

**Primary Dependencies**: NBitcoin (via `DigiByte.Crypto`), existing `ISecureStorage`, `IBlockchainService` (already provides `GetAddressTransactionsAsync(address, skip, take)`, `GetUtxosAsync`, `GetBalanceAsync`, `GetFeeRateAsync`, `BroadcastTransactionAsync`), existing `QrScannerModal`, `ClipboardService`, `PaymentRequest.FromUri`, `AddressValidator`, contacts/address-book page

**Storage**: Existing `ISecureStorage` keys (`multisig_config_*`, `multisig_pending_*`, `multisig_wallet_list`, `multisig_drafts`). Extended with additive, backward-compatible JSON fields; no new store

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` (existing `MultisigWalletServiceTests`, `MultisigModelsTests`); manual UI verification per `quickstart.md`

**Target Platform**: Mobile-first PWA in the browser; light and dark themes; 320–768 px widths

**Project Type**: Web application (client-only change in `DigiByte.Wallet` + `DigiByte.Web`)

**Performance Goals**: Dashboard first paint of name/address/actions without waiting on the network; Activity first 25 entries shown as soon as the first page returns; no UI freeze on "Load more"

**Constraints**: Offline-capable degraded views (Principle V); no secrets in UI/logs (Principle I/II); additive storage migration with no data loss; all addresses validated client-side with network rules

**Scale/Scope**: Handful of multisigs per wallet, N up to the existing supported maximum, histories of hundreds of entries; ~7 pages/components touched or added

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / Rule | Assessment |
|---|---|
| I. Self-Custody | Pass. Only public data (keys, addresses, redeem scripts, PSBTs) is displayed, copied, or shared; no key/seed material touched or transmitted. |
| II. In-Browser Cryptography | Pass. Signing/finalising stays in the browser. No server participates. |
| III. Test-First (non-negotiable) | Pass with obligation. Scoping/migration, rename validation, address and amount validation (wrong network, bad checksum, over-balance, dust), PSBT-import checks, Max/fee math, and activity merge/dedupe get failing tests first in `DigiByte.Wallet.Tests`. |
| IV. Boundary Discipline | Pass. Logic lives in `DigiByte.Wallet` (owner of multisig concepts); `DigiByte.Web` only renders. No new project references. |
| V. Resilience | Pass. Activity and dashboard define loading/offline/error states; local proposals still show offline; core flows are not blocked on new dependencies. |
| Address handling | Pass. Destination validated with `AddressValidator` plus network check before use. |
| CSP / third-party assets | Pass. No new external assets; QR rendering uses existing self-hosted code. |
| Workflow gates | Spec exists and is clarified. Conventional Commits; `docs/PROCESS_FLOWS.md`/Help multisig guide updated in the same PR. |

No violations; Complexity Tracking not required. Post-design re-check: unchanged (design adds no dependencies, services, or projects).

## Project Structure

### Documentation (this feature)

```text
specs/016-multisig-management-revamp/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── multisig-service-and-ui-contracts.md
└── tasks.md              # created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/MultisigModels.cs               # + OwnerWalletIds, Name on config, BackupAt, memo/fee/UnsignedTxId fields, ActivityEntry types (see Models/MultisigActivity.cs)
├── Services/MultisigWalletService.cs      # scoped list/get/drafts, rename, claim, backup stamp, cancel, validated import, activity merge
├── Services/MultisigActivityBuilder.cs    # NEW: pure merge/dedupe/group/filter of chain txs + proposals
├── Services/MultisigSendHelper.cs         # NEW: destination validation, fee/Max estimate, scan classification, dust constant
├── Services/MultisigKeyFormat.cs          # NEW: readable grouping of public keys
└── Models/MultisigActivity.cs             # NEW: ActivityEntry, kinds, direction

src/DigiByte.Web/
├── Pages/MultisigWallets.razor            # scoped list, rename, unassigned prompt
├── Pages/MultisigDetail.razor             # compact dashboard, editable name, expandable co-signers
├── Pages/MultisigSend.razor               # paste/scan/Max/fee/review
├── Pages/MultisigPending.razor            # queue with signer checklist and contextual actions
├── Pages/MultisigActivity.razor           # NEW: route /multisig/{WalletId}/activity
├── Pages/MultisigCreate.razor / MultisigImport.razor   # associate with active wallet
├── Services/ActiveWalletContext.cs        # NEW: active wallet id + public key for multisig pages
└── Components/Multisig/                   # NEW: MultisigHeader, CoSignerRow, KeyView (full key + QR), BackupCard,
                                           #      ActivityList/ActivityItem, SignerProgress, RenameDialog, ReceiveSheet, RecipientInput

tests/DigiByte.Wallet.Tests/
├── MultisigScopingTests.cs                # NEW
├── MultisigRenameTests.cs                 # NEW
├── MultisigSendValidationTests.cs         # NEW
├── MultisigPendingImportTests.cs          # NEW
├── MultisigActivityBuilderTests.cs        # NEW
└── KeyFormatTests.cs                      # NEW
```

**Structure Decision**: Reuse the existing layered layout. Domain and persistence changes go in `DigiByte.Wallet` with tests in `DigiByte.Wallet.Tests`; presentation goes in `DigiByte.Web` using a new `Components/Multisig/` folder to keep the pages compact. A new pure `MultisigActivityBuilder` keeps merge/filter logic testable without storage or network.
