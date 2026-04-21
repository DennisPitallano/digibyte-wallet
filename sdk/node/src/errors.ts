/**
 * All failures the SDK can raise come through this single class. The
 * underlying HTTP status is preserved so callers can branch on it without
 * string-matching on the message.
 *
 *   - `status === 0`      — network / DNS / TLS / timeout
 *   - `status === 401`    — missing or bad bearer token
 *   - `status === 400`    — validation failure (body.error has details)
 *   - `status === 404`    — resource not found / not owned by this merchant
 *   - `status === 429`    — rate-limited (sandbox endpoints only)
 *   - `status >= 500`     — server-side issue; safe to retry with backoff
 *
 * For the webhook verification helper we reuse the same class — it throws
 * with `status: 401` on signature mismatch so the HTTP handler can bubble
 * it straight up.
 */
export class DigiPayError extends Error {
    public readonly status: number;
    public readonly body: unknown;

    constructor(message: string, status = 0, body?: unknown) {
        super(message);
        this.name = 'DigiPayError';
        this.status = status;
        this.body = body;
    }
}
