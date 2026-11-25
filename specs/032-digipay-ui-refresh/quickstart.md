# Quickstart: Validating the DigiPay UI Refresh

How to prove the feature works from end to end. It refers to [data-model.md](data-model.md) and the [contracts](contracts/) rather than repeating them.

## Prerequisites

- .NET 10 SDK, plus Node 20 for rebuilding Tailwind locally (`npm install` in `src/DigiByte.Pay.Web` and `src/DigiByte.Web`).
- Local services (from `.claude/launch.json`):

  | Name | URL |
  |---|---|
  | `digipay-api` | http://localhost:5008 |
  | `digipay-web` | http://localhost:5252 |
  | `digibyte-web` | http://localhost:5251 |

- A testnet merchant signed in with Digi-ID, and a second brand-new merchant with no sessions, keys or webhook.
- A screen reader (NVDA or VoiceOver), and Docker for the Playwright + axe pass.

## Build and unit tests

```powershell
dotnet build DigiByte.slnx
```

```powershell
dotnet test tests/DigiByte.Pay.Web.Tests
```

```powershell
node --test "src/DigiByte.Pay.Web/tests/js/**/*.test.mjs"
```

**Expected**: everything passes. The Pay.Web tests cover theme precedence, every dashboard route, the status badge for every checkout state, CopyButton and the return-path guard ([C2](contracts/dashboard-routes.md)).

## Q0. Baseline (before any change; record the results)

On `main`, before stage 1, record:

1. Three cold-load timings of a pending checkout on a throttled "Fast 4G" profile, at a 375px viewport: time until the amount and QR code are visible.
2. Screenshots of the wallet's `/test-animations` gallery in light and dark, at 375px and 1280px.

## Q1. The wallet is unchanged after the extraction (stage 1)

1. Retake the Q0 gallery screenshots and compare pixel for pixel.
   **Expected**: no differences.
2. Load the wallet offline after one online visit.
   **Expected**: fonts and `_content/DigiByte.UI/css/ui.css` come from the service-worker cache.
3. Check the console.
   **Expected**: no CSP violations.

## Q2. Checkout (US1)

Create a session from the dashboard. Open `/pay/{id}` at 375px and at 320px, in light and dark.

| Step | Expected |
|---|---|
| Look at the page | Merchant name, label, amount, framed QR code, **Open in wallet**, the address in the fixed-width font, and the countdown |
| Press the address copy button | Address copied; a notice confirms it |
| Press the amount copy button | Plain DGB amount copied (no symbol or separators) |
| Block clipboard permission and copy | Value is selected; a notice explains how to copy by hand |
| Pay the session (testnet) | Badge goes **Payment detected → Payment received → Confirmed**. The screen reader announces each change ([data-model §5](data-model.md)) |
| Let a session expire | "Price expired", no QR code or address, Back to shop when a return URL exists |
| Underpay a session | Underpaid state with the received and expected amounts |
| Open `/pay/does-not-exist`; stop Pay.Api and reload | Plain "could not be loaded" with Retry. No exception text |
| With the page open, stop Pay.Api for 20 s, then restart it | "Reconnecting…" while it is down. Last state kept, never "expired". Recovers by itself |
| Open `/pay/{id}/receipt` and `/pay/button?to=…` | Same components and states. Receipt prints legibly on white |

No Baku appears on any `/pay/*` page.

## Q3. Shop-set theme (FR-026a, [C3](contracts/checkout-theme.md))

1. Set the device to dark. Open `/pay/{id}?theme=light`.
   **Expected**: the page is light, and `localStorage["dp-theme"]` is unchanged.
2. Open `/embed/demo.html` with `data-theme="light"` on the button.
   **Expected**: the iframe URL carries `theme=light` and the checkout is light.
3. Use `theme=purple`, or leave the parameter out.
   **Expected**: the checkout follows the shopper's choice or the device, as before.
4. Run one sample store (for example `samples/express-store`) unchanged.
   **Expected**: the payment completes exactly as before (SC-010).

## Q4. Dashboard navigation (US2, [C2](contracts/dashboard-routes.md))

