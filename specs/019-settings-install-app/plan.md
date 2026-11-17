# Implementation Plan: Install App from Settings

**Branch**: `019-settings-install-app` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/019-settings-install-app/spec.md`

## Summary

Add an "Install app" row to the Settings page, shown only while the wallet is not running as an installed app and installation is possible or guidable (Android, iOS, installable desktop browsers). Android and desktop use the browser's native install prompt; iOS, Android without a prompt, and desktop after its prompt was used or dismissed show short instruction sheets. The row is independent of the top banner's dismiss state.

Approach: extend the existing `wwwroot/js/pwa-install.js` so it supports several subscribers (today `registerCallback` holds a single callback, so a second consumer would silently displace the banner) and exposes a one-shot `getState()` snapshot. Add a small Blazor row and instruction dialog to `Pages/Settings.razor`. The banner component is not changed.

## Technical Context

**Language/Version**: C# / .NET 10 (Blazor WebAssembly), plain JavaScript for browser interop

**Primary Dependencies**: Existing `IJSRuntime`, Tailwind utility classes already used by Settings; no new packages

**Storage**: None (install state is read from the browser; nothing persisted)

**Testing**: No Blazor component test project exists for `DigiByte.Web`; verification is the manual device matrix in [quickstart.md](quickstart.md) (Android, iOS, desktop). Principle III is not triggered (no change to `DigiByte.Crypto` / `DigiByte.Wallet`).

**Target Platform**: Installable PWA in Android Chrome/Samsung Internet, iOS Safari (and other iOS browsers), desktop Chrome/Edge

**Project Type**: Web application (static Blazor WASM PWA)

**Performance Goals**: Row state resolved on first render of Settings with no visible layout shift beyond the row appearing; no network use

**Constraints**: Works offline; strict CSP unchanged (no external assets); no data collected or transmitted; banner behaviour unchanged

**Scale/Scope**: One Settings row, one dialog, ~60 lines of JS change, ~100 lines of Razor

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Status | Note |
|---|---|---|
| I. Self-custody | Pass | Touches no keys, seed or password |
| II. In-browser cryptography | Pass | No server, no crypto |
| III. Test-first for crypto & wallet | N/A | No change under `DigiByte.Crypto/` or `DigiByte.Wallet/` |
| IV. Boundary discipline | Pass | Change confined to `DigiByte.Web` |
| V. Resilience | Pass | Fully client-side and offline-safe; no backend dependency |
| CSP / self-hosted assets | Pass | Inline SVG icons only; no new origins |
| Secret hygiene / auth | N/A | No secrets, no calls |
| Conventional Commits, build/test gates | Applies at PR time | `feat(settings): ...` |
| Docs | Pass | Task T021 checks `README.md` and `docs/` for PWA install mentions and updates them |
| Branching | Pass | Work on `feature/settings-install-app` (task T001) |

Post-design re-check: still passes; the JS change adds no network or storage surface.

## Project Structure

### Documentation (this feature)

```text
specs/019-settings-install-app/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-and-interop.md
└── tasks.md             # Created later by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Web/
├── Pages/
│   └── Settings.razor              # new "Install app" row + instructions dialog, in a new "App" group or at the top of "Display"
├── wwwroot/js/
│   └── pwa-install.js              # multi-subscriber support + getState(); existing API stays compatible
└── Components/
    └── PwaInstallBanner.razor      # unchanged (verified, not edited)
```

**Structure Decision**: Single-project change inside `DigiByte.Web`. No new component file: the row and dialog are a small block in `Settings.razor`, matching how that page already hosts its own dialogs (for example the language toast).

## Complexity Tracking

No constitution violations; nothing to justify.
