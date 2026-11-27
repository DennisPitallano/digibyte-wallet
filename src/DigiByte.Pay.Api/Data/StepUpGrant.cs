namespace DigiByte.Pay.Api.Data;

/// <summary>
/// Proof of a fresh Digi-ID sign-in made for one purpose, by one merchant, from one Dashboard session (specs/034
/// research R6). Single use, 5-minute lifetime.
/// </summary>
public class StepUpGrant
{
    public const string PurposePayoutAddress = "payout-address";

    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public required string SessionId { get; init; }
    public required string Purpose { get; init; }
    public required string DigiIdAddress { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public required DateTime ExpiresAt { get; init; }
    public DateTime? ConsumedAt { get; set; }
}
