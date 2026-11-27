namespace DigiByte.Pay.Api.Data;

/// <summary>
/// A merchant's store / shop / project. A merchant (account) can own many stores,
/// each with its own receive (xpub or address), webhook configuration, and
/// default session expiry. Sessions belong to exactly one store.
/// </summary>
public class PayStore : IDdPayoutOwner
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public required string Name { get; set; }
    public required string Network { get; set; } = "mainnet";

    // Exactly one of Xpub / ReceiveAddress is populated after configuration;
    // both null means the store isn't ready to accept payments yet.
    public string? Xpub { get; set; }
    public string? ReceiveAddress { get; set; }
    public int NextAddressIndex { get; set; }

    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }

    // Per-store default for how long pending sessions stay open before expiring.
    // Null falls back to the global default (30 min).
    public int? DefaultSessionExpiryMinutes { get; set; }

    /// <summary>
    /// Merchant opt-in for DigiDollar (DD) payments. When true, sessions may
    /// be created with Currency="DD" and DD payment monitoring is enabled for
    /// this store. Defaults to false — existing DGB integrations keep working
    /// unchanged. DD addresses are generated on demand via the node RPC (no
    /// xpub-based derivation exists for DD), so no per-store address config
    /// is stored here beyond this flag.
    /// </summary>
    public bool DigiDollarEnabled { get; set; }

    /// <summary>
    /// The currency the dashboard's payment links and a newly paired POS tablet start in:
    /// "DGB" or a fiat code from <see cref="DigiByte.Wallet.Models.FiatCurrencies"/>, upper case.
    /// Null = not set (payment links start in USD, a new tablet in DGB).
    /// </summary>
    public string? DefaultCurrency { get; set; }

    /// <summary>
    /// Whether fiat-priced payments let the buyer pick DGB or DD at checkout (specs/033 FR-029). On by default; it
    /// only has an effect while <see cref="DigiDollarEnabled"/> is on.
    /// </summary>
    public bool BuyerChoiceEnabled { get; set; } = true;

    /// <summary>
    /// The store's fixed DD donation address (specs/033 FR-011), created on demand. Never rotated, and kept when DD is
    /// turned off so donations sent later are still credited.
    /// </summary>
    public string? DdDonationAddress { get; set; }

    // ---- DD payouts (specs/034 data-model) ----

    public string? OwnerStoreId => Id;

    /// <summary>The store's DD payout address in effect. Valid DD for <see cref="Network"/>, never a DigiPay address.</summary>
    public string? DdPayoutAddress { get; set; }
    public DateTime? DdPayoutAddressSetAt { get; set; }
    /// <summary>confirmed | daily | threshold.</summary>
    public string DdPayoutSchedule { get; set; } = "confirmed";
    public TimeOnly? DdPayoutDailyTime { get; set; }
    /// <summary>IANA zone, from the merchant's browser when they pick "daily"; UTC when not set.</summary>
    public string? DdPayoutTimeZone { get; set; }
    /// <summary>At least 100 and at most 10,000,000.</summary>
    public long? DdPayoutThresholdCents { get; set; }
    /// <summary>Set after 3 failed payouts in a row (FR-022).</summary>
    public DateTime? DdPayoutsAutoPausedAt { get; set; }
    public int DdPayoutConsecutiveFailures { get; set; }
    /// <summary>Operator pause for this store only (FR-021).</summary>
    public DateTime? DdPayoutsPausedByOperatorAt { get; set; }
    /// <summary>Set by "Pay out now" / "Retry now"; cleared when the planner picks it up.</summary>
    public DateTime? DdPayoutRequestedAt { get; set; }
    public string? DdPayoutRequestReason { get; set; }
    public DateTime? DdPayoutLastDailyRunAt { get; set; }
    /// <summary>Stores that already had DD on when payouts shipped may keep it on without a payout address (FR-031).</summary>
    public bool DdPayoutAddressRequiredExempt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
