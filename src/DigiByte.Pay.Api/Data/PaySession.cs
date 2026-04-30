namespace DigiByte.Pay.Api.Data;

public enum PaySessionStatus
{
    Pending = 0,
    Seen = 1,
    Paid = 2,
    Confirmed = 3,
    Expired = 4,
    Underpaid = 5,
    // Merchant-initiated terminal states. Refunded applies to a previously-
    // paid (or confirmed) session that the merchant has settled off-platform
    // — we only record the refund txid for audit, we do NOT broadcast. Voided
    // cancels a pending/seen session the merchant decided not to honour.
    Refunded = 6,
    Voided = 7,
}

public class PaySession
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public required string StoreId { get; init; }
    public required int AddressIndex { get; init; }
    public required string Address { get; init; }
    public required long AmountSatoshis { get; init; }
    public string? FiatCurrency { get; init; }
    public decimal? FiatAmount { get; init; }
    public decimal? DgbPriceAtCreation { get; init; }
    public string? Label { get; init; }
    public string? Memo { get; init; }
    public PaySessionStatus Status { get; set; } = PaySessionStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    // Settable so the void endpoint can collapse it to now and stop the
    // monitor from polling the chain for a cancelled invoice.
    public required DateTime ExpiresAt { get; set; }
    public DateTime? SeenAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaidTxid { get; set; }
    public long ReceivedSatoshis { get; set; }
    public int Confirmations { get; set; }

    // Merchant-initiated refund/void audit fields. Refund is record-keeping
    // only — the merchant sends the refund DGB off-platform and pastes the
    // txid here so the dashboard + webhooks stay in sync. Void carries only
    // the note (no on-chain activity).
    public string? RefundTxid { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? RefundNote { get; set; }

    /// <summary>
    /// Origin tag for the session. Today this is only stamped for sessions
    /// created from a POS-paired API key (value: "pos"); SDK / hosted /
    /// direct-API calls leave it null. Used by the dashboard to flag POS
    /// sales separately from online/SDK orders. Free-form so future sources
    /// (e.g. "woocommerce", "shopify") don't need an enum migration.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Optional URL the hosted checkout sends the buyer back to after
    /// payment confirms. Stripe-shaped — merchants pass this on session
    /// create so their customer ends the flow on the merchant's "thank you"
    /// page rather than on Pay.Web. HTTPS-only in production; HTTP allowed
    /// for localhost so dev environments work. Length-capped at 2048 to
    /// match the practical URL ceiling.
    /// </summary>
    public string? ReturnUrl { get; init; }
}
