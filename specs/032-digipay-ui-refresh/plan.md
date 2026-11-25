# Implementation Plan: DigiPay Website and Merchant Dashboard Follow the Wallet's Look and Feel

**Branch**: `feature/digipay-ui-refresh` (planned) | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/032-digipay-ui-refresh/spec.md`

## Summary

DigiPay (`src/DigiByte.Pay.Web`) adopts the wallet's style layer, fonts, theme choices, feedback patterns and menu. The wallet's generic style layer is the part of `src/DigiByte.Web/wwwroot/css/app.css` from the `--ui-*` tokens through the bottom menu, plus skeleton, reduced-motion and Baku styles and the two self-hosted fonts.

That layer moves into a new, dependency-free Razor Class Library, `src/DigiByte.UI`. Both apps reference it and get its files as static web assets at build time. This gives one style source (clarification 3) without DigiPay ever loading anything from the wallet's site (Principle IV).

Changes to DigiPay itself:

- The theme switches to the wallet's `.dark` class mechanism with three modes.
- Small DigiPay components wrap the shared classes: notice, copy button, status badge, confirm dialog, empty state, skeleton, rail and bottom menu.
- The eleven dashboard tabs become routed pages under `/merchant/{section}`.
- The checkout gains copy buttons, a status announcer, a reconnecting state and an optional `theme` query parameter that the embed script can set.

There are no payment, API or authentication changes.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`net10.0`); plain CSS; small vanilla JS boot scripts.

**Primary Dependencies**:
- Blazor Web App, InteractiveServer, for DigiPay.
- Blazor WebAssembly for the wallet.
- Tailwind CSS 3.4.19, used only for layout utilities.
- New `DigiByte.UI` Razor Class Library, holding static assets only.
- Existing: QRCoder, Blazor-ApexCharts, SignalR client.
- New test-only: bUnit and Playwright with axe-core (see research R7).

**Storage**: Browser `localStorage` only, for the theme (`dp-theme`) and the POS theme (`digipay.pos.theme:v1`). No server storage changes.

**Testing**:
- New `tests/DigiByte.Pay.Web.Tests` (xUnit + bUnit) for components, route mapping, theme precedence and status mapping.
- `node --test` for the theme boot script, matching the wallet's `tests/js` pattern.
- A Playwright + axe accessibility and timing pass, run from `quickstart.md`.

**Target Platform**:
- DigiPay: ASP.NET Core 10 on Railway (Docker image with no Node).
- Wallet: static WASM PWA behind Nginx.
- Evergreen browsers, 320px and wider.

**Project Type**: Web application, with two front ends that share a style library.

**Performance Goals**: Checkout amount and QR code usable in ≤ 2 s on the Fast 4G profile defined in FR-013a, with the median no slower than the pre-restyle baseline (FR-013a, SC-011). The added weight is Inter at 48 KB, JetBrains Mono at 40 KB (both woff2) and the shared CSS at about 30 KB unminified.

**Constraints**:
- The dashboard's wide content area is capped at 1280px (`max-w-7xl`); narrow pages use the wallet's 480px column.
- No relaxation of the wallet's CSP: the shared assets are same-origin `_content/DigiByte.UI/*`.
- The DigiPay Docker build has no Node, so compiled `tailwind.css` stays committed.
- `font-display: swap` is required.
- The wallet must render pixel-identically after the extraction.
- Existing URLs must keep working (FR-019).

**Scale/Scope**:
- 19 DigiPay routes, plus 10 new dashboard section routes.
- About 25 Razor files touched in DigiPay.
- The 1,785-line `Dashboard.razor` splits into about 12 files.
- One new library and one new test project.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / rule | Applies? | Assessment |
|---|---|---|
| I. Self-custody | No | No key, seed or password handling changes. DigiPay never holds shopper keys. |
| II. In-browser cryptography | No | No signing or crypto paths touched. Digi-ID sign-in stays as is. |
| III. Test-first for Crypto & Wallet | Indirectly | `DigiByte.Crypto` and `DigiByte.Wallet` are not changed. The wallet's `DigiByte.Web` changes only how its CSS and fonts are referenced. That is guarded by a visual comparison of the style gallery (quickstart Q1), not unit tests. New DigiPay logic gets tests in a new test project anyway. |
| IV. Boundary discipline | **Yes** | New `DigiByte.UI` is a leaf library with no project references, referenced by `DigiByte.Web` and `DigiByte.Pay.Web`. No existing dependency direction is inverted. DigiPay gets the assets at build time and stays independently deployable. The constitution lists the existing projects, and adding a leaf is not an inversion, so no amendment is needed. The PR still states it explicitly for the reviewer. **PASS** |
| V. Resilience | Yes | No new backend dependency. The checkout gains an explicit reconnecting state and never shows a final state because of a dropped connection (FR-011). The dashboard keeps today's behaviour when Pay.Api is down, with clearer messages. **PASS** |
| CSP: self-hosted assets | Yes | Fonts and CSS are served from each app's own origin under `_content/DigiByte.UI/`. No CDN, and no CSP change in the wallet. DigiPay has no CSP today, and this feature does not add one (noted as a follow-up in research R9). **PASS** |
| Secret hygiene | Yes | The `theme` query parameter carries no data. The return-to-section parameter on sign-in is restricted to local `/merchant/...` paths (open-redirect guard, contract C2). No tokens appear in URLs. **PASS** |
| Auth boundary | Yes | Unchanged. The dashboard still sends its `dgp_`/session bearer token from `localStorage`. Routed sections reuse the same auth check. **PASS** |
| Conventional commits, spec-kit, docs | Yes | Docs to update: `docs/UI_STYLE.md` (DigiPay section, shared-source note), `docs/ARCHITECTURE.md` (new `DigiByte.UI` project), and the embed docs (`/docs` page and `samples` READMEs) for the `theme` option. |

**Gate result: PASS.** No violations, so Complexity Tracking is empty.

**Re-check after Phase 1 design: PASS.** The contracts add no backend dependency, secret, auth path or third-party origin:

- **C1** keeps assets same-origin.
- **C2** adds an open-redirect guard and per-store ownership checks.
- **C3** is an additive, data-free query parameter.

Three things are recorded in research R9 as separate future changes and are not taken on here: a DigiPay CSP, a server-side per-store checkout theme, and sharing wallet Razor components.

## Project Structure

### Documentation (this feature)

```text
specs/032-digipay-ui-refresh/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1: preferences, sections, checkout states
├── quickstart.md        # Phase 1: validation guide
├── contracts/
│   ├── ui-style-contract.md   # Shared classes, tokens, theme hook, asset paths
│   ├── dashboard-routes.md    # /merchant/{section} routes, query state, sign-in return
│   └── checkout-theme.md      # theme query parameter + embed script option
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks output (not created here)
```

### Source Code (repository root)

```text
src/
├── DigiByte.UI/                         # NEW Razor Class Library, static assets only, no references
│   ├── DigiByte.UI.csproj
│   └── wwwroot/
│       ├── css/ui.css                   # moved from DigiByte.Web/wwwroot/css/app.css:
│       │                                #   @font-face, --ui-* tokens, buttons, cards, inputs, addresses,
│       │                                #   headings, segmented/choice, icon/back buttons, QR frame,
│       │                                #   stagger, skeleton, reduced motion, bottom menu/rail, toast,
│       │                                #   Baku bubble
│       ├── fonts/inter-latin-wght-normal.woff2           # moved from DigiByte.Web
│       ├── fonts/jetbrains-mono-latin-wght-normal.woff2  # moved from DigiByte.Web
│       └── images/mascot/poses/*.webp   # only the poses DigiPay's dashboard uses (copied)
│
├── DigiByte.Web/                        # wallet: references DigiByte.UI
│   ├── DigiByte.Web.csproj              # + ProjectReference ../DigiByte.UI
│   └── wwwroot/
│       ├── index.html                   # + <link href="_content/DigiByte.UI/css/ui.css"> before app.css
│       └── css/app.css                  # generic layer removed; wallet-only rules stay
│                                        #   (multisig, signing room, scan panel, coin, card finishes)
│
└── DigiByte.Pay.Web/
    ├── DigiByte.Pay.Web.csproj          # + ProjectReference ../DigiByte.UI
    ├── tailwind.config.js               # darkMode 'class'; Inter + JetBrains Mono families
    ├── wwwroot/
    │   ├── theme.js                     # rewritten: light | dark | system, .dark class, ?theme= override
    │   ├── css/tailwind-input.css       # keeps entrance animations; drops duplicates of ui.css
    │   └── embed/digipay.js             # + optional `theme` / data-theme option → ?theme=
    └── Components/
        ├── App.razor                    # + ui.css, font preload, skip link target
        ├── Layout/
        │   ├── MainLayout.razor         # skip link + ToastHost
        │   ├── SiteLayout.razor         # NEW: SiteNav (sheet on phones) + footer, for public pages
        │   └── MerchantLayout.razor     # NEW: rail ≥1024px, bottom menu below; auth gate; store switcher
        ├── Shared/
        │   ├── Ui/                      # NEW thin wrappers emitting shared classes
        │   │   ├── Toast.razor + ToastService.cs
        │   │   ├── CopyButton.razor     # clipboard + notice + select-on-failure
        │   │   ├── StatusBadge.razor    # checkout state → badge + icon + text
        │   │   ├── ConfirmDialog.razor  # role=dialog, focus trap, Escape
        │   │   ├── EmptyState.razor     # optional Baku pose (dashboard only)
        │   │   ├── Skeleton.razor
        │   │   ├── ThemePicker.razor    # Light / Dark / Match my device (replaces ThemeToggle)
        │   │   └── LiveRegion.razor     # aria-live announcer
        │   └── (existing: Icon, Loader, CodeBlock, … restyled)
        └── Pages/
            ├── Checkout.razor, Receipt.razor, Donate.razor, TerminalState.razor, Pos.razor   # restyled
            ├── Merchant/                # NEW: Dashboard.razor split into routed pages
            │   ├── Overview.razor (/merchant, /merchant/overview)
            │   ├── Analytics.razor, Sessions.razor, PaymentLinks.razor, Donation.razor,
            │   │   Webhook.razor, Stores.razor, ApiKeys.razor, AuditLog.razor, PosMode.razor,
            │   │   Settings.razor, NewPayment.razor
            │   ├── SessionDetail.razor (moved; /merchant/sessions/{id} unchanged)
            │   └── MerchantState.cs     # scoped: token, merchant, active store, cached lists
            └── (public pages restyled in place)

tests/
└── DigiByte.Pay.Web.Tests/              # NEW xUnit + bUnit
    ├── ThemePrecedenceTests.cs
    ├── DashboardRouteTests.cs           # every section reachable; old /merchant still Overview
    ├── StatusBadgeTests.cs              # every checkout state has badge, text, announcement
    ├── CopyButtonTests.cs
    └── ReturnUrlGuardTests.cs           # only local /merchant paths accepted
src/DigiByte.Pay.Web/tests/js/theme.test.mjs   # NEW node --test for theme.js precedence

docker/pay-web/Dockerfile                # + COPY src/DigiByte.UI/DigiByte.UI.csproj for restore
docker/web/Dockerfile                    # + same
DigiByte.slnx                            # + DigiByte.UI, DigiByte.Pay.Web.Tests
```

**Structure Decision**: Keep the existing monorepo layout. Add one leaf library (`src/DigiByte.UI`) for the shared style source and one test project. DigiPay's dashboard moves from one tabbed page to a `Pages/Merchant/` folder of routed pages under a shared `MerchantLayout`.

## Delivery Order

The order follows spec priorities. Each stage can be merged and released on its own:

1. **Shared source and wallet extraction**: create `DigiByte.UI`, move the CSS and fonts, and prove the wallet is unchanged (quickstart Q1). Nothing in DigiPay changes yet.
2. **DigiPay foundation**: reference `DigiByte.UI`, new `theme.js`, Tailwind config, `MainLayout` skip link, `ToastHost` and the `Ui/` wrappers.
3. **Shopper pages (US1, P1)**: checkout, receipt, donation page. Includes the copy buttons, status badge, live region, reconnecting state, `?theme=` and the embed option.
4. **Dashboard navigation (US2, P1)**: `MerchantLayout`, routed sections, sign-in return, back button.
5. **Style layer everywhere (US3, P1)**: convert the remaining dashboard and POS controls to the shared classes.
6. **Feedback states (US4)**, **theme picker and POS theme (US5)**, **accessibility pass (US6)**.
7. **Public website (US7, P3)** and docs.

## Complexity Tracking

No constitution violations, so nothing to justify.
