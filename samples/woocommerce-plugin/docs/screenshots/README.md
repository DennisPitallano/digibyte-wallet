# Screenshot capture checklist

Eight screenshots cover the full merchant + customer flow. Each is referenced
from the plugin's main [`README.md`](../../README.md) and from
[`docs/customer-flow.md`](../customer-flow.md). The filenames are stable —
drop new captures in place and the docs pick them up automatically.

| # | Filename | What to capture |
|---|---|---|
| 1 | `01-install-upload.png` | WP admin → _Plugins → Add New → Upload Plugin_ with the ZIP file picker open. |
| 2 | `02-settings.png` | _WooCommerce → Settings → Payments → DigiPay (DigiByte)_, settings filled in (api_key/secret blurred). |
| 3 | `03-checkout-radio.png` | Block-based WC checkout with the **DigiByte (DGB)** radio + DigiByte coin icon visible under _Payment options_. |
| 4 | `04-hosted-checkout-qr.png` | DigiPay hosted checkout (`/pay/{id}`) with the QR, address, expiry countdown, and amount. |
| 5 | `05-payment-confirmed.png` | Same hosted page after confirmation: "Payment confirmed" + **Return to merchant** button + countdown. |
| 6 | `06-thankyou.png` | The WooCommerce thank-you page (`/checkout/order-received/...`) the buyer is auto-redirected to. |
| 7 | `07-wc-order-admin.png` | _WooCommerce → Orders_ admin showing the order with status _Completed_ and the DigiByte txid in the order notes. |
| 8 | `08-digipay-dashboard-session.png` | _(pending — needs merchant auth, doesn't fit the automated `capture.mjs` flow.)_ DigiPay dashboard sessions list showing the WC-sourced session with the "↩ <merchant-host>" chip. Capture manually after signing in to the dashboard. |

## Capture tips

- **Resolution**: ~1440×900 viewport keeps file sizes reasonable (300–600 KB each as PNG).
- **Format**: PNG. SVG is fine for diagrams; raster for UI.
- **Privacy**: blur api_key, webhook_secret, and any real customer PII. The
  screenshot of the settings page can show **placeholder** values
  (`dgp_test_…`) — they're more readable than blur boxes.
- **Browser chrome**: omit. Use the browser's "capture full page" or a
  dedicated tool — no URL bar, no dev tools.
- **Theme**: use the light theme so the screenshots stay legible regardless
  of where the README is read.

## Recapture script

Once Chrome is reachable from `claude` (Claude in Chrome extension signed in),
ask me to run:

```
"recapture the WooCommerce plugin screenshots"
```

I'll drive the running stack (`http://localhost:8080`, `http://localhost:5252`,
`http://localhost:5008`) through the flow and re-shoot all eight in order.
