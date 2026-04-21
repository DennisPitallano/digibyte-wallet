# @dgbwallet/digipay

Official Node.js SDK for **[DigiPay](https://pay.dgbwallet.app)** — accept DigiByte payments on your site without holding any funds.

- **Non-custodial.** Payments land directly in your wallet (single address or BIP84 xpub).
- **Zero runtime dependencies.** Native `fetch` + `node:crypto`. ~5 KB published.
- **TypeScript first.** Strict types for every endpoint + the webhook payload.
- **Node 18+.**

```bash
npm install @dgbwallet/digipay
```

## Quickstart

```ts
import { DigiPay } from '@dgbwallet/digipay';

const dp = new DigiPay({ apiKey: process.env.DIGIPAY_KEY! });

const session = await dp.sessions.create({
  amount: 5,
  label: 'Order #1234',
});

console.log(session.checkoutUrl); // → https://pay.dgbwallet.app/pay/ses_…
```

## Self-serve registration

If you don't have an API key yet, register a brand-new merchant + first store + initial key in a single call:

```ts
import { DigiPay } from '@dgbwallet/digipay';

const merchant = await DigiPay.register({
  displayName: 'My Shop',
  addressOrXpub: 'dgb1q…', // or a BIP84 xpub
  webhookUrl: 'https://my-shop.example/digipay-webhook',
});

console.log(merchant.apiKey);        // dgp_… (shown once)
console.log(merchant.webhookSecret); // store this for verifyWebhook
```

## Webhook verification

DigiPay POSTs signed JSON to your `webhookUrl` on every state change. The signature is HMAC-SHA256 of the raw body, hex-encoded, in the `X-DigiPay-Signature` header (prefixed `sha256=`).

```ts
import express from 'express';
import { verifyWebhook, DigiPayError } from '@dgbwallet/digipay';

const app = express();

app.post('/digipay-webhook',
  express.raw({ type: 'application/json' }), // raw body — required
  (req, res) => {
    try {
      const event = verifyWebhook({
        rawBody: req.body,
        signature: req.headers['x-digipay-signature'] as string,
        secret: process.env.DIGIPAY_SECRET!,
      });
      // event.event === 'session.paid' | 'session.confirmed' | …
      // event.session.id, .amount, .paidTxid, etc.
      console.log(`${event.event} for ${event.session.id}`);
      res.status(200).end();
    } catch (err) {
      if (err instanceof DigiPayError) return res.status(err.status).end();
      throw err;
    }
  });
```

**Critical:** verify the signature against the **raw bytes** before parsing JSON. Re-serializing breaks the HMAC.

## Resources

### Sessions

```ts
await dp.sessions.create({ amount: 5, label, memo, fiatCurrency, fiatAmount });
await dp.sessions.get('ses_abc');
await dp.sessions.list({ status: 'paid', take: 50 });
await dp.sessions.exportCsv({ status: 'paid' }); // returns CSV text
```

### Stores

```ts
await dp.stores.list();
await dp.stores.get('sto_abc');
await dp.stores.create({ name: 'Side hustle', network: 'mainnet' });
await dp.stores.update('sto_abc', { webhookUrl: '…', defaultSessionExpiryMinutes: 30 });
await dp.stores.delete('sto_abc');

// Webhook tooling
await dp.stores.sendTestWebhook('sto_abc');
await dp.stores.listDeliveries('sto_abc', { take: 100 });
await dp.stores.replayDelivery('sto_abc', 'wdel_…');
await dp.stores.exportDeliveriesCsv('sto_abc');
```

## Errors

Every failure raises a `DigiPayError` with the HTTP status preserved:

```ts
import { DigiPayError } from '@dgbwallet/digipay';

try {
  await dp.sessions.create({ amount: 0 });
} catch (err) {
  if (err instanceof DigiPayError) {
    console.log(err.status); // 400
    console.log(err.body);   // { error: 'amount (DGB) must be > 0' }
  }
}
```

| `err.status` | Meaning |
|---|---|
| `0` | Network / DNS / TLS / timeout |
| `400` | Validation failure — see `err.body.error` |
| `401` | Missing or invalid API key |
| `404` | Resource not found, or not owned by this merchant |
| `429` | Rate-limited (sandbox endpoints only) |
| `>= 500` | Server-side; safe to retry with backoff |

## Configuration

```ts
new DigiPay({
  apiKey: 'dgp_…',
  baseUrl: 'https://api.pay.dgbwallet.app', // default
  timeoutMs: 15_000,                         // default
});
```

For staging or self-hosted, pass the alternate `baseUrl`.

## License

MIT — see [LICENSE](../../LICENSE).
