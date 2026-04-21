/**
 * Session lifecycle. Kept as a union of string literals rather than an
 * enum so it serialises cleanly and stays forward-compatible — future
 * states can be added by the server without breaking string comparisons
 * in user code.
 */
export type SessionStatus =
    | 'pending'     // waiting for a payment to land in mempool
    | 'seen'        // payment detected, not yet confirmed
    | 'paid'        // confirmed enough for most use-cases (1 conf)
    | 'confirmed'   // 6+ confirmations — treat as irreversible
    | 'expired'     // window closed without enough funds
    | 'underpaid';  // funds received but below the expected amount

export interface Session {
    id: string;
    storeId: string;
    merchantId: string;
    status: SessionStatus;
    /** The DigiByte address this session is watching. */
    address: string;
    /** Amount in satoshis (1 DGB = 100 000 000 sat). */
    amountSatoshis: number;
    /** Same amount expressed as decimal DGB — convenience field. */
    amount: number;
    fiatCurrency?: string | null;
    fiatAmount?: number | null;
    label?: string | null;
    memo?: string | null;
    /** Satoshis actually received so far. */
    receivedSatoshis: number;
    confirmations: number;
    paidTxid?: string | null;
    createdAt: string;
    expiresAt: string;
    seenAt?: string | null;
    paidAt?: string | null;
    /** BIP21 URI — hand to a wallet or QR encoder directly. */
    uri: string;
    /** Hosted checkout URL if you prefer a full page over an embed. */
    checkoutUrl: string;
}

export interface Store {
    id: string;
    merchantId: string;
    name: string;
    network: 'mainnet' | 'testnet' | 'regtest';
    hasReceive: boolean;
    mode: 'address' | 'xpub' | 'none';
    receiveAddress?: string | null;
    /** Truthy means a webhook is configured on this store; the secret is not returned. */
    webhookUrl?: string | null;
    defaultSessionExpiryMinutes: number;
    createdAt: string;
}

export interface WebhookDelivery {
    id: string;
    storeId: string;
    sessionId?: string | null;
    eventName: string;
    url: string;
    attempt: number;
    statusCode?: number | null;
    errorMessage?: string | null;
    durationMs?: number | null;
    responseSnippet?: string | null;
    createdAt: string;
    deliveredAt?: string | null;
    /**
     * When set, the server will automatically re-dispatch at this UTC
     * timestamp. Null on succeeded / permanent-failure / dead-lettered rows.
     */
    nextRetryAt?: string | null;
    success: boolean;
}

export interface RegisterMerchantResponse {
    id: string;
    displayName: string;
    storeId: string;
    network: string;
    mode: 'address' | 'xpub';
    /** Shown once — store it server-side, can't be retrieved later. */
    apiKey: string;
    /** Only set if webhookUrl was provided at registration. */
    webhookSecret?: string | null;
}

export interface CreateMerchantParams {
    displayName: string;
    /** DigiByte address or BIP84 xpub. */
    addressOrXpub: string;
    network?: 'mainnet' | 'testnet' | 'regtest';
    webhookUrl?: string;
}

export interface CreateSessionParams {
    /** Amount in DGB (decimal). For fiat-locked invoices set fiatAmount instead. */
    amount: number;
    storeId?: string;
    label?: string;
    memo?: string;
    fiatCurrency?: string;
    fiatAmount?: number;
    /** Override the store's default expiry window. */
    expiryMinutes?: number;
}

export interface ListSessionsParams {
    storeId?: string;
    status?: SessionStatus;
    /** 1-100, default 25. */
    take?: number;
    skip?: number;
}

export interface SessionList {
    total: number;
    take: number;
    skip: number;
    sessions: Session[];
}

/**
 * Webhook event payload. Delivered as the body of the POST to your
 * configured webhookUrl, verified via the X-DigiPay-Signature header
 * (see `verifyWebhook` in the top-level export).
 */
export interface WebhookEvent {
    event: string;
    timestamp: string;
    session: Session;
}
