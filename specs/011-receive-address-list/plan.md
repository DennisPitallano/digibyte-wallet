# Implementation Plan: Your Addresses on the Receive Page

**Branch**: `feature/receive-address-list` | **Date**: 2026-11-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-receive-address-list/spec.md`

## Summary

Add a "Your addresses (N)" row to the Receive page for HD wallets. It starts folded on every visit; tapping it opens the newest 10 receiving addresses (more on request), each with its name and whether it is used, unused or "Status unknown". Choosing one puts it in the QR code, copy, share and download; a short privacy notice appears under the QR code when the address on show is used; naming reuses the Activity labels; a used address can open Activity already filtered to it. The wallet already derives address ranges (`GetReceivingAddressesAsync`) and the page already has everything else, so almost all of the work is in the page plus one small pure helper in `DigiByte.Wallet` that turns the address list, the transaction history and the labels into list entries. No change to how addresses are generated, no backend, no new network call: used/unused comes from the history Home already caches.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: existing `WalletService`, `AddressLabelService`, `ISecureStorage`, `ActivityAddressIndex` and `AddressKey` from spec 010; Tailwind classes already in the project

**Storage**: Nothing new is stored. It reads Home's cached history (`cache:transactions:<walletId>`) and the label store, and writes one existing preference (`pref:activityAddress:<walletId>`) to open Activity filtered.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` (constitution principle III); manual check in the preview for the page

**Target Platform**: Browser (WebAssembly), phone and desktop widths

**Project Type**: Web application (monorepo); touches `DigiByte.Wallet` (one helper) and `DigiByte.Web` (Receive page) only

**Performance Goals**: SC-004: with 50 generated addresses Receive is ready in under 3 seconds on a typical phone. Only the newest 10 are derived at first, 10 more per "Show more".

**Constraints**: No new network request; works offline (entries show a muted "Status unknown" label); the existing limit and save-failure messages stay unchanged; the page opens on the newest address with the list folded, and neither switching nor opening the list is saved.

**Scale/Scope**: One page, one helper, one test file

**Notes**: A locked wallet never reaches the page (its auth guard sends the user to unlock), so nothing is listed. Watch-only (xpub) wallets use the HD path on Receive today; task T028 checks that the list works for them.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Self-Custody | Pass. Only public addresses and names are shown. No key or seed handling. |
| II. In-Browser Cryptography | Pass. Derivation stays in the browser through the existing call. No server involved. |
| III. Test-First for wallet logic | Applies to the new helper in `DigiByte.Wallet`. Its tests are written first and cover empty history, unknown status, duplicates and encoding variants. A test also pins the rule that the list's newest address equals the one Receive shows today and that generating a new address adds exactly one. |
| IV. Boundary Discipline | Pass. The helper sits in `DigiByte.Wallet`; the page only presents. No new project reference. |
| V. Resilience | Pass. No new dependency. With no history the list still shows with status "unknown"; "unknown" is never shown as "unused". |
| Security: address handling | Pass. Addresses come from the wallet's own derivation, not user input. |
| Security: CSP / secrets | Pass. Nothing new loaded; nothing logged. |
| Docs | A short note goes in `docs/` only if the Receive screen is described there (checked in tasks). |

Result: no violations; Complexity Tracking not needed.

**Post-design re-check**: unchanged. The design adds one pure helper and page code only.

## Project Structure

### Documentation (this feature)

```text
specs/011-receive-address-list/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── receive-address-list-ui.md
└── tasks.md             # created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DigiByte.Wallet/
│   └── Services/ReceiveAddressList.cs          # NEW: entries, status, windowing (pure, testable)
└── DigiByte.Web/
    └── Pages/Receive.razor                     # list, switching, naming, view activity

tests/DigiByte.Wallet.Tests/
└── ReceiveAddressListTests.cs                  # NEW
```

**Structure Decision**: Keep the existing layering. Deciding what the list shows (order, window, status, names) goes in `DigiByte.Wallet` so it is unit tested without the browser; `DigiByte.Web` only renders it and handles taps. `WalletService` and the crypto project are not changed.
