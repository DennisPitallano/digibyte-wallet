namespace DigiByte.Pay.Api.Data;

public class PayMerchant
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    // Digi-ID address acts as the merchant's stable identity.
    // Null for merchants created via the (legacy / SDK) unauthenticated API-key path.
    public string? DigiIdAddress { get; set; }
    // Exactly one of Xpub / ReceiveAddress is populated.
    // Xpub: fresh address per session via BIP84 derivation (proper isolation).
    // ReceiveAddress: single reused address (simpler onboarding, ambiguity if
    //   multiple sessions are open simultaneously).
    public string? Xpub { get; set; }
    public string? ReceiveAddress { get; set; }
    public required string Network { get; set; } = "mainnet";
    public int NextAddressIndex { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public required string ApiKeyPrefix { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
