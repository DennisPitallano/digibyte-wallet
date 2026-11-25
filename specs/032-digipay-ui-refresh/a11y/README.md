# Accessibility scan and throttled checkout timing (T084, T044)

Run on 2026-11-25 with `axe-pass.mjs` in `mcr.microsoft.com/playwright:v1.48.0-jammy`, against DigiPay on the host.

- **Scope:** 28 page types (public site, sign-in, tip page, checkout and receipt, POS, every dashboard section), each in light and dark.
- **Rules:** axe-core 4.10, WCAG 2.0/2.1 A and AA tags.

## Accessibility

**Result: 0 serious or critical findings in all 56 scans (SC-003).** Full results: [axe-results.json](axe-results.json).

The first run ([axe-results-run1.json](axe-results-run1.json)) found these, all now fixed:

| Finding | Where | Fix |
|---|---|---|
| `aria-prohibited-attr` | Every page | The notice container had `aria-label` without a role. It now has `role="region"`. |
| `color-contrast` | Mostly public pages | DigiByte blue (#0066cc) as text on navy in dark mode; small slate, red, emerald, indigo and amber text; the Telegram pill. Remapped in `digipay.css` to shades that reach 4.5:1 in each theme. |
| `link-in-text-block` | Docs, About, SDKs | Links in running text told apart by colour only. They're now underlined. |
| `scrollable-region-focusable` | Code samples | Scrollable code samples are now focusable (`tabindex="0"`) and named. |

## Checkout speed on Fast 4G (FR-013a, SC-011)

**Setup:** Fast 4G: 9 Mbps down, 1.5 Mbps up, 150 ms latency; 375px; cache disabled. 20 runs, each from a fresh browser context.

**Result: median 1018 ms, max 2924 ms; 19/20 runs within 2 s.** This meets "within 2 seconds in at least 95% of test runs".

An earlier run gave a median of 1047 ms, a max of 1085 ms, and 20/20 within 2 s.
