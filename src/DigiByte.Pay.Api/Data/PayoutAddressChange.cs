namespace DigiByte.Pay.Api.Data;

/// <summary>A requested change of an owner's DD payout address, held for 48 hours (specs/034 FR-005).</summary>
public class PayoutAddressChange
{
    public const string StatusPending = "pending";
    public const string StatusApplied = "applied";
    public const string StatusCancelled = "cancelled";

    public required string Id { get; init; }
    /// <summary>Null for the merchant-level balance.</summary>
    public string? StoreId { get; set; }
    public required string MerchantId { get; init; }
    public string? OldAddress { get; init; }
    public required string NewAddress { get; init; }
    public string Status { get; set; } = StatusPending;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    public required DateTime EffectiveAt { get; init; }
    public required string RequestedBySessionId { get; init; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBySessionId { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
