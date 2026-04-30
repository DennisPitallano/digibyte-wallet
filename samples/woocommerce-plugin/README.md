# DigiPay × WooCommerce — payment gateway plugin

Accept DigiByte (DGB) payments in WooCommerce via DigiPay's hosted checkout.
Non-custodial — payments land directly in your wallet. HMAC-signed webhooks,
idempotent session creation, multi-currency-priced orders.

This is a **v0.1.0 hosted-checkout MVP**: the customer is redirected to
DigiPay's hosted checkout to pay. Embedded checkout (QR + status on the WC
thank-you page) is on the v2 roadmap.

## What it shows

- A `WC_Payment_Gateway` subclass that creates a DigiPay session at
  `process_payment()` time and redirects to `session.checkoutUrl`.
- A `wc-api` webhook receiver with **raw-body HMAC verification** plus a
  5-minute timestamp tolerance, so a leaked secret can't be used to replay
  an old delivery indefinitely.
- Stripe-style `Idempotency-Key` derived from the WC order id, so a
  double-clicked _Place order_ can't mint two sessions.
- A WooCommerce **Blocks payment-method registration** so the gateway shows
  on the new (default since WC 8.3) block-based checkout, not just the
  classic shortcode checkout.
- **Fiat-priced orders** automatically converted to DGB at checkout time
  using a CoinGecko price feed (60s WP-transient cached).
- A merchant **`returnUrl`** sent on every session — the hosted checkout
  routes the buyer back to the WC thank-you page after payment confirms.
- WooCommerce HPOS (custom-order-tables) compatibility declared.

## Requirements

| | |
|---|---|
| WordPress | 6.0+ |
| PHP | 7.4+ |
| WooCommerce | 7.0+ (8.3+ for block-based checkout — older versions still work via the classic shortcode checkout) |
| Outbound HTTPS | required for fiat mode (CoinGecko) and `returnUrl` validation |
| WC store currency (fiat mode) | one of: USD, EUR, GBP, PHP, JPY |

## Install