1. **Laptop width (1280px)**: the left rail lists every section, the current one is marked, and the store switcher works. Lists and tables use the wide area. Settings and New payment stay narrow.
2. **Phone width (375px)**: the bottom menu shows Overview, Sessions, raised New payment, Payment links and More. Every section is reachable in two taps or fewer.
3. **Bookmarks**: bookmark `/merchant/webhook`, close the tab and reopen it.
   **Expected**: Webhook opens. Signed out, it goes to sign-in and comes back to Webhook.
4. **History**: go Sessions (filter Paid) → API keys → Back.
   **Expected**: Sessions with the Paid filter. Then press Forward.
   **Expected**: API keys.
5. **Session detail**: open a session, then press its back button.
   **Expected**: the same filtered list.
6. **Old bookmark**: `/merchant`.
   **Expected**: Overview.
7. **Bad paths**: `/merchant/nope` and `?store=<another merchant's store>`.
   **Expected**: the not-found page with a link to Overview, and no foreign data.
8. **Return guard**: `/merchant/signin?return=https://evil.example`.
   **Expected**: after sign-in you land on `/merchant`.

## Q5. Style, states, theme and accessibility (US3–US6)

1. **New merchant** (from Prerequisites): every section shows its empty state and next step ([data-model §3](data-model.md)), with Baku only in the dashboard. Overview explains why payments can't be created yet and links to Settings.
2. **Slow network**: throttle to Slow 4G.
   **Expected**: content-shaped placeholders, no "Loading…" text.
3. **Notices**: save Settings, create and revoke an API key, send a test webhook to a failing URL.
   **Expected**: notices on success. A plain message with Retry on failure. Revoke asks for confirmation in a dialog. A failing-webhook banner appears on Overview and Webhook.
4. **Theme picker**: switch Light, Dark and Match my device on the site, dashboard and checkout. Change the device setting while on Match my device. Cold-reload each.
   **Expected**: no flash of the wrong theme.
5. **POS**: pair a tablet.
   **Expected**: it starts dark. Change the theme in POS settings behind the manager PIN, restart, and confirm it is kept. A shopper can't reach the setting.
6. **Keyboard**: Tab from the top of each page type.
   **Expected**: a skip link appears first, the focus outline is always visible, dialogs keep focus inside and close with Escape, and focus returns afterwards.
7. **Automated accessibility pass** (official Playwright container, axe-core): run on `/`, `/docs/api`, `/merchant/signin`, every `/merchant/*` section, a session detail, `/pay/{id}` in each state, the receipt, `/pay/button` and `/pos`, in both themes.
   **Expected**: zero serious or critical findings (SC-003).
8. **Size and contrast**: compare buttons, fields and cards with the wallet's gallery at the same widths.
   **Expected**: same heights (48/44px), 16px field text, and the contrast numbers from `docs/UI_STYLE.md` (SC-001, SC-002).

## Q6. Checkout speed (FR-013a, SC-011)

Repeat the Q0 timing 20 times on the restyled checkout.

**Expected**: the amount and QR code are visible within 2 s in at least 19 of 20 runs, and the median is no worse than the Q0 baseline. If either fails, apply the R8 fallback in [research.md](research.md) (prerendered shopper pages) and measure again.

## Q7. Public website (US7)

1. At 375px and 1280px in both themes, check each public page: Home, Features, How it works (`/docs`), the API, POS and WooCommerce guides, SDKs and About.
   **Expected**: the shared building blocks are used, and the content is unchanged.
2. Open the site menu at 375px.
   **Expected**: a sheet that closes with the close button, a tap outside or Escape.
3. Press every code-sample copy button.
   **Expected**: copied, with a notice.
4. Turn on reduced motion.
   **Expected**: no entrance animations.

## Q8. Release builds

```powershell
docker build -f docker/pay-web/Dockerfile .
```

```powershell
docker build -f docker/web/Dockerfile .
```

**Expected**: both build. The DigiPay image still contains no Node. In both published outputs, `wwwroot/_content/DigiByte.UI/css/ui.css` and the two fonts are present.
