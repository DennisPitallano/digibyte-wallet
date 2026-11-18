# Implementation Plan: Pull-Down Refresh on Multi-Sig Wallet Dashboard

**Branch**: `021-multisig-pull-refresh` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

## Summary

Wrap the single multi-sig wallet page (`/multisig/{WalletId}`) in the existing `PullToRefresh` component, wired to a refresh routine that reloads balance and recent activity (the same data as the page's refresh button). Unlike the button, the pull routine reports whether each load succeeded: on failure it keeps the last known balance and activity and shows an error toast ("Couldn't refresh"). Concurrent pulls and button taps are ignored while a refresh runs. No new data, services, or dependencies.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (Razor components)

**Primary Dependencies**: existing `PullToRefresh` component, `NotificationService` (toasts), `MultisigWalletService`, Tailwind (already in the project)

**Storage**: N/A

**Testing**: xUnit (`tests/DigiByte.Wallet.Tests`). The repo has no component-test framework (bUnit), so the gesture and UI behaviour are verified manually per the quickstart

**Target Platform**: PWA in mobile browsers and installed app; touch devices

**Project Type**: web application (static Blazor WASM PWA)

**Performance Goals**: the pull indicator follows the finger without visible lag; a refresh takes as long as the existing button refresh

**Constraints**: no new third-party assets (CSP); must not regress cached/offline behaviour (Principle V); the gesture only arms at the top of the scroll

**Scale/Scope**: one page and one existing shared component; small page-local change

## Constitution Check

| Principle | Result |
|---|---|
| I. Self-custody | Pass: no key or seed handling |
| II. In-browser crypto | Pass: no signing or server role change |
| III. Test-first (Crypto/Wallet) | Pass / N/A: no change to `DigiByte.Crypto` or `DigiByte.Wallet`. If any logic moves into `DigiByte.Wallet`, tests are added in the same PR |
| IV. Boundary discipline | Pass: change is confined to `DigiByte.Web` |
| V. Resilience | Pass: on failure, last known data stays visible with a clear message (spec FR-007) |
| CSP / secrets / auth | Pass: unaffected |
| Workflow | Spec and clarify done; Conventional Commit `feat(multisig): ...`; no `docs/` change needed (no new user-facing concept beyond a standard gesture) |

No violations, so Complexity Tracking is not needed. Re-checked after design: still passes.

## Project Structure

### Documentation (this feature)

```text
specs/021-multisig-pull-refresh/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/requirements.md
└── tasks.md          # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Web/
├── Pages/MultisigDetail.razor             # wrap page body in <PullToRefresh>; add pull-refresh routine + failure toast
└── Components/Shared/PullToRefresh.razor  # reused as-is (touched only if research item R3 requires it)

src/DigiByte.Wallet/Services/MultisigWalletService.cs  # read in T003; changed only if offline activity does not signal failure
tests/DigiByte.Wallet.Tests/                           # a unit test is added only if the Wallet service changes (Principle III)
```

**Structure Decision**: Page-local change in the existing web project, reusing the shared component so the gesture matches the Activity page (FR-008).

## Complexity Tracking

None.
