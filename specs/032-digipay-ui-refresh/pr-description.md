# feat(pay): DigiPay website, checkout and dashboard follow the wallet's look and feel

Spec: `specs/032-digipay-ui-refresh` (spec, plan, contracts, tasks).

## What changes for people

- **Shoppers.** The checkout, receipt and tip page use the wallet's components. There are copy buttons for the address, the amount and the transaction ID. Plain-language status badges are announced to screen readers. No QR code or address is shown once a payment is final. Failures show plain messages instead of technical errors. The amount and QR code arrive in the first page load.
- **Merchants.**
  - Every dashboard section has its own address, so bookmarks, Back and Forward work.
  - Wide screens get a left rail; phones get a bottom menu with a raised **New payment** button and a More sheet.
  - Sign-in returns you to the page you were opening.
  - Destructive actions ask first.
  - Saves and copies confirm with a notice.
  - Loading shows placeholders, and empty sections explain what goes there, with Baku in the dashboard only.
  - Overview warns when webhook deliveries fail.
- **Everyone.** Light, Dark or Match my device on every page. DigiPay defaults to Match my device; the POS tablet starts dark and its theme is set behind the manager PIN.
- **Fix: live updates now work.** The checkout only tried to connect for live updates on its first render, which happens before the payment loads, so it never connected and shoppers had to reload. It now updates on its own, shows "Reconnecting" when the connection drops, and keeps checking until the server reports a final state.

## Security-relevant (please review)

- **New project, `src/DigiByte.UI`** (constitution Principle IV). It is a static-asset Razor Class Library with no project references, so it's a leaf and inverts no existing dependency. `DigiByte.Web` and `DigiByte.Pay.Web` reference it and copy it in at build time.
- **The wallet's CSP is unchanged.** The shared CSS and fonts are served from each app's own origin at `_content/DigiByte.UI/...` (`style-src 'self'`, `font-src 'self'`).
- **Sign-in return guard (T055).** `/merchant/signin?return=` goes through `ReturnUrlGuard`, which only allows local `/merchant/...` paths. Absolute, protocol-relative, backslash, encoded and `javascript:` values fall back to `/merchant`. Unit-tested in `ReturnUrlGuardTests`.
- **Store in the URL.** A `?store=` that isn't the merchant's shows "not found" and requests nothing for it.
- **No secrets in URLs.** The new `?theme=` parameter carries no data and is never stored.

## Evidence

- **The wallet looks the same.** A computed-style fingerprint of `/test-animations` is identical before and after the extraction, at 375px and 1280px, in light and dark (`baseline/gallery-fingerprint.json`).
- **Checkout speed.** Before the restyle, the amount appeared after a median of 76 ms. Now it is in the HTML at about 20 ms and can paint at about 50 ms, after the CSS loads (`baseline/timings.md`). These runs were local and unthrottled.
- **Tests:**
  - `tests/DigiByte.Pay.Web.Tests` (xUnit + bUnit) covers the theme rules, notices, copy button, dialog, status badges, checkout errors and rendering, live-update start, dashboard routes, and the return guard;
  - `node --test` covers `theme.js`.
- **Screenshots to attach:** the wallet style gallery before and after; the checkout (waiting, underpaid, tip page at 320px dark); the dashboard (rail, bottom menu, empty state, store not set up); the POS in light.

## Not in this PR (tracked in tasks.md)

- **Throttled Fast 4G timing and the axe accessibility scan.** Both need the Playwright container image.
- **Docker image builds.** These need the .NET SDK base image pulled.
- **Screen-reader pass and the five-shopper / five-merchant usability tests.** These need people.
- **Splitting `Dashboard.razor` into per-section files.** Maintainability only; nothing users see changes.
- **Pre-existing, not changed here:** NuGet advisory on `Microsoft.AspNetCore.SignalR.Common` 10.0.5 (high).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
