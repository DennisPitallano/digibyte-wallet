namespace DigiByte.Pay.Api.Data;

/// <summary>
/// Per-browser session token issued on Digi-ID sign-in. Decoupled from the merchant's
/// long-lived API key so signing in from a second device (or re-signing in after
/// clearing storage) doesn't invalidate every existing browser session.
///
/// Token wire format: dps_{prefix}_{secret}
///   dps_ — DigiPay session (distinguishes from dgp_ API keys)
///
/// Only the SHA-256 of the secret is stored; the plaintext is shown once at sign-in
/// and then lives in the browser's localStorage.
/// </summary>
public class MerchantSession
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public required string TokenPrefix { get; init; }
    public required string TokenHash { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    public required DateTime ExpiresAt { get; init; }
}
