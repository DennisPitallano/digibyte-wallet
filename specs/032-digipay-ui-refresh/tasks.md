---

description: "Task list for the DigiPay UI refresh"
---

# Tasks: DigiPay Website and Merchant Dashboard Follow the Wallet's Look and Feel

**Input**: Design documents in `specs/032-digipay-ui-refresh/`: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included, because plan.md defines a test project (`tests/DigiByte.Pay.Web.Tests`) and a `node --test` suite for `theme.js`. Accessibility and timing checks are manual quickstart steps, not tasks that write tests.

**Organization**: Tasks are grouped by user story (US1–US7 from spec.md), so each story can be built, tested and released on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: The user story the task belongs to
- Paths are relative to the repository root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Record the baseline and create the new projects.

- [X] T001 Record the quickstart Q0 baseline on `main` before any change:
  - ten cold-load timings of a pending checkout (Fast 4G, 375px) until the amount and QR code are visible;
  - screenshots of the wallet's `/test-animations` gallery in light and dark at 375px and 1280px.

  Save them in `specs/032-digipay-ui-refresh/baseline/` as `timings.md` plus the PNGs.
- [X] T002 Create the Razor Class Library `src/DigiByte.UI/DigiByte.UI.csproj`: `net10.0`, `Microsoft.NET.Sdk.Razor`, no package or project references, with an empty `wwwroot/` holding `css/`, `fonts/` and `images/mascot/poses/`.
- [X] T003 Add `src/DigiByte.UI/DigiByte.UI.csproj` to `DigiByte.slnx`.
- [X] T004 [P] Create the test project `tests/DigiByte.Pay.Web.Tests/DigiByte.Pay.Web.Tests.csproj` (xUnit + bUnit, referencing `src/DigiByte.Pay.Web`) and add it to `DigiByte.slnx`.
- [X] T005 [P] Add `COPY src/DigiByte.UI/DigiByte.UI.csproj src/DigiByte.UI/` to the restore layer of `docker/pay-web/Dockerfile`.
- [X] T006 [P] Add the same `COPY` line to the restore layer of `docker/web/Dockerfile`.
- [X] T007 [P] Add a `test` script to `src/DigiByte.Pay.Web/package.json` (`node --test "tests/js/**/*.test.mjs"`) and create the folder `src/DigiByte.Pay.Web/tests/js/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the shared style source, prove the wallet is unchanged, then wire DigiPay to the shared layer, theme script and basic components. Every user story depends on this phase.

**⚠️ CRITICAL**: No user story work can start until this phase is complete.

### 2a. Extract the shared style layer from the wallet (research R1, R2)

- [X] T008 Move `src/DigiByte.Web/wwwroot/fonts/inter-latin-wght-normal.woff2` and `jetbrains-mono-latin-wght-normal.woff2` to `src/DigiByte.UI/wwwroot/fonts/` with `git mv`.
- [X] T009 Create `src/DigiByte.UI/wwwroot/css/ui.css` by moving these blocks out of `src/DigiByte.Web/wwwroot/css/app.css`, in their current order:
  - the `@font-face` rules (lines ~4–27), with `src` paths rewritten to `../fonts/…`;
  - the `--ui-*` and `--motion-*` token block (`:root` and `.dark`, from line ~855);
  - Buttons, Cards, Text inputs, Addresses, Headings, Tabs (segmented control), Choice buttons, Icon buttons, Back button, Muted text, No zoom on focus, QR frame, Staggered list entrance, and the generic Bottom menu block (lines ~965–1933, excluding "Recovery phrase" and "Slide to confirm");
  - Skeleton loading (line ~244);
  - Accessibility: Reduced Motion (line ~435);
  - the Baku mascot block (line ~470).

  Leave the wallet-only blocks in `app.css`: recovery phrase, slide to confirm, card finishes, coin, celebration, shared-wallet mode, pending payments, signing room, shared wallet list, scan panel. See [contracts/ui-style-contract.md](contracts/ui-style-contract.md).
- [X] T010 Add `<ProjectReference Include="..\DigiByte.UI\DigiByte.UI.csproj" />` to `src/DigiByte.Web/DigiByte.Web.csproj`.
- [X] T011 In `src/DigiByte.Web/wwwroot/index.html`, add `<link rel="stylesheet" href="_content/DigiByte.UI/css/ui.css" />` immediately before the `css/app.css` link. Change the font preload links, if any, to `_content/DigiByte.UI/fonts/…`.
- [ ] T012 Check that the CSP in `src/DigiByte.Web/wwwroot/index.html` and the Nginx security headers in `src/DigiByte.Web` need no change, since `_content/…` is same-origin. Then publish the wallet and confirm that `service-worker-assets.js` lists `_content/DigiByte.UI/css/ui.css` and both fonts.
- [X] T013 Run quickstart Q1: retake the gallery screenshots, compare them pixel for pixel with the T001 baseline, check offline load and check the console for CSP errors. Fix any difference before continuing.
- [X] T014 Update `docs/UI_STYLE.md`, section "How it is built", to say the generic layer lives in `src/DigiByte.UI/wwwroot/css/ui.css`, that the wallet's `app.css` keeps only wallet-only rules, and that the change rule in contracts/ui-style-contract.md applies.

### 2b. DigiPay uses the shared layer (research R3, R6)

- [X] T015 Add `<ProjectReference Include="..\DigiByte.UI\DigiByte.UI.csproj" />` to `src/DigiByte.Pay.Web/DigiByte.Pay.Web.csproj`.
- [X] T016 Update `src/DigiByte.Pay.Web/tailwind.config.js`:
  - `darkMode: 'class'`;
  - `fontFamily.sans: ['Inter', 'system-ui', '-apple-system', 'sans-serif']`;
  - `fontFamily.mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Consolas', 'monospace']`.

  Rebuild and commit `wwwroot/css/tailwind.css`, because the Docker build has no Node.
