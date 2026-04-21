import { createHmac, timingSafeEqual } from 'node:crypto';
import { DigiPayError } from './errors.js';
import type { WebhookEvent } from './types.js';

export interface VerifyWebhookOptions {
    /**
     * Raw request body, exactly as DigiPay sent it. Must be the un-parsed
     * bytes — every reserialization changes the signature. In Express,
     * mount `express.raw({ type: 'application/json' })` on the route.
     */
    rawBody: Buffer | string;
    /** Value of the `X-DigiPay-Signature` request header. */
    signature: string | undefined | null;
    /**
     * The webhook secret you got from `POST /v1/pay/merchants` (or rotated
     * via the dashboard). Treat it like a password.
     */
    secret: string;
}

/**
 * Verify the HMAC signature on an incoming DigiPay webhook and return the
 * parsed event. Throws `DigiPayError` (status 401) on missing / mismatched
 * signature so an Express handler can pass the error straight to the
 * default error pipeline.
 *
 *     app.post('/webhook',
 *       express.raw({ type: 'application/json' }),
 *       (req, res) => {
 *         try {
 *           const event = verifyWebhook({
 *             rawBody: req.body,
 *             signature: req.headers['x-digipay-signature'],
 *             secret: process.env.DIGIPAY_SECRET,
 *           });
 *           // event.event === 'session.paid' etc.
 *           res.status(200).end();
 *         } catch (err) {
 *           res.status(401).end();
 *         }
 *       });
 */
export function verifyWebhook(options: VerifyWebhookOptions): WebhookEvent {
    const { rawBody, signature, secret } = options;
    if (!signature) {
        throw new DigiPayError('Missing X-DigiPay-Signature header', 401);
    }
    if (!secret) {
        throw new DigiPayError('Webhook secret is required', 400);
    }

    // Header format: "sha256=<hex>". Tolerate the prefix being absent
    // since some proxies strip it.
    const provided = signature.startsWith('sha256=') ? signature.slice(7) : signature;

    const bodyBuf = Buffer.isBuffer(rawBody) ? rawBody : Buffer.from(rawBody, 'utf8');
    const expected = createHmac('sha256', secret).update(bodyBuf).digest('hex');

    // timingSafeEqual throws if the two buffers differ in length, which
    // would itself be a side-channel — guard with the length check first.
    const a = Buffer.from(expected);
    const b = Buffer.from(provided);
    if (a.length !== b.length || !timingSafeEqual(a, b)) {
        throw new DigiPayError('Webhook signature mismatch', 401);
    }

    try {
        return JSON.parse(bodyBuf.toString('utf8')) as WebhookEvent;
    } catch (err) {
        throw new DigiPayError('Webhook body is not valid JSON', 400, err);
    }
}
