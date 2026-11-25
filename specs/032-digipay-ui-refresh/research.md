# Research: DigiPay UI Refresh

Phase 0 decisions for [plan.md](plan.md). Technical Context has no open "NEEDS CLARIFICATION" items. Each decision below records what was chosen, why, and what else was considered.

## R1. How the wallet and DigiPay share one style source

- **Decision**: Create a new Razor Class Library, `src/DigiByte.UI`, that holds only static web assets:
  - `css/ui.css`
  - the two woff2 fonts
  - the Baku poses that DigiPay uses

  `DigiByte.Web` and `DigiByte.Pay.Web` both reference it. Each app serves the files from its own origin at `_content/DigiByte.UI/...`.
- **Rationale**:
  - **One file to edit.** A change reaches both apps on their next build (clarification 3).
  - **Copied at build time, not fetched.** The assets ship inside each app's publish output, so DigiPay never fetches from the wallet's site (Principle IV), and both Dockerfiles only need to copy one more csproj.
  - **Works in both apps.** RCL static assets work in Blazor WebAssembly and Blazor Server alike, appear in the wallet's `service-worker-assets.js` manifest (so they are cached for offline use), and need no Node at build time. That matters because DigiPay's Docker image has no Node.
  - **Safe for the wallet's CSP.** Same-origin assets leave its strict CSP (`style-src 'self'`) untouched.
- **Alternatives considered**:
  - *DigiPay links the wallet's `app.css` file directly* (MSBuild `Link`). Rejected: DigiPay would depend on files inside the wallet project, and the wallet-only rules (multisig, signing room, scan panel) would leak into DigiPay.
  - *An npm package or Tailwind plugin.* Rejected: it needs Node in DigiPay's Docker build and a publishing step.
  - *Loading the CSS from the wallet's domain at runtime.* Rejected: it breaks independent deployment, creates a cross-origin dependency, and would need CSP changes later.
  - *Copying the CSS by hand* (option B in clarification 3). Rejected by the user.

## R2. What moves into `ui.css`, and what stays in the wallet

- **Decision**:
  - **Moves**: the `@font-face` rules; the `--ui-*` and `--motion-*` tokens (`:root` and `.dark`); buttons; cards; inputs and field wrappers; addresses; headings; segmented control; choice buttons; icon and back buttons; muted text; no-zoom-on-focus; QR frame; stagger; skeleton; the reduced-motion block; the generic bottom menu and its ≥1024px rail; toast styling; and the Baku bubble.
  - **Stays in the wallet's `app.css`**: wallet card finishes, spinning coin, success celebration, slide-to-confirm, shared-wallet mode, pending payments, signing room, shared wallet list and scan panel.
- **Rationale**: Only generic, reusable blocks move. Wallet features stay wallet-owned, so DigiPay's CSS doesn't carry their weight. Slide-to-confirm stays because DigiPay has no money-moving confirmations that need it. Refunds and voids use a confirm dialog.
- **Guard**:
  - The wallet loads `ui.css` immediately before `app.css`, so the cascade order is unchanged.
  - The style gallery at `/test-animations` is screenshotted before and after, in both themes at 375px and 1280px, and must match pixel for pixel (quickstart Q1).
  - `docs/UI_STYLE.md` is updated to say where the rules now live.

## R3. Theme mechanism in DigiPay

- **Decision**:
  - **Same selector as the wallet.** Switch DigiPay to the wallet's `.dark` class on `<html>` (Tailwind `darkMode: 'class'`), because `ui.css` keys off `.dark`.
  - **Three modes.** Rewrite `wwwroot/theme.js` for `light | dark | system`, and keep the existing storage key `dp-theme`, so a saved `light`/`dark` survives. No saved value means `system`, the clarified default.
  - **Precedence**, highest first:
    1. A valid `?theme=light|dark` on `/pay/*` pages. It applies to that page view only and is not saved.
    2. On `/pos`, the POS key `digipay.pos.theme:v1`, default `dark`.
    3. `dp-theme`.
    4. The device setting.
  - **Keep the existing safeguards**: the MutationObserver and enhanced-navigation re-apply hooks, which handle Blazor stripping the attribute.
  - **Browser chrome colour.** Set `<meta name="theme-color">` like the wallet does.
- **Rationale**: Reusing the wallet's selector means `ui.css` needs no per-app variants. Keeping the key avoids resetting existing users. Running the script synchronously in `<head>` (as today) avoids a flash (FR-027).
- **Alternatives considered**: Rewriting `ui.css` to support both `[data-theme=dark]` and `.dark` doubles every themed selector. Rejected.

## R4. Shop-set checkout theme (FR-026a)

- **Decision**:
  - **Pages**: `/pay/{id}`, `/pay/{id}/receipt` and `/pay/button` accept an optional `theme=light|dark` query parameter. Other values are ignored.
  - **Embed script**: `digipay.js` accepts `theme` in `DigiPay.checkout({...})` and `DigiPay.button({...})`, and a `data-theme` attribute on auto-bound elements. It appends `theme=` to the iframe URL.
  - **Redirects**: shops that redirect add the parameter to the `CheckoutUrl` themselves.
  - **API**: no change to the Pay API or its session model.
