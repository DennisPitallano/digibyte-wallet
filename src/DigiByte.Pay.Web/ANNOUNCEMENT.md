# DigiPay — WooCommerce plugin + merchant round-trip

> Release date: April 2026
> Live at: https://pay.dgbwallet.app
> Plugin docs: https://pay.dgbwallet.app/docs/woocommerce
> SDKs + integrations: https://pay.dgbwallet.app/sdks
> Roadmap: https://pay.dgbwallet.app/features

---

## Short post (Telegram / Twitter / Nostr, ~280 chars)

🟦 DigiPay now plugs into **WooCommerce** — the WordPress backend behind ~40% of e-commerce.

Drop in the plugin, paste an API key, accept DGB. Block-checkout integration, live fiat→DGB conversion, HMAC-signed webhooks, auto-redirect back to your thank-you page.

→ https://pay.dgbwallet.app/docs/woocommerce

---

## Medium post (Reddit / forums / blog intro, ~160 words)

**DigiPay — first platform plugin: WooCommerce v0.1.0, live at pay.dgbwallet.app/docs/woocommerce**

The previous release was about getting devs to a working integration in minutes. This one is about reaching merchants who don't write code at all — the ~40% of e-commerce stores that run on WordPress + WooCommerce just got DGB as a one-click payment option.

What's in:

- **Drop-in WC plugin.** Upload the ZIP, paste an API key + webhook secret, tick Enable. Done. Hides itself at checkout until configured so buyers never hit a half-broken state.
- **Block-checkout integration.** Works with Woo's new Cart & Checkout Blocks (default since WC 8.3), not just the legacy shortcode.
- **Fiat-priced orders.** Plugin fetches live DGB price from CoinGecko (cached 60s) and pins the DGB amount at session creation — your store stays priced in USD/EUR/GBP/PHP/JPY.
- **Round-trip back to your shop.** New `returnUrl` field on session creation routes the buyer back to your WC thank-you page after payment confirms (5-second auto-redirect, button always clickable). Available to SDK + direct-API merchants too.
- **Replay-protected webhooks.** Raw-body HMAC verification + Stripe-shaped 5-min timestamp tolerance.

Step-by-step install with screenshots: **pay.dgbwallet.app/docs/woocommerce**.

---

## Long post (merchant newsletter, ~400 words)

### From "any developer can integrate" to "any shop owner can install"

The previous release was for developers — sample apps, Postman, idempotency. This one is for the merchants who can't (or shouldn't have to) edit `webhook_handler.py`. WooCommerce is the dominant WordPress e-commerce stack — by most counts it powers ~40% of online stores — and DigiPay v0.1.0 ships there as a drop-in plugin.

**The plugin.**
Upload `digipay-for-woocommerce-0.1.0.zip` from the [GitHub release](https://github.com/DennisPitallano/digibyte-wallet/releases), activate it, paste in an API key + webhook secret from the DigiPay dashboard, save. The gateway shows up at checkout as **DigiByte (DGB)** with the official coin mark. The whole install is ~10 minutes the first time, ~2 minutes thereafter, and the [on-site install guide](https://pay.dgbwallet.app/docs/woocommerce) walks every step with screenshots. Block-checkout merchants (default since WC 8.3) get a first-class payment-method registration; classic-shortcode stores keep working too.

**Fiat-priced orders.**
Most WC stores price in USD, EUR, GBP, PHP, or JPY — not DGB. The plugin fetches the live DGB rate from CoinGecko (cached 60s in a WP transient so multiple sessions per minute don't hammer the upstream), converts the order total to DGB, and sends `amount` + `fiatAmount` + `fiatCurrency` + `dgbPriceAtCreation` together. The hosted checkout's volatility banner picks up the quote-time price so a buyer who lingers sees the right warning.

**Closing the round-trip.**
A buyer who paid on DigiPay's hosted checkout used to get stuck on the "Payment confirmed" screen with no path home. New `returnUrl` field on `POST /v1/pay/sessions` (validated, persisted, echoed in webhooks) lets merchants pass the destination URL; the hosted checkout shows a **Return to merchant** button + 5-second auto-redirect once the session reaches `confirmed`. The WC plugin sends the order's thank-you URL automatically — SDK and direct-API merchants get the same round-trip by passing the field themselves.

**Replay protection.**
Webhook receivers now have a Stripe-shaped 5-minute freshness window — a leaked secret can't be used to replay an old captured delivery indefinitely. The plugin enforces it; the SDKs leave it to the application but the docs show the pattern.

**Auto-hide when misconfigured.**
The gateway refuses to render at checkout when the API key, webhook secret, or store currency is missing, so buyers never click _Place Order_ and only then discover the integration is half-set-up.

**What's next.** Email receipts on `session.confirmed`, the first xUnit test suite for `Pay.Api`, a refund helper that generates a `digibyte:` URI for the merchant to sign in their own wallet (DigiPay never holds funds), and the WordPress.org plugin directory submission once we've gathered field reports from the early installers. Roadmap at **pay.dgbwallet.app/features**.

DigiPay is open source, non-custodial, and lives at [github.com/DennisPitallano/digibyte-wallet](https://github.com/DennisPitallano/digibyte-wallet). If WooCommerce isn't your stack, the existing Express / Flask / ASP.NET mini-stores show the same pattern in ~150 lines.

— Built by [Dennis Pitallano](https://dennispitallano.github.io) and contributors.