- [X] T017 Rewrite `src/DigiByte.Pay.Web/wwwroot/theme.js` per data-model §1 and §2:
  - modes `light | dark | system`, key `dp-theme`; missing or invalid means `system`;
  - on `/pay/*`, a `?theme=light|dark` override (case-insensitive) that is never saved; other values are ignored;
  - on `/pos`, the key `digipay.pos.theme:v1`, where missing means `dark`;
  - toggle the `dark` class on `<html>` and set `<meta name="theme-color">` to `#002352` or `#f8fafc`;
  - keep the MutationObserver and enhanced-navigation re-apply hooks, and follow live device changes while in `system` mode;
  - expose `window.DigiPayTheme = { getMode, setMode, resolve, apply }`.
- [X] T018 [P] Write `src/DigiByte.Pay.Web/tests/js/theme.test.mjs` (node --test with a fake DOM, `localStorage` and `matchMedia`). Cover:
  - saved `light` and `dark` are kept;
  - missing → device;
  - invalid → `system`;
  - `?theme=light` on `/pay/x` wins over a saved `dark` and isn't saved;
  - `?theme=purple` is ignored;
  - `?theme=` on `/merchant` is ignored;
  - `/pos` defaults to dark;
  - device changes re-apply only in `system` mode.
- [X] T019 [P] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/ThemeResolver.cs`: a pure C# mirror of the T017 precedence, used for tests and for any server-side `<html>` class hint. Add `tests/DigiByte.Pay.Web.Tests/ThemePrecedenceTests.cs` with the same cases as T018.
- [X] T020 Update `src/DigiByte.Pay.Web/Components/App.razor`:
  - add `<link rel="stylesheet" href="@Assets["_content/DigiByte.UI/css/ui.css"]" />` after `tailwind.css`;
  - add `<link rel="preload" as="font" type="font/woff2" crossorigin href="_content/DigiByte.UI/fonts/inter-latin-wght-normal.woff2">` and `<meta name="theme-color">`;
  - keep `theme.js` synchronous in `<head>`.
- [X] T021 Remove the duplicated base rules from `src/DigiByte.Pay.Web/wwwroot/css/tailwind-input.css` and `src/DigiByte.Pay.Web/wwwroot/app.css`: the font stack, focus styles, and any button or input base. Keep the `page-enter`, `slide-up-*`, `fade-in-*` and `card-hover` animations, with their reduced-motion opt-out.
- [X] T022 Update `src/DigiByte.Pay.Web/Components/Layout/MainLayout.razor`:
  - a "Skip to main content" link as the first focusable element, targeting `<main id="main" tabindex="-1">`;
  - a `<ToastHost />`;
  - a `<LiveRegion />` host.
- [X] T023 [P] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/ToastService.cs` (scoped; register it in `src/DigiByte.Pay.Web/Program.cs`) and `Toast.razor`/`ToastHost.razor`, per data-model §6:
  - tones `success | info | warning | error`;
  - 3 s by default; `error` stays until dismissed;
  - `role=status` for success and info, `role=alert` for warning and error;
  - identical text within 2 s is merged;
  - an optional action button;
  - positioned clear of the bottom menu.
- [X] T024 [P] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/CopyButton.razor`, plus `wwwroot/clipboard.js` loaded from `App.razor`:
  - copies `Value` with `navigator.clipboard.writeText`;
  - shows a success notice;
  - on failure, selects the target element's text and shows an info notice explaining manual copy;
  - `aria-label` comes from a `Label` parameter;
  - `btn-ghost btn-sm` or `icon-btn` variants.
- [X] T025 [P] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/ConfirmDialog.razor` and `wwwroot/dialog.js`:
  - `role="dialog"`, `aria-modal="true"` and `aria-labelledby` set to the title;
  - focus moves in and is trapped; Escape and Cancel close it; focus returns to the opener;
  - `card-raised` surface, with `btn-danger` and `btn-secondary` buttons.