1. Build the plugin ZIP:

   ```bash
   cd samples
   zip -r digipay-for-woocommerce-0.1.0.zip woocommerce-plugin
   ```

   Or download the prebuilt ZIP from
   [GitHub releases](https://github.com/DennisPitallano/digibyte-wallet/releases).

2. In WordPress admin → _Plugins → Add New → Upload Plugin_, upload the ZIP and
   activate.

3. Go to _WooCommerce → Settings → Payments → DigiPay (DigiByte)_ and
   configure.

## Configure

You'll need two values from your DigiPay dashboard:

1. **API key** — _API keys_ tab → create a key starting with `dgp_…`.
2. **Webhook secret** — _Webhook_ tab → set the URL to the value the plugin
   shows on the settings page (looks like
   `https://your-shop.example/?wc-api=digipay_webhook`) and copy the secret it
   generates.

Other settings:

| Setting | Default | Notes |
|---|---|---|
| API base URL | `https://pay.dgbwallet.app` | Override only for self-hosted DigiPay or regtest. |
| Currency mode | `Fiat (recommended)` | Plugin fetches DGB/<store-currency> from CoinGecko (cached 60s) and sends `amount` + `fiatAmount` + `fiatCurrency` + `dgbPriceAtCreation` together. Supported store currencies: **USD, EUR, GBP, PHP, JPY**. Switch to `DGB` if the WC store currency itself is DigiByte. |
| Session expiry (seconds) | _(empty — uses DigiPay default of 1800s / 30m)_ | Match your fulfilment SLA. |
| Debug logging | off | When on, session creation + webhook events are written to _WooCommerce → Status → Logs_ under source `digipay`. |

The gateway is **automatically hidden at checkout** when:
- the API key or webhook secret is blank, or
- the store currency isn't supported (fiat mode only).

This avoids the worst-case UX of a buyer clicking _Place order_ and only then
finding out the gateway is misconfigured.

### Return URL

The plugin always sends `returnUrl = <WC thank-you page>` on session create —
no admin field. After the buyer confirms payment on DigiPay's hosted
checkout, they see a "Return to merchant" button and a 5-second
auto-redirect back to the WC thank-you page (`/checkout/order-received/{id}/...`).
The URL is order-specific and includes the WC order key, so each buyer is
sent to their own page.

## Test locally on regtest

This is the same flow [described in the verification section of the
plan](../../) — useful when you're hacking on the plugin.

1. Bring up the regtest stack from the repo root:

   ```bash
   docker compose up
   ```

   That launches a regtest DigiByte node, `DigiByte.Pay.Api`, and
   `DigiByte.Pay.Web`.

2. Run a disposable WordPress + MariaDB next door:

   ```bash
   docker run -d --name digipay-wc-db -e MYSQL_ROOT_PASSWORD=rootpw \
     -e MYSQL_DATABASE=wp -e MYSQL_USER=wp -e MYSQL_PASSWORD=wp mariadb:11

   docker run -d --name digipay-wc -p 8080:80 --link digipay-wc-db:db \
     -e WORDPRESS_DB_HOST=db -e WORDPRESS_DB_USER=wp \
     -e WORDPRESS_DB_PASSWORD=wp -e WORDPRESS_DB_NAME=wp \
     wordpress:php8.2-apache
   ```

   Visit <http://localhost:8080>, finish the WP install, install WooCommerce,
   then upload `digipay-for-woocommerce-0.1.0.zip`.

3. Register a DigiPay merchant in the dashboard (Pay.Web), copy the API key +
   webhook secret into the plugin settings. Set the **API base URL** to your
   local Pay.Api (e.g. `http://host.docker.internal:5000`) and the **webhook
   URL** on the dashboard side to
   `http://host.docker.internal:8080/?wc-api=digipay_webhook`. HTTP is fine on
   regtest — the `WebhookDispatcher` exempts localhost from the HTTPS
   requirement.

4. Place a test order. Pay from the regtest wallet, mine a block, and watch the
   WC order flip `pending → processing → completed` with the txid recorded as
   an order note.

### Edge cases worth exercising before tagging a release

- Underpay (send half) → the order goes to `on-hold` with an "underpaid;
  manual review" note.
- Let the session expire without paying → order goes to `failed`.
- Replay the same webhook twice (`curl` with the captured body + signature) →
  the second delivery is a no-op + `200 ok` (per-delivery-id de-dupe).
- Tamper one byte of the body → `401`.
- Strip the `X-DigiPay-Signature` header → `401`.
- Double-click _Place order_ → both clicks resolve to the same DigiPay session
  id (via `Idempotency-Key: wc_order_{id}`).

## Offline smoke test

The plugin ships a tiny PHP script that exercises the HMAC verification path
against pre-captured fixtures — no WP, no DigiPay, no network. Useful in CI:

```bash
php tests/verify.php
```

Expected output:

```
  ✓ session.paid: valid signature must verify
  ✓ session.paid: tampered body must NOT verify
  …
ok — all verification cases passed.
```

If you change [`includes/class-digipay-webhook.php`](includes/class-digipay-webhook.php),
re-run this before opening a PR.

## Files

- `digipay-for-woocommerce.php` — plugin header + bootstrap (Woo guard, HPOS
  declaration, gateway registration, webhook route).
- `includes/class-digipay-gateway.php` — `WC_Payment_Gateway` subclass with the
  settings UI and `process_payment()`.
- `includes/class-digipay-client.php` — minimal `wp_remote_post` wrapper for
  `POST /v1/pay/sessions`.
- `includes/class-digipay-webhook.php` — raw-body HMAC verifier + event-to-WC
  state mapping.
- `includes/class-digipay-logger.php` — gated wrapper over `wc_get_logger()`.
- `assets/icon.svg` — gateway icon shown next to the title at checkout.
- `tests/verify.php` — offline smoke test.
- `tests/webhook-fixtures/` — captured webhook bodies + signatures.

## Production-ready hardening (left out for clarity)

- An admin meta-box on the order screen showing the live DigiPay session
  status, with a "View on DigiPay" link.
- Refund button in the WC admin — DigiPay refunds are **non-custodial**, so the
  helper would generate a `digibyte:` URI for the merchant to sign in their
  wallet, rather than auto-spend. (See the "Refund helper" entry on the DigiPay
  roadmap.)
- A scheduled reconciliation job that polls DigiPay for any session whose
  webhook delivery may have been lost (the webhook dispatcher already retries,
  but a belt-and-braces sweep is cheap insurance).
- Multi-store dashboards — pick which DigiPay store to bill into per WC store
  in a multisite install.
- Subscriptions / WooCommerce Subscriptions integration (on the Q4 2026+
  roadmap as a separate plugin).

## License

GPL-2.0-or-later — same as WooCommerce, so it can co-exist on a Woo install
without licence-clash drama.
