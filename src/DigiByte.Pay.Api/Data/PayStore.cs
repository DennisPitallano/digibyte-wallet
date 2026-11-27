namespace DigiByte.Pay.Api.Data;

/// <summary>
/// A merchant's store / shop / project. A merchant (account) can own many stores,
/// each with its own receive (xpub or address), webhook configuration, and
/// default session expiry. Sessions belong to exactly one store.
/// </summary>
public class PayStore
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
    /// Merchant opt-in for DigiDollar (DD) payments. When true, sessions may be created with Currency="DD" and DD
    /// payment monitoring is enabled for this store. It can only be on while <see cref="DdAccountKey"/> is set
    /// (specs/035 FR-004): every DD address is derived from that key, so DD goes straight to the merchant's wallet.
    /// </summary>
    public bool DigiDollarEnabled { get; set; }

    /// <summary>
    /// The merchant's DigiDollar account public key (xpub/tpub at m/86'/coin'/0', specs/035), exactly as pasted and
    /// checked by <see cref="DigiByte.Crypto.KeyGeneration.DigiDollarAccountKey"/>. Never a private key. Max 200 chars.
    /// </summary>
    public string? DdAccountKey { get; set; }

    /// <summary>SHA-256 hex of <c>network:key</c> (64 chars): the id of the key's shared <see cref="DdKeySequence"/>.</summary>
    public string? DdKeyId { get; set; }

    public DateTime? DdAccountKeySetAt { get; set; }

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
    /// The store's fixed DD donation address (specs/033 FR-011, 035 FR-009), derived from the next index of its key's
    /// sequence the first time it's asked for. Cleared when the key is replaced or removed.
    /// </summary>
    public string? DdDonationAddress { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
