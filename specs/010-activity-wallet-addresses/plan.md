# Implementation Plan: Wallet Addresses in Activity

**Branch**: `feature/activity-wallet-addresses` | **Date**: 2026-11-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-activity-wallet-addresses/spec.md`

## Summary

Show, on every activity row, which of the user's own addresses a transaction used, and let the user filter the list by one of those addresses. Today `TransactionRecord` only carries the counterparty address, so the work is mostly data: the wallet already knows which addresses are its own while it builds history from the explorer, so it can record them on each transaction at the same time (no extra network calls). A small pure helper in `DigiByte.Wallet` builds the "used addresses" list (with counts) and applies the address filter; the Blazor activity page, the shared row and the detail dialog consume it.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: NBitcoin (address derivation, already used), existing `AddressLabelService`, Tailwind CSS classes already in the project

**Storage**: Transactions are already persisted as JSON by `TransactionTracker` and cached by Home. New fields are optional so old stored data still loads. No new store.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` (constitution principle III)

**Target Platform**: Browser (WebAssembly), mobile and desktop widths

**Project Type**: Web application (monorepo; this feature touches `DigiByte.Wallet` and `DigiByte.Web` only)

**Performance Goals**: SC-004: 500 transactions / 100 addresses lists and filters in under 1 second. Index and filter are linear passes over in-memory data.

**Constraints**: No new network calls and no new backend. Works when the explorer is unreachable (shows the addresses it already knows, omits the rest). Existing history behavior must not regress.

**Scale/Scope**: One list page, one row component, one detail dialog, one model change, one helper

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Self-Custody | Pass. Only public addresses are handled. No key or seed access added. |
| II. In-Browser Cryptography | Pass. All work is client-side. No server added or changed. |
| III. Test-First for wallet logic | Applies (changes in `DigiByte.Wallet`). Tests for the helper and for history enrichment are written first. Negative paths: unknown addresses, missing data, address-format variants. |
| IV. Boundary Discipline | Pass. Logic sits in `DigiByte.Wallet`; `DigiByte.Web` only presents it. No new project references. |
| V. Resilience | Pass. No new dependency. When explorer data is missing, rows just omit the wallet address (FR spec edge case). |
| Security: address handling | Pass. Addresses shown come from the wallet's own derivation and explorer results, not user input. |
| Security: CSP / secrets | Pass. Nothing new loaded; nothing logged. |
| Docs | A short note goes in `docs/` if the activity screen is described there (checked in tasks). |

Result: no violations; Complexity Tracking not needed.

**Post-design re-check**: unchanged. The design adds optional fields and a pure helper only.

## Project Structure

### Documentation (this feature)

```text
specs/010-activity-wallet-addresses/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── activity-addresses-ui.md
└── tasks.md             # created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DigiByte.Wallet/
│   ├── Models/WalletInfo.cs                    # TransactionRecord: new optional address fields
│   └── Services/
│       ├── WalletService.cs                    # history build fills own addresses; change-address set
│       └── ActivityAddressIndex.cs             # NEW: used-address list + filter (pure, testable)
└── DigiByte.Web/
    ├── Pages/Transactions.razor                # address picker + filter + search match
    ├── Pages/Home.razor                        # passes labels to the shared row (no filter here)
    └── Components/Shared/
        ├── TransactionRow.razor                # shows wallet address / label / "+N more"
        └── TransactionDetailModal.razor        # lists all wallet addresses, copyable

tests/DigiByte.Wallet.Tests/
├── ActivityAddressIndexTests.cs                # NEW
└── TransactionHistoryAddressTests.cs           # NEW (history enrichment)
```

**Structure Decision**: Keep the existing layering. Data derivation and the filter rules go in `DigiByte.Wallet` so they can be unit tested without the browser; `DigiByte.Web` only renders. The Home page reuses `TransactionRow`, so it picks up the row change for free.
