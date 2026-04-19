namespace DigiByte.Pay.Api.Data;

/// <summary>
/// A merchant-owned API key for server-to-server SDK/REST use. Tokens are
/// "dgp_{Prefix}_{secret}" — we store the Prefix in clear and the SHA-256
/// hash of the secret. Merchants can hold multiple active keys (e.g. one
/// per environment / CI host) and revoke individually without disrupting
/// the others or the browser session.
///
/// The account-level <c>PayMerchant.ApiKeyPrefix</c>/<c>ApiKeyHash</c> pair
/// predates this table; the startup backfill seeds one row per merchant
/// from those columns so existing SDK integrations keep working.
/// </summary>
public class PayApiKey
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    /// <summary>"dgp_xxxxxxxxxx" — unique, indexed for auth lookup.</summary>
    public required string Prefix { get; init; }
    /// <summary>SHA-256 hex of the raw secret; raw secret is never stored.</summary>
    public required string Hash { get; init; }
    /// <summary>Optional label shown in the dashboard ("production", "ci", etc.).</summary>
    public string? Label { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Bumped on every successful authenticate.</summary>
    public DateTime? LastUsedAt { get; set; }
    /// <summary>Non-null = revoked; auth lookups filter these out.</summary>
    public DateTime? RevokedAt { get; set; }
}
