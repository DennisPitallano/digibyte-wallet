# Implementation Plan: Baku Mascot — Guide & Guardian

**Branch**: `002-baku-mascot-guide` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-baku-mascot-guide/spec.md`

## Summary

Introduce "Baku", the wallet's dragon mascot, as an in-app guide (onboarding, tips, empty/error/success states) and guardian (warnings before seed reveal, risky DGB/DigiDollar sends, P2P trades). The approach is entirely client-side and UI-layer: a static pose asset set cut from the three supplied green-screen sheets, a predefined English message catalog, a small `BakuService` that owns the presence preference, dismissed-tip state and "one message at a time" arbitration, a reusable `BakuMessage` Blazor component, and a pure, unit-tested `SendRiskEvaluator` that decides when a send warrants a guardian warning. No backend, no new network calls, no new third-party dependencies.

## Technical Context

**Language/Version**: C# / .NET 10 (Blazor WebAssembly), Tailwind CSS

**Primary Dependencies**: Existing only (Blazor WASM, Tailwind build in `DigiByte.Web`). Asset preparation uses a one-off local script (Python/Pillow) that is not shipped or referenced by the build.

**Storage**: Browser `localStorage` for Baku preferences and dismissed tips, following the existing `AppState` pattern (`dgb-*` keys). No IndexedDB, no secrets.

**Testing**: xUnit (existing `tests/DigiByte.Wallet.Tests`). Pure decision logic placed in `DigiByte.Wallet` with unit tests; `BakuService` rules (priority, mode filtering, dismissal, persistence) tested in the same project, which already references `DigiByte.Web`; remaining UI verified manually via quickstart.

**Target Platform**: Installable PWA in modern mobile and desktop browsers, offline-capable.

**Project Type**: Web application (Blazor WASM PWA) within a layered monorepo.

**Performance Goals**: Mascot assets add no more than 5% to first-load time (SC-005); pose images lazy-loaded except the one on the current screen; each pose image 15 KB or smaller in WebP/PNG at display size.

**Constraints**: Strict CSP, so all assets are self-hosted and no inline/remote images. English-only messages (clarified). Motion limited to a subtle entrance transition and a short celebration effect, CSS only, disabled under reduced-motion (FR-013). Must not obscure balances, addresses or actions. Works offline (assets precached by the service worker).

**Scale/Scope**: About 20 poses, about 25 predefined messages, about 10 screens touched, 1 new service, 1 new component, 1 small pure logic class.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Self-Custody | PASS. Baku never reads, shows or stores seed, password or keys. Preferences hold only a presence level and tip IDs (FR-014). |
| II. In-Browser Cryptography | PASS. No cryptographic or signing changes. |
| III. Test-First for Wallet Logic | PASS. The only wallet-layer addition (`SendRiskEvaluator`) is pure logic and gets unit tests, including negative paths, written first. |
| IV. Boundary Discipline | PASS. Risk rule lives in `DigiByte.Wallet` (the lowest layer owning "address/amount risk"). Presentation (service, component, assets) lives in `DigiByte.Web`. No new reference direction. |
| V. Resilience | PASS. No backend dependency. Image load failure falls back to text (FR-011); core DGB flows are never blocked by Baku. |
| Security: CSP | PASS. Assets self-hosted under `wwwroot/images/mascot`; CSP unchanged (`img-src 'self'`). |
| Security: Address handling | PASS. Evaluator receives already-validated addresses and never replaces validation. |

No violations; Complexity Tracking not needed.

**Post-design re-check**: unchanged, PASS.

## Project Structure

### Documentation (this feature)

```text
specs/002-baku-mascot-guide/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contracts.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DigiByte.Wallet/
│   └── Services/
│       ├── SendRiskEvaluator.cs          # pure: new-address + >=50% balance rules
│       └── KnownAddressProvider.cs       # saved contacts + previously-sent addresses
├── DigiByte.Web/
│   ├── Components/Shared/
│   │   ├── BakuMessage.razor             # pose + message card/inline/modal variants
│   │   └── BakuWarningDialog.razor       # confirm-to-proceed guardian dialog
│   ├── Services/
│   │   ├── BakuService.cs                # preference, dismissed tips, arbitration
│   │   └── BakuCatalog.cs                # poses + predefined messages (English)
│   ├── Pages/                            # touched: Welcome, CreateWallet, RecoverWallet,
│   │                                     #   BackupSeed, Unlock, Home, Send, Receive,
│   │                                     #   DigiDollar, DigiDollarSend, P2PMarketplace,
│   │                                     #   Settings, Help
│   └── wwwroot/images/mascot/
│       ├── poses/*.webp                  # cut-out transparent poses (generated)
│       └── (original sheets remain as source art)
tests/
└── DigiByte.Wallet.Tests/
    ├── SendRiskEvaluatorTests.cs
    ├── KnownAddressProviderTests.cs
    └── BakuServiceTests.cs
```

**Structure Decision**: Extend the existing layered projects. Presentation in `DigiByte.Web` following the `Components/Shared` and `Services` conventions (scoped service registered in `Program.cs`, `dgb-*` localStorage keys like `AppState`). The one piece of business logic goes into `DigiByte.Wallet` so it is unit-testable under Principle III.

## Complexity Tracking

No constitution violations to justify.
