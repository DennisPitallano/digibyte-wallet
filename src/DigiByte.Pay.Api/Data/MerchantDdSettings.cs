namespace DigiByte.Pay.Api.Data;

/// <summary>
/// The DD payout settings an owner has: a store (<see cref="PayStore"/>) or a merchant's DD held outside any store
/// (<see cref="MerchantDdSettings"/>). The planner, sender and address service work through this view only.
/// </summary>
public interface IDdPayoutOwner
{
    string MerchantId { get; }
    /// <summary>Null for the merchant-level owner.</summary>
    string? OwnerStoreId { get; }
    string Network { get; }
    string? DdPayoutAddress { get; set; }
    DateTime? DdPayoutAddressSetAt { get; set; }
    string DdPayoutSchedule { get; set; }
    TimeOnly? DdPayoutDailyTime { get; set; }
    string? DdPayoutTimeZone { get; set; }
    long? DdPayoutThresholdCents { get; set; }
    DateTime? DdPayoutsAutoPausedAt { get; set; }
    int DdPayoutConsecutiveFailures { get; set; }
    DateTime? DdPayoutsPausedByOperatorAt { get; set; }
    DateTime? DdPayoutRequestedAt { get; set; }
    /// <summary>"manual" or "retry" while <see cref="DdPayoutRequestedAt"/> is set.</summary>
    string? DdPayoutRequestReason { get; set; }
    DateTime? DdPayoutLastDailyRunAt { get; set; }
}

/// <summary>
/// Payout settings for a merchant's DD held outside any store: DD from deleted stores, and remainders under 1.00 DD
/// moved there when a store is deleted (specs/034 FR-015, FR-039). One row per merchant.
/// </summary>
public class MerchantDdSettings : IDdPayoutOwner
{
    public required string MerchantId { get; init; }
    public string? OwnerStoreId => null;
    public required string Network { get; set; }
    public string? DdPayoutAddress { get; set; }
    public DateTime? DdPayoutAddressSetAt { get; set; }
    public string DdPayoutSchedule { get; set; } = "confirmed";
    public TimeOnly? DdPayoutDailyTime { get; set; }
    public string? DdPayoutTimeZone { get; set; }
    public long? DdPayoutThresholdCents { get; set; }
    public DateTime? DdPayoutsAutoPausedAt { get; set; }
    public int DdPayoutConsecutiveFailures { get; set; }
    public DateTime? DdPayoutsPausedByOperatorAt { get; set; }
    public DateTime? DdPayoutRequestedAt { get; set; }
    public string? DdPayoutRequestReason { get; set; }
    public DateTime? DdPayoutLastDailyRunAt { get; set; }
}
