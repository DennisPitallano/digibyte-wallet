import { DigiPayError } from './errors.js';

export interface HttpClientOptions {
    apiKey: string;
    baseUrl: string;
    /** Forwarded as the User-Agent so server logs identify SDK traffic. */
    userAgent: string;
    /** Per-request timeout in ms. Defaults to 15s. */
    timeoutMs: number;
}

/** Per-request overrides — currently just idempotency support, but typed
 * as an object so future per-request options (idempotency window, debug
 * trace, etc.) don't widen the call signature. */
export interface RequestOpts {
    /** Sent as the `Idempotency-Key` header. The server scopes it per-merchant
     * and replays the original response for 24h. Up to 255 chars. */
    idempotencyKey?: string;
}

/**
 * Thin wrapper around the global `fetch` (Node 18+) that adds:
 *   • Bearer auth header
 *   • Per-request timeout via AbortController
 *   • DigiPayError mapping — non-2xx responses surface as a typed error
 *     with the HTTP status preserved on `err.status` so callers don't
 *     have to string-match on `err.message`.
 *
 * Returns the parsed JSON body. Use `requestRaw` for endpoints that
 * stream non-JSON content (e.g. CSV exports).
 */
export class HttpClient {
    constructor(private readonly opts: HttpClientOptions) { }

    async request<T>(method: string, path: string, body?: unknown, opts: RequestOpts = {}): Promise<T> {
        const res = await this.requestRaw(method, path, body, opts);
        const text = await res.text();
        if (!text) return undefined as T;
        try {
            return JSON.parse(text) as T;
        } catch {
            throw new DigiPayError(`Non-JSON response from ${path}`, res.status, text);
        }
    }

    /** For binary/text endpoints (CSV) — returns the raw Response. */
    async requestRaw(method: string, path: string, body?: unknown, opts: RequestOpts = {}): Promise<Response> {
        const url = new URL(path, this.opts.baseUrl).toString();
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), this.opts.timeoutMs);

        let res: Response;
        try {
            res = await fetch(url, {
                method,
                headers: {
                    'Authorization': `Bearer ${this.opts.apiKey}`,
                    'User-Agent': this.opts.userAgent,
                    ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
                    ...(opts.idempotencyKey ? { 'Idempotency-Key': opts.idempotencyKey } : {}),
                },
                body: body !== undefined ? JSON.stringify(body) : undefined,
                signal: controller.signal,
            });
        } catch (err) {
            if (err instanceof Error && err.name === 'AbortError') {
                throw new DigiPayError(`Request to ${path} timed out after ${this.opts.timeoutMs}ms`, 0);
            }
            throw new DigiPayError(`Network error contacting ${path}: ${(err as Error).message}`, 0, err);
        } finally {
            clearTimeout(timer);
        }

        if (!res.ok) {
            // Try to surface the API's error message, fall back to status text.
            let bodyParsed: unknown;
            const errText = await res.text();
            try { bodyParsed = errText ? JSON.parse(errText) : undefined; }
            catch { bodyParsed = errText; }
            const message = (bodyParsed as { error?: string } | undefined)?.error
                ?? `${method} ${path} failed with HTTP ${res.status}`;
            throw new DigiPayError(message, res.status, bodyParsed);
        }

        return res;
    }
}
