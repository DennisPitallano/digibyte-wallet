namespace DigiByte.Pay.Api.Data;

/// <summary>Operator-wide key/value settings: payout pause and ledger scan state (specs/034 data-model).</summary>
public class OperatorSetting
{
    public const string PayoutsPausedAt = "payouts.paused_at";
    public const string PayoutsPausedReason = "payouts.paused_reason";
    public const string LedgerLastScanAt = "ledger.last_scan_at";
    public const string NodeUnreachableSince = "node.unreachable_since";

    public required string Key { get; init; }
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