- [X] T026 [P] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/LiveRegion.razor` and `LiveAnnouncer.cs`: a scoped service with `Announce(string text, bool assertive = false)` that renders a visually hidden `aria-live` region.
- [X] T027 [P] Copy `support.webp`, `greeting.webp`, `idea.webp` and `thinking.webp` from `src/DigiByte.Web/wwwroot/images/mascot/poses/` to `src/DigiByte.UI/wwwroot/images/mascot/poses/` (copy, don't move; the wallet keeps its own). Create `src/DigiByte.Pay.Web/Components/Shared/Ui/Skeleton.razor` (shapes `line | block | row | card`, count, uses `skeleton`) and `EmptyState.razor` (title, text, an optional action, and an optional `BakuPose` parameter limited to `support | greeting | idea | thinking` that renders the Baku bubble from `_content/DigiByte.UI/images/mascot/poses/{pose}.webp`).
- [X] T028 [P] Add `tests/DigiByte.Pay.Web.Tests/CopyButtonTests.cs` (bUnit, mocked JS interop): success raises a success notice; failure selects the text and raises an info notice; there is an accessible name. Add `tests/DigiByte.Pay.Web.Tests/ConfirmDialogTests.cs`: role and label attributes, Cancel and Escape raise `OnCancel`, and Confirm raises `OnConfirm`.

**Checkpoint**: The wallet looks pixel-identical. DigiPay loads `ui.css`, the fonts and the new theme script. The shared wrappers exist and are tested. User stories can start.

---

## Phase 3: User Story 1 - A shopper pays on a checkout that looks and works like the wallet (Priority: P1) 🎯 MVP

**Goal**: The checkout, receipt and donation page use the shared layer and have copy buttons, clear announced states, a reconnecting state and no technical errors. Shops can set the theme.

**Independent Test**: Quickstart Q2 and Q3, plus Q6 for speed.

### Tests for User Story 1

- [X] T029 [P] [US1] Write `tests/DigiByte.Pay.Web.Tests/StatusBadgeTests.cs`. Enumerate every row of data-model §5 (API statuses `pending, seen, paid, confirmed, underpaid, expired, refunded, voided` plus client states `reconnecting` and `load-failed`). Assert:
  - each row's badge text, tone, icon and announcement exactly as the table says;
  - an unknown status renders as `pending` with no announcement;
  - no emoji or symbol glyphs appear in the output.
- [X] T030 [P] [US1] Write `tests/DigiByte.Pay.Web.Tests/CheckoutErrorTests.cs` (bUnit, HttpClient that throws or returns 404). The page shows "This payment page could not be loaded", a Retry button, and no exception message text (FR-010).

### Implementation for User Story 1

- [X] T031 [P] [US1] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/CheckoutStates.cs`: the data-model §5 table as a static map from a status string to `(BadgeText, Tone, Icon, Announcement, IsFinal)`. Final states are `confirmed, expired, underpaid, refunded, voided`.
- [X] T032 [P] [US1] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/StatusBadge.razor`, which renders from `CheckoutStates` with shared badge tone classes and an `Icon` from the shared icon set. It replaces `src/DigiByte.Pay.Web/Components/Pages/CheckoutStatusPill.razor`: delete the old file and update its callers.
- [X] T033 [US1] Restyle `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor` with the shared classes:
  - `card` container;
  - `qr-frame` around the QR code, with the logo at most 1/5 of the code's width;
  - **Open in wallet** as `btn-primary`;
  - the address in `font-mono` with a `CopyButton`, and the DGB amount with a `CopyButton` that copies the plain decimal string (no symbol or separators; FR-008);
  - the price-drift banner in warning tone;
  - the countdown kept.
- [X] T034 [US1] In `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor`, call `LiveAnnouncer.Announce` with the §5 announcement when the session status changes. Make sure a toast never covers the status badge (place the badge outside the toast area).
- [X] T035 [US1] In `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor`, handle the SignalR `Reconnecting`, `Reconnected` and `Closed` events (`hub` at line ~208):
  - show the `reconnecting` badge and keep the last known state;
  - on `Closed`, retry with backoff and also poll the session over HTTP every 10 s while not final;
  - never switch to a final state without a server status saying so (FR-011).
- [X] T036 [US1] In `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor`, replace `loadError = $"Could not load session: {ex.Message}"` (line ~174) with the `load-failed` state: plain text, Retry, and Back to shop when `ReturnUrl` is known. Log the exception to the server logger only.
- [X] T037 [US1] Restyle `src/DigiByte.Pay.Web/Components/Pages/TerminalState.razor`:
  - drop the glyph icons (⏱ ! ↩ ⊘) in favour of `StatusBadge` and the shared icons, with the plain-language explanation and next step from §5;
  - `expired` shows no QR code or address;
  - `underpaid` shows the received and expected amounts;
  - Back to shop (`btn-secondary`) when `ReturnUrl` is set.
- [X] T038 [P] [US1] Restyle `src/DigiByte.Pay.Web/Components/Pages/Receipt.razor`: `card`, `CopyButton` on the txid, `font-mono` values, `qr-frame-sm` on the txid QR, and the load-failed state without exception text. Add a print stylesheet section (`@media print`) so it prints dark on white in either theme (FR-028).
- [X] T039 [P] [US1] Restyle `src/DigiByte.Pay.Web/Components/Pages/Donate.razor`: `card`, `qr-frame`, `CopyButton` for the address and amount, `btn-primary` Open in wallet, `font-mono` address. Keep the existing query parameters `to, amount, name, label`.
- [X] T040 [US1] In `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor`, carry a valid `theme` query value into the "View receipt" link (contract C3).
- [X] T041 [US1] Update `src/DigiByte.Pay.Web/wwwroot/embed/digipay.js`:
  - accept an optional `theme` in `DigiPay.checkout()` and `DigiPay.button()`, and a `data-theme` attribute on auto-bound elements;
  - append `theme=<light|dark>` to the iframe URL only for those two values, and drop anything else silently;
  - style the modal backdrop and close button to match when a theme is given;
  - change nothing else (contract C3).
- [X] T042 [P] [US1] Add a light/dark example with `data-theme` to `src/DigiByte.Pay.Web/wwwroot/embed/demo.html`.
- [X] T043 [US1] Make sure no Baku markup or pose images are referenced from `Checkout.razor`, `Receipt.razor`, `Donate.razor`, `TerminalState.razor` or `Pos.razor` (clarification 2). Add an assertion to `tests/DigiByte.Pay.Web.Tests/StatusBadgeTests.cs` that the rendered checkout contains no `mascot/poses` URL.
- [ ] T044 [US1] Run quickstart Q2, Q3 and Q6 (20 timed runs on the Fast 4G profile at 375px with a cold cache, compared with the baseline median from the T001 runs). Also complete one testnet payment, unchanged, through each shop integration: the embed demo, `samples/express-store`, `samples/flask-store`, `samples/dotnet-store` and the WooCommerce plugin (`samples/woocommerce-plugin`). If the amount and QR code aren't visible within 2 s in at least 19 of 20 runs, or the median is worse than the baseline, apply research R8's fallback:
  - prerender `/pay/*` with static server rendering plus InteractiveServer for live updates;
  - in `src/DigiByte.Pay.Web/Components/Routes.razor`, `App.razor` and the three shopper pages.

  Measure again.

**Checkpoint**: The shopper checkout is complete, matches the wallet and can be released on its own.

---

## Phase 4: User Story 2 - The dashboard is easy to move around, on desktop and phone (Priority: P1)

**Goal**: Every dashboard section has its own URL, back and forward work, there's a left rail on wide screens and a bottom menu on phones, and the merchant returns to the right place after sign-in.

**Independent Test**: Quickstart Q4.

### Tests for User Story 2

- [ ] T045 [P] [US2] Write `tests/DigiByte.Pay.Web.Tests/DashboardRouteTests.cs`:
  - every route in contracts/dashboard-routes.md resolves to its page component;
  - `/merchant` renders Overview;
  - `/merchant/nope` renders the not-found page with a link to Overview;
  - the bottom menu items are exactly Overview, Sessions, New payment (raised), Payment links and More;
  - the current item has `aria-current="page"`.
- [ ] T046 [P] [US2] Write `tests/DigiByte.Pay.Web.Tests/ReturnUrlGuardTests.cs` for the guard in contract C2.
  - **Accepts**: `/merchant/webhook`, `/merchant/sessions?status=paid`.
  - **Rejects to `/merchant`**: `https://evil.example`, `//evil.example`, `/\evil.example`, `%2F%2Fevil.example`, `javascript:alert(1)`, `/docs`, `/merchant/signin`.

### Implementation for User Story 2

- [ ] T047 [P] [US2] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/ReturnUrlGuard.cs`: `static string Sanitize(string? value)`, implementing the contract C2 rules (relative path starting with `/merchant`, not `/merchant/signin`, no `//`, `/\`, scheme or host, checked again after URL-decoding).
- [ ] T048 [P] [US2] Create `src/DigiByte.Pay.Web/Components/Pages/Merchant/DashboardSections.cs` with the data-model §3 table: key, route, label, icon, bottom-menu position, More membership, and empty-state text and pose.
- [ ] T049 [US2] Create `src/DigiByte.Pay.Web/Components/Pages/Merchant/MerchantState.cs` (scoped; register it in `Program.cs`), per data-model §4:
  - move the token load (`localStorage["digipay-token"]`), the authenticated `HttpClient` helper with the `Authorization: Bearer` header, merchant and store loading, and sign-out out of `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor` (lines ~1020–1060 and ~1700);
  - `ActiveStoreId` comes from `?store=`, else the last used store, else the first store;
  - cached lists refresh when older than 60 s or after a change;
  - a missing or rejected token sends the merchant to `/merchant/signin?return=<current path+query>`.
- [ ] T050 [US2] Create `src/DigiByte.Pay.Web/Components/Layout/MerchantLayout.razor` (with `.razor.css`). It uses `MerchantState` for auth and has:
  - from 1024px, a left rail listing every section plus the store switcher (searchable when there are more than 10 stores), Resources links, the account, Sign out and the ThemePicker slot;
  - below 1024px, the shared bottom-menu classes: Overview, Sessions, raised New payment, Payment links and More, where More opens a sheet with the rest plus the store switcher;
  - `aria-current="page"` on the current item;
  - a content area that is wide (capped at 1280px) for list, table, chart and log pages, with a `Narrow` layout parameter for forms (480px, the wallet's column; clarification 1, second pass).
- [ ] T051 [US2] Split `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor` into routed pages under `src/DigiByte.Pay.Web/Components/Pages/Merchant/`, each with `@layout MerchantLayout` and its section's `@page` routes from contract C2. Move each tab's markup and code verbatim first, and restyle later in US3:
  - `Overview.razor` (`/merchant`, `/merchant/overview`)
  - `NewPayment.razor` (`/merchant/new`, Narrow)
  - `Sessions.razor` (`/merchant/sessions`)
  - `PaymentLinks.razor` (`/merchant/links`)
  - `Analytics.razor` (`/merchant/analytics`)
  - `Donation.razor` (`/merchant/donation`)
  - `Webhook.razor` (`/merchant/webhook`)
  - `Stores.razor` (`/merchant/stores`)
  - `ApiKeys.razor` (`/merchant/api-keys`)
  - `AuditLog.razor` (`/merchant/audit`)
  - `PosMode.razor` (`/merchant/pos`)
  - `Settings.razor` (`/merchant/settings`, Narrow)

  Delete `Dashboard.razor` once every section works.
- [ ] T052 [US2] Move `src/DigiByte.Pay.Web/Components/Pages/SessionDetail.razor` to `src/DigiByte.Pay.Web/Components/Pages/Merchant/SessionDetail.razor`. Keep `@page "/merchant/sessions/{SessionId}"`, apply `MerchantLayout`, and add a `back-btn` that returns to the referring Sessions URL (query kept), or `/merchant/sessions`.
- [ ] T053 [US2] Put query-string state on the section pages:
  - Sessions: `status`, `q`, `page`. Search uses `replace` navigation; changing the filter or page adds a history entry.
  - Analytics: `range` (`24h, 7d, 30d, 90d`).
  - Webhook: `delivery` (`failed, all`).
  - Audit: `page`.

  Each page reads its values on load and on location change.
- [ ] T054 [US2] Validate `?store=` ownership in `MerchantState`. If the store isn't one of the merchant's, render the not-found page and never request that store's data.
- [ ] T055 [US2] Update `src/DigiByte.Pay.Web/Components/Pages/Signin.razor`: read `?return=`, pass it through `ReturnUrlGuard.Sanitize`, and navigate there after a successful Digi-ID sign-in (default `/merchant`).
- [ ] T056 [US2] Replace `src/DigiByte.Pay.Web/Components/Pages/NotFound.razor` with a DigiPay-styled page: `page-title`, a short explanation, and a link to Overview when signed in, or to Home otherwise.
- [ ] T057 [US2] Set the page titles to `<Section> · DigiPay` on every Merchant page with `<PageTitle>`. Check that `FocusOnNavigate` in `src/DigiByte.Pay.Web/Components/Routes.razor` still focuses the page `h1`.
- [ ] T058 [US2] Update internal links that pointed at dashboard tabs (SiteNav "Dashboard", Signin redirect, links inside sections, `SessionDetail` back link) to the new routes. Search for `"/merchant"` across `src/DigiByte.Pay.Web/Components/`.
- [ ] T059 [US2] Run quickstart Q4.

**Checkpoint**: The dashboard can be bookmarked, works with back and forward, and is easy to use on desktop and phone. It works on its own without US1.

---

## Phase 5: User Story 3 - One style layer across every DigiPay page (Priority: P1)

**Goal**: Every DigiPay control uses the shared building blocks: 48/44px buttons, 16px fields, mono values and danger confirmations.

**Independent Test**: Quickstart Q5 step 8, comparing side by side with the wallet's gallery.

- [ ] T060 [US3] Delete the per-page class bundle constants (`Card`, `Btn`, `BtnGhost`, `Input` and so on) from the Merchant pages created in T051 and from `src/DigiByte.Pay.Web/Components/Pages/Merchant/SessionDetail.razor`. Replace them with `card`, `btn-primary`, `btn-secondary`, `btn-ghost`, `btn-sm`, `input`, `field`, `field-label`, `field-hint` and `field-error`, plus `page-title`, `block-title` and `section-label`. Keep only Tailwind layout utilities beside them.
- [ ] T061 [P] [US3] Convert the tables in `src/DigiByte.Pay.Web/Components/Pages/Merchant/Sessions.razor`, `Webhook.razor` (delivery log), `ApiKeys.razor`, `AuditLog.razor` and `src/DigiByte.Pay.Web/Components/Shared/AuditPanel.razor` to shared-style tables on wide screens and stacked `card` rows below 768px. Long ids wrap or are shortened, with a `CopyButton` for the full value.
- [ ] T062 [US3] Show every address, txid, API key, pairing code and code snippet in `font-mono` across the Merchant pages, `src/DigiByte.Pay.Web/Components/Shared/CodeBlock.razor` and `InlineCode.razor`.
- [ ] T063 [US3] Wrap the destructive actions in `ConfirmDialog` with `btn-danger`: revoke API key (`ApiKeys.razor`), void and refund (`SessionDetail.razor`), unpair device (`PosMode.razor`), remove store (`Stores.razor`). Each dialog states what will happen and whether it can be undone.
- [ ] T064 [P] [US3] Convert the choice controls to `segmented` / `choice-pill` with `aria-pressed`: the Sessions status filter, the Analytics range, the Webhook delivery filter, the Settings toggles (DigiDollar beta, expiry), and the PaymentLinks options.
- [ ] T065 [US3] Restyle `src/DigiByte.Pay.Web/Components/Pages/Pos.razor`:
  - keep the full-screen keypad and QR layout, the pairing flows, the manager PIN modal and the unpair modal;
  - remove the hardcoded `bg-slate-950` and `dgb-900`, so colours come from `ui.css` tokens and `.dark`;
  - keypad keys stay large (≥ 64px), using `btn-secondary`;
  - the Unpair button uses `btn-danger` (red-600), replacing `bg-red-500`;
  - the PIN and unpair modals use `ConfirmDialog` semantics (`role=dialog`, focus trap, Escape);
  - the pairing method toggle gets `aria-pressed`;
  - the QR code sits in `qr-frame`.
- [ ] T066 [P] [US3] Restyle `src/DigiByte.Pay.Web/Components/Pages/Signin.razor`: `card` surface, `qr-frame` around the Digi-ID QR code, shared buttons, and a `back-btn` link to Home.
- [ ] T067 [P] [US3] Make `src/DigiByte.Pay.Web/Components/Shared/Icon.razor` match the wallet's icon style (stroke width, size and 24px grid). Add the icons used by `CheckoutStates` (clock, radar, check, check-circle, alert, timer-off, undo, ban, refresh) and by `DashboardSections`. Remove all emoji and glyph status markers across `src/DigiByte.Pay.Web/Components/` (FR-006).
- [ ] T068 [US3] Add a `## DigiPay` section to `docs/UI_STYLE.md`. It lists the shared building blocks DigiPay uses and where DigiPay differs: POS keypad sizing, data tables, code blocks, analytics charts, and the wide dashboard content area (FR-037).
- [ ] T069 [US3] Run quickstart Q5 step 8 (sizes and contrast against the gallery) on every DigiPay page type, in light and dark, at 375px and 1280px. Fix any mismatch.

**Checkpoint**: All P1 stories are done. DigiPay visibly belongs to the wallet's family.

---

## Phase 6: User Story 4 - Clear loading, empty, error and success feedback (Priority: P2)

**Goal**: Content-shaped placeholders, helpful empty states with Baku (dashboard only), notices for success and failure, and setup and webhook banners.

**Independent Test**: Quickstart Q5 steps 1–3, with a brand-new merchant and a throttled network.

- [ ] T070 [US4] Replace every "Loading…" text in `src/DigiByte.Pay.Web/Components/Pages/Merchant/*.razor`, `Shared/AnalyticsPanel.razor` and `Shared/AuditPanel.razor` with `Skeleton` shapes matching each section's content (rows for lists, blocks for stats and charts). Keep `Shared/Loader.razor` only for the first full-page load.
- [ ] T071 [US4] Add an `EmptyState` to each section in data-model §3, using its title, text, action and Baku pose: Sessions, Payment links, Analytics, Webhook, API keys, Audit log and POS mode.
- [ ] T072 [US4] Add a store-readiness check to `MerchantState` (no receive xpub or address set, so the store can't accept payments).
  - **Overview** (`Overview.razor`) shows a setup card with a link to `/merchant/settings`.
  - **New payment** (`NewPayment.razor`) explains why and links to Settings instead of showing the form (FR-023).
- [ ] T073 [US4] Add a failing-webhook banner (warning tone, linking to `/merchant/webhook?delivery=failed`) to `Overview.razor` and `Webhook.razor` when recent deliveries are failing, using the existing delivery-log data (FR-024).
- [ ] T074 [US4] Send success and failure feedback for every action through `ToastService`:
  - save settings, create and revoke a key, create a link, copy a link, send a test webhook, pair and unpair a device, void, refund, and switch store;
  - failures say what went wrong in plain words, with a Retry action where it makes sense, and no exception text.

  Remove the inline "Copied ✓" label swaps.
- [ ] T075 [US4] Run quickstart Q5 steps 1–3.

---

## Phase 7: User Story 5 - Light, dark or match my device, everywhere (Priority: P2)

**Goal**: A labelled three-way theme picker on every page type. The POS theme is controlled by the merchant behind the PIN.

**Independent Test**: Quickstart Q5 steps 4–5.

- [ ] T076 [US5] Create `src/DigiByte.Pay.Web/Components/Shared/Ui/ThemePicker.razor`: a `segmented` control labelled Light, Dark and Match my device with `aria-pressed`, calling `DigiPayTheme.setMode` through JS interop and reflecting the current mode. Delete `src/DigiByte.Pay.Web/Components/Shared/ThemeToggle.razor`.
- [ ] T077 [US5] Place the `ThemePicker`:
  - in `src/DigiByte.Pay.Web/Components/Shared/SiteNav.razor`, in the menu;
  - in the `MerchantLayout` rail and More sheet;
  - in `src/DigiByte.Pay.Web/Components/Pages/Merchant/Settings.razor`;
  - as a compact control in the footer of the checkout, receipt and donation pages;
  - as a compact control on `src/DigiByte.Pay.Web/Components/Pages/Signin.razor`.
- [ ] T078 [US5] Add a "Screen theme" setting to the POS settings in `src/DigiByte.Pay.Web/Components/Pages/Pos.razor`, reachable only after the existing manager PIN check. It writes `localStorage["digipay.pos.theme:v1"]` through a new `setTheme`/`getTheme` pair in `src/DigiByte.Pay.Web/wwwroot/pos-store.js` (values `light | dark | system`; missing means `dark`). Unpairing (`clear`) must keep this key (data-model §2).
- [ ] T079 [US5] Cold-reload every page type in each of the three modes, and while toggling the device setting, to check for a flash of the wrong theme (FR-027). Fix it if the server-rendered `<html>` lacks the class before `theme.js` runs.
- [ ] T080 [US5] Run quickstart Q5 steps 4–5.

---

## Phase 8: User Story 6 - Accessible to keyboard and screen-reader users (Priority: P2)

**Goal**: Skip link, visible focus, proper dialogs and labels, and zero serious or critical axe findings.

**Independent Test**: Quickstart Q5 steps 6–7.

- [ ] T081 [US6] Add `aria-label`s to every icon-only button across `src/DigiByte.Pay.Web/Components/`: copy, close, menu, store switcher, refresh, back and theme (FR-031).
- [ ] T082 [US6] Make sure every interactive element shows the shared `:focus-visible` ring in both themes. Remove any `outline-none` without a replacement in `src/DigiByte.Pay.Web/Components/**/*.razor` and `wwwroot/css/tailwind-input.css`.
- [ ] T083 [US6] Expose selected state on the store switcher, filters, pairing method and ThemePicker (`aria-pressed`, or `aria-selected` in tablists). Make sure every form field has a `<label>` or `aria-labelledby`, and that errors use `aria-invalid` plus `aria-describedby` pointing at `field-error`.
- [ ] T084 [US6] Run the Playwright + axe-core pass from quickstart Q5 step 7, in the official Playwright container. Pages: `/`, `/docs/api`, `/merchant/signin`, every `/merchant/*` section, one session detail, `/pay/{id}` in each state, the receipt, `/pay/button` and `/pos`, in both themes. Save the script as `specs/032-digipay-ui-refresh/a11y/axe-pass.mjs`, then fix every serious and critical finding until there are zero (SC-003).
- [ ] T085 [US6] Do a screen-reader pass (NVDA or VoiceOver): sign-in → Overview → Sessions → session detail, and checkout through payment (quickstart Q5 step 6). Fix anything announced wrongly.

---

## Phase 9: User Story 7 - The public website feels like the wallet's home (Priority: P3)

**Goal**: The public pages use the shared blocks, the phone site menu is a sheet, and code samples have copy buttons. The content is unchanged.

**Independent Test**: Quickstart Q7.

- [ ] T086 [US7] Create `src/DigiByte.Pay.Web/Components/Layout/SiteLayout.razor`, containing `SiteNav` and `SiteFooter`. Apply `@layout SiteLayout` to `Home.razor`, `Features.razor`, `About.razor`, `Docs.razor`, `ApiDocs.razor`, `PosDocs.razor`, `WooCommerceDocs.razor` and `Sdks.razor`, removing their hand-included nav and footer.
- [ ] T087 [US7] Rewrite the phone menu in `src/DigiByte.Pay.Web/Components/Shared/SiteNav.razor`: replace the checkbox hamburger dropdown with a sheet (`card-raised`, `role="dialog"`) that closes with its close button, a tap on the backdrop or Escape, and returns focus to the menu button. The Sign in / Dashboard pill uses `btn-primary btn-sm`.
- [ ] T088 [P] [US7] Restyle `Home.razor` and `Features.razor` with `page-title`, `block-title`, `card`/`card-interactive` and shared buttons. Keep all text, sections and images.
- [ ] T089 [P] [US7] Restyle `About.razor` and `Sdks.razor` the same way. In `Sdks.razor`, the tabbed snippets use `segmented` with `aria-selected`.
- [ ] T090 [P] [US7] Restyle `Docs.razor`, `ApiDocs.razor`, `PosDocs.razor` and `WooCommerceDocs.razor`, plus `Shared/Endpoint.razor`, `Shared/DocsHeading.razor`, `Shared/Feature.razor`, `Shared/FeaturePanel.razor`, `Shared/Step.razor` and `Shared/Stat.razor`.
- [ ] T091 [US7] Give `src/DigiByte.Pay.Web/Components/Shared/CodeBlock.razor` a `CopyButton` (copy and notice), `font-mono` and a `card` surface. Remove its old copy code and the inline "Copied ✓" swap.
- [ ] T092 [US7] Document the `theme` embed option and query parameter (contract C3) in the embed section of `Docs.razor`, in `samples/express-store/README.md`, `samples/flask-store/README.md` and `samples/dotnet-store/README.md` (wherever they show the embed), and as a note in `WooCommerceDocs.razor`.
- [ ] T093 [US7] Run quickstart Q7.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T094 [P] Update `docs/ARCHITECTURE.md`: add the `DigiByte.UI` leaf project, who references it, and that its assets are copied in at build time. Update the dependency description for Principle IV.
- [ ] T095 [P] Add a CHANGELOG entry in `CHANGELOG.md` covering the DigiPay restyle, routed dashboard, theme picker, checkout copy buttons and the embed `theme` option.
- [ ] T096 Check across DigiPay that nothing overflows its button, badge or menu item, or pushes the layout sideways, at 320px (SC-008). Check long store names, labels and txids in particular.
- [ ] T097 Run quickstart Q8: build both Docker images. Check that the DigiPay image has no Node and that `wwwroot/_content/DigiByte.UI/css/ui.css` and the two fonts are in both published outputs.
- [ ] T098 Run the SC-004 and SC-005 hands-on tests before release (quickstart Q9):
  - five people unfamiliar with DigiPay pay a testnet checkout on a phone (copy address, pay, see "received");
  - five merchants find each of five named dashboard sections on desktop and on a phone, timed, and return with the back button.

  Record pass/fail, times and the "looked trustworthy" answers in `specs/032-digipay-ui-refresh/baseline/usability.md`.
- [ ] T099 Run `dotnet build DigiByte.slnx`, `dotnet test` and `node --test` for both web apps. Then run the full quickstart (Q1–Q9) once more and record the results in `specs/032-digipay-ui-refresh/baseline/timings.md`.
- [ ] T100 Write the PR description. It flags the new `DigiByte.UI` project (Principle IV, a leaf with no inversion) and states that the wallet's CSP is unchanged. It lists the sign-in return guard (T055, `ReturnUrlGuard`) as a security-relevant change to sign-in. It also attaches the before/after gallery, checkout and dashboard screenshots required by contract C1's change rule.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T001 MUST run before any code change.
- **Foundational (Phase 2)**: depends on Setup. 2a (T008–T014) comes before 2b (T015–T028). Blocks every user story.
- **US1 (Phase 3)** and **US2 (Phase 4)**: both depend only on Phase 2 and are independent of each other.
- **US3 (Phase 5)**: depends on US2's T051/T052, because it restyles the split Merchant pages. The checkout parts are already done in US1.
- **US4 (Phase 6)**: depends on US2 (Merchant pages and `MerchantState`).
- **US5 (Phase 7)**: depends on Phase 2 (theme.js). Placing the picker in `MerchantLayout` needs US2's T050. Placing it in the site nav doesn't.
- **US6 (Phase 8)**: run after US1–US5, since it audits what they built.
- **US7 (Phase 9)**: depends only on Phase 2. It can run in parallel with US1–US6.
- **Polish (Phase 10)**: after the stories you intend to release.

### Within Each Story

Tests (T029/T030, T045/T046) are written first and fail. Then mapping and helper classes (T031, T047, T048), then pages, then the quickstart check.

### Parallel Opportunities

- Setup: T004–T007 together.
- Foundational 2b: T018, T019 and T023–T028 together, after T017 and T020.
- After Phase 2: US1, US2 and US7 can run at the same time in different files.
- In US1: T029, T030, T031, T032, T038, T039 and T042.
- In US3: T061, T064, T066 and T067.
- In US7: T088, T089 and T090.

---

## Parallel Example: User Story 1

```text
Task: "T029 StatusBadgeTests in tests/DigiByte.Pay.Web.Tests/StatusBadgeTests.cs"
Task: "T030 CheckoutErrorTests in tests/DigiByte.Pay.Web.Tests/CheckoutErrorTests.cs"
Task: "T031 CheckoutStates map in src/DigiByte.Pay.Web/Components/Shared/Ui/CheckoutStates.cs"
Task: "T038 Restyle Receipt.razor"
Task: "T039 Restyle Donate.razor"
Task: "T042 Embed demo examples in wwwroot/embed/demo.html"
```

## Parallel Example: User Story 2

```text
Task: "T045 DashboardRouteTests"
Task: "T046 ReturnUrlGuardTests"
Task: "T047 ReturnUrlGuard.cs"
Task: "T048 DashboardSections.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1: Setup, including the T001 baseline.
2. Phase 2: Foundational. Release 2a on its own: the wallet is unchanged and simply loads `ui.css` from `DigiByte.UI`.
3. Phase 3: US1. **Stop and validate** with quickstart Q2, Q3 and Q6, then release. Every shopper sees the new checkout.

### Incremental Delivery

The order follows plan.md's delivery order. Each step can be released on its own:

1. Shared source and wallet extraction (2a).
2. DigiPay foundation (2b).
3. Checkout (US1).
4. Dashboard navigation (US2).
5. Style layer everywhere (US3).
6. Feedback (US4), theme (US5) and accessibility (US6).
7. Public website (US7).

Convert one page or section per commit, as `docs/UI_STYLE.md` advises, using Conventional Commits (`feat(pay): …`, `refactor(web): …`).

### Parallel Team Strategy

After Phase 2:

- Developer A takes US1 (checkout).
- Developer B takes US2, then US3 and US4 (dashboard).
- Developer C takes US7 (public site), then US5.
- Everyone joins US6 at the end.

---

## Notes

- [P] tasks touch different files and have no unfinished dependencies.
- Keep the wallet pixel-identical. Rerun quickstart Q1 after any later edit to `src/DigiByte.UI/**`.
- Never add Node to `docker/pay-web/Dockerfile`. Commit the rebuilt `tailwind.css` instead.
- No Pay API, data or authentication changes are in scope (FR-036). Stop and revise the spec if a task seems to need one.
