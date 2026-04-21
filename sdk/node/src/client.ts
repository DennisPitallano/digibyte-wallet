import { HttpClient } from './http.js';
import type {
    CreateMerchantParams, CreateSessionParams, ListSessionsParams,
    RegisterMerchantResponse, Session, SessionList, Store, WebhookDelivery,
} from './types.js';

export interface DigiPayOptions {
    /** API key from `POST /v1/pay/merchants` (or the dashboard). Looks like `dgp_…`. */
    apiKey: string;
    /**
     * API base URL. Defaults to the hosted production endpoint —
     * override for self-hosted deployments or staging.
     */
    baseUrl?: string;
    /** Per-request timeout. Defaults to 15 s. */
    timeoutMs?: number;
}

const DEFAULT_BASE_URL = 'https://api.pay.dgbwallet.app';
const SDK_VERSION = '0.1.0';

/**
 * The main entry point. One instance per merchant API key.
 *
 *     import { DigiPay } from '@dgbwallet/digipay';
 *     const dp = new DigiPay({ apiKey: process.env.DIGIPAY_KEY });
 *     const session = await dp.sessions.create({ amount: 5, label: 'Order #123' });
 *     console.log(session.checkoutUrl);
 *
 * For one-shot registration (creating a brand-new merchant + first store
 * + initial API key in a single call) use the static `register` helper —
 * it doesn't need an existing key.
 */
export class DigiPay {
    public readonly sessions: SessionsResource;
    public readonly stores: StoresResource;
    private readonly http: HttpClient;

    constructor(options: DigiPayOptions) {
        if (!options.apiKey) throw new Error('DigiPay: apiKey is required');
        this.http = new HttpClient({
            apiKey: options.apiKey,
            baseUrl: (options.baseUrl ?? DEFAULT_BASE_URL).replace(/\/+$/, ''),
            userAgent: `digipay-node/${SDK_VERSION}`,
            timeoutMs: options.timeoutMs ?? 15_000,
        });
        this.sessions = new SessionsResource(this.http);
        this.stores = new StoresResource(this.http);
    }

    /**
     * Self-serve merchant registration — unauthenticated. Returns the
     * initial API key in the response (shown once, store it server-side).
     */
    static async register(
        params: CreateMerchantParams,
        options: { baseUrl?: string; timeoutMs?: number } = {},
    ): Promise<RegisterMerchantResponse> {
        const baseUrl = (options.baseUrl ?? DEFAULT_BASE_URL).replace(/\/+$/, '');
        const url = `${baseUrl}/v1/pay/merchants`;
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), options.timeoutMs ?? 15_000);
        try {
            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'User-Agent': `digipay-node/${SDK_VERSION}`,
                },
                body: JSON.stringify(params),
                signal: controller.signal,
            });
            const text = await res.text();
            const body = text ? JSON.parse(text) : undefined;
            if (!res.ok) {
                throw new Error((body as { error?: string })?.error ?? `Registration failed: HTTP ${res.status}`);
            }
            return body as RegisterMerchantResponse;
        } finally {
            clearTimeout(timer);
        }
    }
}

class SessionsResource {
    constructor(private readonly http: HttpClient) { }

    /** Create a new payment session. The returned `checkoutUrl` is the hosted page. */
    create(params: CreateSessionParams): Promise<Session> {
        return this.http.request<Session>('POST', '/v1/pay/sessions', params);
    }

    /** Look up a single session by id. Public read — no auth strictly needed. */
    get(sessionId: string): Promise<Session> {
        return this.http.request<Session>('GET', `/v1/pay/sessions/${encodeURIComponent(sessionId)}`)
            .then((res: any) => res.session ?? res);
    }

    /** List sessions for the authenticated merchant, newest first. */
    list(params: ListSessionsParams = {}): Promise<SessionList> {
        const qs = buildQuery({
            storeId: params.storeId,
            status: params.status,
            take: params.take,
            skip: params.skip,
        });
        return this.http.request<SessionList>('GET', `/v1/pay/sessions${qs}`);
    }

    /**
     * Stream the same list as a CSV blob — useful for bookkeeping exports.
     * Returns the raw text (Excel + Sheets parse it natively); callers can
     * pipe to a file or upload it to a sheet without parsing.
     */
    async exportCsv(params: ListSessionsParams = {}): Promise<string> {
        const qs = buildQuery({
            format: 'csv',
            storeId: params.storeId,
            status: params.status,
            take: params.take ?? 10_000,
        });
        const res = await this.http.requestRaw('GET', `/v1/pay/sessions${qs}`);
        return res.text();
    }
}

class StoresResource {
    constructor(private readonly http: HttpClient) { }

    list(): Promise<Store[]> {
        return this.http.request<Store[]>('GET', '/v1/pay/stores');
    }

    get(storeId: string): Promise<Store> {
        return this.http.request<Store>('GET', `/v1/pay/stores/${encodeURIComponent(storeId)}`);
    }

    create(params: { name: string; network?: 'mainnet' | 'testnet' | 'regtest' }): Promise<Store> {
        return this.http.request<Store>('POST', '/v1/pay/stores', params);
    }

    update(storeId: string, patch: Partial<{
        name: string;
        network: 'mainnet' | 'testnet' | 'regtest';
        addressOrXpub: string;
        webhookUrl: string;
        defaultSessionExpiryMinutes: number;
    }>): Promise<Store> {
        return this.http.request<Store>('PATCH', `/v1/pay/stores/${encodeURIComponent(storeId)}`, patch);
    }

    delete(storeId: string): Promise<{ ok: true }> {
        return this.http.request<{ ok: true }>('DELETE', `/v1/pay/stores/${encodeURIComponent(storeId)}`);
    }

    /** Fire a synthetic webhook so the receiver can confirm signature handling. */
    sendTestWebhook(storeId: string): Promise<{ ok: boolean; webhookUrl: string; deliveryId: string; statusCode: number | null; error: string | null }> {
        return this.http.request('POST', `/v1/pay/stores/${encodeURIComponent(storeId)}/webhook/test`);
    }

    listDeliveries(storeId: string, params: { take?: number; sessionId?: string } = {}): Promise<WebhookDelivery[]> {
        const qs = buildQuery({ take: params.take, sessionId: params.sessionId });
        return this.http.request<WebhookDelivery[]>('GET', `/v1/pay/stores/${encodeURIComponent(storeId)}/webhook-deliveries${qs}`);
    }

    /** Re-fire a previous delivery — server writes a new row with attempt + 1. */
    replayDelivery(storeId: string, deliveryId: string): Promise<WebhookDelivery> {
        return this.http.request<WebhookDelivery>(
            'POST',
            `/v1/pay/stores/${encodeURIComponent(storeId)}/webhook-deliveries/${encodeURIComponent(deliveryId)}/replay`,
        );
    }

    /** CSV export of the delivery log — same auth scope as listDeliveries. */
    async exportDeliveriesCsv(storeId: string, params: { take?: number; sessionId?: string } = {}): Promise<string> {
        const qs = buildQuery({
            format: 'csv',
            take: params.take ?? 10_000,
            sessionId: params.sessionId,
        });
        const res = await this.http.requestRaw('GET', `/v1/pay/stores/${encodeURIComponent(storeId)}/webhook-deliveries${qs}`);
        return res.text();
    }
}

function buildQuery(params: Record<string, string | number | undefined>): string {
    const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== null);
    if (entries.length === 0) return '';
    return '?' + entries.map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`).join('&');
}
