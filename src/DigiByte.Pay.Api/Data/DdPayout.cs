namespace DigiByte.Pay.Api.Data;

/// <summary>One transfer of an owner's held DD to its payout address (specs/034 data-model).</summary>
public class DdPayout
{
    public const string StatusWaiting = "waiting";
    public const string StatusSending = "sending";
    public const string StatusChecking = "checking";
    public const string StatusSent = "sent";
    public const string StatusConfirmed = "confirmed";
    public const string StatusFailed = "failed";

    public const string FailureSuperseded = "destination_superseded";
    public const string FailureAddressInvalid = "payout_address_invalid";
    public const string FailureRejected = "send_rejected";
    public const string FailureNotFound = "send_not_found";

    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public string? StoreId { get; set; }
    public required string Destination { get; init; }
    /// <summary>At least 100.</summary>
    public required long AmountCents { get; init; }
    public string Status { get; set; } = StatusWaiting;
    /// <summary>node_unavailable | fee_reserve | coverage | operator_paused | change_pending.</summary>
    public string? WaitReason { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>auto | daily | threshold | threshold_age | manual | retry | store_delete.</summary>
    public required string Trigger { get; init; }
    public string? Txid { get; set; }
    public int Confirmations { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    /// <summary>When an insufficient-funds rejection first put this payout back to waiting.</summary>
    public DateTime? CoverageWaitSince { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? FailedAt { get; set; }
}

/// <summary>Links a payout to the confirmed credits it covers.</summary>
public class DdPayoutItem
{
    public required string PayoutId { get; init; }
    public required string LedgerEntryId { get; init; }
    public required long AmountCents { get; init; }
}
