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
    // Address, AmountSatoshis, Currency and AmountCentsDd describe the currency in effect. They are settable so a
    // buyer-choice session can switch to the option it locks to (specs/033 research R1).
    public required string Address { get; set; }
    public required long AmountSatoshis { get; set; }
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
    // For DGB sessions this is received satoshis. For DD sessions
    // (Currency = "DD"), it holds received amount in USD cents. The unit is
    // implied by Currency; consumers should branch on that field rather than
    // reading raw satoshis for DD.
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

    /// <summary>
    /// Payment currency for this session. "DGB" (default, existing behaviour)
    /// or "DD" (DigiDollar — native USD-pegged stablecoin). DD sessions carry
    /// their amount in <see cref="AmountCentsDd"/> and leave AmountSatoshis
    /// at 0; DGB sessions do the reverse. Added in migration
    /// AddDigiDollarSupport with a "DGB" default so existing rows stay valid.
    /// </summary>
    public string Currency { get; set; } = "DGB";

    /// <summary>
    /// Requested amount in USD cents when <see cref="Currency"/> = "DD".
    /// Null / zero for DGB sessions. Kept separate from AmountSatoshis for
    /// semantic clarity — cents and sats are different units.
    /// </summary>
    public long? AmountCentsDd { get; set; }

    /// <summary>
    /// True once the sandbox (POST /v1/pay/test/sessions/{id}/advance) has moved this
    /// testnet/regtest session. InvoiceMonitor skips simulated sessions so the chain
    /// (which has no such payment) never overwrites the simulated state.
    /// </summary>
    public bool Simulated { get; set; }

    /// <summary>
    /// True when the buyer may pay in DGB or DD (specs/033 FR-020, research R5). Set at creation only; the
    /// options live in <see cref="PaySessionOption"/> rows.
    /// </summary>
    public bool BuyerChoice { get; init; }

    /// <summary>When funds were first seen on one of a buyer-choice session's options. Null otherwise.</summary>
    public DateTime? LockedAt { get; set; }

    /// <summary>"DGB" or "DD": currency of funds received on the option the session didn't lock to (FR-025).</summary>
    public string? ExtraCurrency { get; set; }

    /// <summary>Extra funds received: satoshis for DGB, cents for DD. Cumulative.</summary>
    public long? ExtraAmount { get; set; }

    /// <summary>First txid seen for the extra funds.</summary>
    public string? ExtraTxid { get; set; }

    public DateTime? ExtraSeenAt { get; set; }
}
