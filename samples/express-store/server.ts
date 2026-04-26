// End-to-end DigiPay sample: a one-product mini-store.
//
// Flow:
//   GET  /                  → browse the product, click "Buy"
//   POST /buy               → create a DigiPay session, redirect to its checkoutUrl
//   POST /digipay-webhook   → verified webhook receiver; flips the in-memory order
//   GET  /orders/:id        → "your order" page that polls until the webhook fires
//
// Deliberately uses an in-memory order store so the sample stays single-file.
// In a real app you'd swap that for Postgres / SQLite / Redis / Stripe-style
// idempotent upserts.
//
// Run with:
//   DIGIPAY_KEY=dgp_…       (a DigiPay API key — Dashboard → API keys)
//   DIGIPAY_SECRET=…        (the store webhook secret — Dashboard → Webhook)
//   PUBLIC_URL=http://localhost:3000   (your URL the customer will see)
//   npm install && npm run dev

import crypto from 'node:crypto';
import express, { type Request, type Response } from 'express';
import {
    DigiPayClient,
    DigiPayError,
    verifyWebhook,
    type WebhookEvent,
} from '@dgbwallet/digipay';

const KEY = process.env.DIGIPAY_KEY ?? required('DIGIPAY_KEY');
const SECRET = process.env.DIGIPAY_SECRET ?? required('DIGIPAY_SECRET');
const PUBLIC_URL = (process.env.PUBLIC_URL ?? 'http://localhost:3000').replace(/\/+$/, '');
const PORT = Number(process.env.PORT ?? 3000);

const digipay = new DigiPayClient(KEY);

// One product. In a real store this would come from a DB.
const PRODUCT = { id: 'sku-tee', name: 'DigiByte tee', price: 12.5 }; // 12.5 DGB

// Order state by id. status: 'pending' | 'paid' | 'confirmed' | 'expired' | 'underpaid'.
const orders = new Map<string, { sessionId: string; status: string; txid?: string }>();

const app = express();
app.use(express.urlencoded({ extended: false }));

app.get('/', (_, res) =>
    res.send(layout(`
        <h1>${PRODUCT.name}</h1>
        <p class="price">${PRODUCT.price} DGB</p>
        <form method="POST" action="/buy">
            <button type="submit">Buy with DigiByte</button>
        </form>
    `)),
);

app.post('/buy', async (req: Request, res: Response) => {
    try {
        // Idempotency key: clients echo this back on a retry (double-click,
        // crashed-then-restarted browser, etc.) and DigiPay returns the
        // original session instead of minting a new one. The header on `/buy`
        // would normally be `Idempotency-Key`; for the sample we mint one
        // ourselves keyed off any client-provided id, falling back to a uuid.
        const clientKey = (req.headers['idempotency-key'] as string | undefined)
            ?? `buy-${PRODUCT.id}-${crypto.randomUUID()}`;
        const session = await digipay.sessions.create(
            {
                amount: PRODUCT.price,
                label: PRODUCT.name,
                memo: `sku=${PRODUCT.id}`,
            },
            { idempotencyKey: clientKey },
        );
        // Track our local order keyed by sessionId — the webhook will look it up.
        orders.set(session.id, { sessionId: session.id, status: 'pending' });
        // Redirect the customer to DigiPay's hosted checkout — they pay there.
        res.redirect(session.checkoutUrl);
    } catch (err) {
        const status = err instanceof DigiPayError ? err.status : 500;
        res.status(status).send(`DigiPay rejected the session: ${(err as Error).message}`);
    }
});

// Customer lands here from `/buy?return_url=` if you wire it up; for this sample
// we just give them a polling page they can reach by id. In production you'd
// pass `return_url` to the session and DigiPay would bounce them back here.
app.get('/orders/:id', (req: Request, res: Response) => {
    const order = orders.get(req.params.id);
    if (!order) return res.status(404).send('order not found');
    res.send(layout(`
        <h1>Order ${order.sessionId.slice(-8)}</h1>
        <p>Status: <b id="status">${order.status}</b></p>
        ${order.txid ? `<p>Tx: <code>${order.txid}</code></p>` : ''}
        <script>
            // Poll the local order endpoint until terminal — saves wiring SignalR.
            const id = ${JSON.stringify(order.sessionId)};
            setInterval(async () => {
                const r = await fetch('/orders/' + id + '.json');
                const j = await r.json();
                document.getElementById('status').textContent = j.status;
                if (['paid','confirmed','expired','underpaid'].includes(j.status)) location.reload();
            }, 2000);
        </script>
    `));
});

app.get('/orders/:id.json', (req, res) => {
    const order = orders.get(req.params.id);
    if (!order) return res.status(404).end();
    res.json(order);
});

// CRITICAL: webhook needs the *raw bytes*, not parsed JSON, because the HMAC
// covers the bytes as the server sent them. `express.json()` would normalise
// whitespace and break verification. Mount the raw body parser only on this
// route so the rest of the app can use the urlencoded parser above.
app.post(
    '/digipay-webhook',
    express.raw({ type: 'application/json' }),
    (req: Request, res: Response) => {
        let event: WebhookEvent;
        try {
            event = verifyWebhook({
                rawBody: req.body,
                signature: req.headers['x-digipay-signature'] as string | undefined,
                secret: SECRET,
            });
        } catch (err) {
            // 401 on bad signature, 400 on malformed body. Don't echo the reason —
            // a curious attacker shouldn't get a free oracle.
            const status = err instanceof DigiPayError ? err.status : 500;
            return res.status(status).end();
        }

        const order = orders.get(event.session.id);
        if (!order) {
            // Webhook for an order we don't know — ack to stop retries, log for debugging.
            console.warn(`webhook for unknown session ${event.session.id}`);
            return res.status(200).end();
        }

        // Map DigiPay event names to your local status. Unknown events get a 200
        // ack so DigiPay doesn't retry forward-compatible events forever.
        switch (event.event) {
            case 'session.paid':
            case 'session.confirmed':
                order.status = event.event.split('.')[1];
                order.txid = event.session.paidTxid ?? undefined;
                console.log(`✅ ${order.status}: ${event.session.amount} DGB for ${order.sessionId}`);
                break;
            case 'session.expired':
            case 'session.underpaid':
                order.status = event.event.split('.')[1];
                console.log(`❌ ${order.status}: ${order.sessionId}`);
                break;
        }

        res.status(200).end();
    },
);

app.listen(PORT, () => {
    console.log(`mini-store on ${PUBLIC_URL}`);
    console.log('Webhook URL to register on the DigiPay store:');
    console.log(`  ${PUBLIC_URL}/digipay-webhook`);
});

function required(name: string): never {
    throw new Error(`Set ${name} (see README.md)`);
}

function layout(body: string): string {
    return `<!doctype html>
<meta charset="utf-8">
<title>DigiByte tee — sample store</title>
<style>
    body { font-family: system-ui, sans-serif; max-width: 32rem; margin: 4rem auto; padding: 0 1rem; }
    h1 { font-size: 1.5rem; }
    .price { font-size: 2rem; font-weight: bold; color: #0062cc; }
    button { padding: .75rem 1.25rem; background: #0062cc; color: white; border: 0; border-radius: .5rem;
             font-size: 1rem; font-weight: bold; cursor: pointer; }
    code { background: #f3f4f6; padding: .125rem .375rem; border-radius: .25rem; font-size: .875rem; }
</style>
${body}`;
}
