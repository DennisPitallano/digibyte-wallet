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

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
