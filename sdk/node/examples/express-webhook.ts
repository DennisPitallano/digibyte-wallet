import express from 'express';
import { verifyWebhook, DigiPayError, type WebhookEvent } from '@dgbwallet/digipay';

// Run with:  DIGIPAY_SECRET=… npx tsx examples/express-webhook.ts

const app = express();

app.post(
    '/digipay-webhook',
    // Crucial: raw bytes, not parsed JSON. The signature covers the bytes.
    express.raw({ type: 'application/json' }),
    (req, res) => {
        let event: WebhookEvent;
        try {
            event = verifyWebhook({
                rawBody: req.body,
                signature: req.headers['x-digipay-signature'] as string | undefined,
                secret: process.env.DIGIPAY_SECRET!,
            });
        } catch (err) {
            // Signature mismatch → 401, malformed body → 400. Don't leak details.
            const status = err instanceof DigiPayError ? err.status : 500;
            return res.status(status).end();
        }

        switch (event.event) {
            case 'session.paid':
                console.log(`💰 ${event.session.amount} DGB received for ${event.session.label}`);
                // → mark order as paid, fulfil, send receipt, etc.
                break;
            case 'session.confirmed':
                console.log(`✅ Confirmed: ${event.session.id}`);
                break;
            case 'session.expired':
            case 'session.underpaid':
                console.log(`❌ ${event.event}: ${event.session.id}`);
                break;
            default:
                // Unknown event types: ack 200 and ignore — DigiPay adds events
                // over time and 4xx/5xx responses trigger delivery failures.
                break;
        }

        res.status(200).end();
    },
);

app.listen(3000, () => console.log('listening on :3000'));