- **Rationale**: Purely additive. Shops that set nothing behave exactly as today (FR-036, SC-010). The parameter carries no personal or payment data, so a query string is fine.
- **Alternatives considered**: A per-store theme setting stored on the server. Rejected for this feature: it needs an API and data-model change and a migration, and the constitution treats Pay API changes as a separate review. It can follow later.

## R5. Dashboard routing

- **Decision**:
  - **Pages**: replace the in-page `activeTab` field with routed pages under `/merchant/{section}`, sharing `MerchantLayout`. `/merchant` keeps showing Overview. The section slugs are listed in [contracts/dashboard-routes.md](contracts/dashboard-routes.md).
  - **Shared state**: the token, merchant, active store and fetched lists move to a scoped `MerchantState` service. Moving between sections doesn't refetch everything, and back and forward restore the section instantly.
  - **Filters**: Sessions filters live in the query string.
  - **Store**: the active store is a `?store=` query parameter, falling back to the last used store.
  - **Sign-in return**: `Signin` accepts `?return=` and only honours local paths that start with `/merchant`.
- **Rationale**: This gives bookmarking, deep links and working back and forward buttons (FR-014, FR-015, SC-006). The scoped service keeps InteractiveServer circuit costs flat. The return guard stops open redirects.
- **Alternatives considered**:
  - *Hash fragments* (`/merchant#webhook`). Rejected: Blazor routing ignores fragments, and back-button handling would be hand-rolled.
  - *One page with a route parameter* (`/merchant/{section?}`). Rejected: it keeps the 1,785-line file and the coupling.

## R6. Shared components: share Razor components or only CSS?

- **Decision**: Share only CSS, fonts and images through `DigiByte.UI` in this feature. DigiPay gets its own thin wrappers in `Components/Shared/Ui/`:
  - `Toast` and `ToastService`
  - `CopyButton`
  - `StatusBadge`
  - `ConfirmDialog`
  - `EmptyState`
  - `Skeleton`
  - `ThemePicker`
  - `LiveRegion`

  The wrappers output the shared class names described in [contracts/ui-style-contract.md](contracts/ui-style-contract.md).
- **Rationale**: The wallet's components depend on wallet services. For example, `CopyValueButton` needs `ClipboardService` and `LocalizationService`, and toasts need `NotificationService`. Moving them would drag wallet services into a shared library or require refactoring the wallet. Sharing the class contract gives the same look with no wallet code changes beyond CSS references.
- **Alternatives considered**: Moving the wallet components into `DigiByte.UI` now. Deferred: worth doing once both apps have stable needs. Noted as a follow-up.

## R7. Testing approach

- **Decision**:
  - **New test project.** Add `tests/DigiByte.Pay.Web.Tests` (xUnit + bUnit). It covers theme precedence (through the C# helper that mirrors the JS rules), dashboard route mapping, the status badge mapping for every checkout state, CopyButton behaviour and the return-path guard.
  - **Theme script.** Add `node --test` coverage for `theme.js`, mirroring `src/DigiByte.Web/tests/js`.
  - **Accessibility and timing.** These run as a scripted Playwright + axe-core pass in quickstart Q5–Q6, via the official Playwright container already used for the WooCommerce screenshots, so the repo needs no new Node dependency. It is not a CI gate in this feature.
- **Rationale**: The merge gates are `dotnet build` and `dotnet test`, and bUnit fits them. Accessibility and timing checks need a real browser, so they are verification steps rather than unit tests.
- **Alternatives considered**: Adding Playwright as a .NET test project in CI. Deferred: it needs browsers in CI and is a separate infrastructure decision.

## R8. Checkout speed (FR-013a, SC-011)

- **Decision**:
  - **Measure first.** Record today's checkout load as the baseline before any change (quickstart Q6).
  - **Fonts.** Preload only Inter with `font-display: swap`. JetBrains Mono loads on demand (also swap), since only the address uses it.
  - **CSS.** Ship `ui.css` minified at publish.
  - **Fallback.** If the restyled checkout misses 2 s or the baseline, opt the shopper pages (`/pay/*`) into static server rendering with prerendering, keeping InteractiveServer for live updates. That way the amount and QR code show before the interactive connection opens.
- **Rationale**: Today `Routes` uses `prerender: false`, so the page shows nothing until the interactive connection is up. That is the biggest likely contributor to load time, larger than the fonts (88 KB in total, with swap). Measuring first avoids an architecture change that may not be needed.
- **Alternatives considered**: Dropping custom fonts on the checkout (option C in clarification 5). Rejected by the user, since it breaks visual consistency.

## R9. Deferred and out of scope (recorded for later)

- A server-side per-store checkout theme (see R4).
- Moving wallet Razor components into `DigiByte.UI` (see R6).
- A Content Security Policy for DigiPay. It has none today. Adding one is a security improvement outside this restyle and should be its own change.
- A language option. The user chose English only.
- Logging and metrics. The restyle adds nothing to monitor.
