namespace DigiByte.Pay.Api.Data;

/// <summary>
/// A merchant / account. Owns one or more <see cref="PayStore"/>s, each of which
/// carries its own receive config, webhook, and sessions.
///
/// Keyed by <see cref="DigiIdAddress"/> after Digi-ID sign-in. The API key
/// here is account-scoped (not per-store) — all stores owned by the account
/// share this key. Per-store keys can be added later without breaking the
/// account-level one.
/// </summary>
public class PayMerchant
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    // Digi-ID address acts as the merchant's stable identity.
    // Null for merchants created via the (legacy / SDK) unauthenticated API-key path.
    public string? DigiIdAddress { get; set; }
    public required string ApiKeyPrefix { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
