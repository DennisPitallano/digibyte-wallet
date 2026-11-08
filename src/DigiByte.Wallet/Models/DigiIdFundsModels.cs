using System.Text.Json.Serialization;

namespace DigiByte.Wallet.Models;

// Types for finding and recovering funds sent to Digi-ID addresses (spec 009).
// None of these holds a private key, seed or PIN (FR-015). Amounts are in satoshis.

/// <summary>How a check of one Digi-ID address ended. A failed lookup is never <see cref="Empty"/>.</summary>
public enum DigiIdFundOutcome
{
    /// <summary>The address holds funds (confirmed, pending or both).</summary>
    Funds,
    /// <summary>The lookup succeeded and found nothing.</summary>
    Empty,
    /// <summary>"CouldNotCheck only when the lookup threw. Never shown as Empty."</summary>
    CouldNotCheck,
}

/// <summary>Result of checking one Digi-ID address.</summary>
public sealed record DigiIdFundFinding
{
    public required string Domain { get; init; }

    /// <summary>Legacy P2PKH Digi-ID address, derived from the domain, never read from the login history. Empty when the domain was not a valid site name.</summary>
    public required string Address { get; init; }

    /// <summary>Sum of UTXOs with at least 1 confirmation. Only this amount is sweepable.</summary>
    public long ConfirmedSatoshis { get; init; }

    /// <summary>Sum of UTXOs with 0 confirmations. Shown, never swept.</summary>
    public long PendingSatoshis { get; init; }

    public required DigiIdFundOutcome Outcome { get; init; }

    /// <summary>Short user-facing reason. Set only for <see cref="DigiIdFundOutcome.CouldNotCheck"/>.</summary>
    public string? Error { get; init; }

    /// <summary>A sweep record for this address is Sending or Pending. The address is not offered for sweeping.</summary>
    public bool InFlight { get; init; }

    /// <summary>
    /// Outputs whose script is present and differs from the address's own standard script. Left untouched and excluded from
    /// every amount (FR-016). Zero when the lookup returns no script.
    /// </summary>
    public int OtherOutputsCount { get; init; }

    public bool CanSweep => Outcome == DigiIdFundOutcome.Funds && ConfirmedSatoshis > 0 && !InFlight;
}

/// <summary>Why a selected address is not part of a sweep plan.</summary>
public enum DigiIdSweepExclusionKind
{
    /// <summary>"Must be at least 10,000 sat (0.0001 DGB), else the item is excluded as 'too small'."</summary>
    TooSmall,
    /// <summary>No confirmed funds (only pending, or nothing).</summary>
    NothingConfirmed,
    /// <summary>A sweep for this address is already Sending or Pending.</summary>
    InFlight,
    /// <summary>The re-check of the address failed, so its funds were not planned.</summary>
    LookupFailed,
}

public sealed record DigiIdSweepExclusion(string Domain, string SourceAddress, long AmountSatoshis, DigiIdSweepExclusionKind Kind, string Reason);

/// <summary>One address in a sweep plan. Holds no key: the key is re-derived when the plan is executed.</summary>
public sealed record DigiIdSweepPlanItem
{
    public required string Domain { get; init; }
    public required string SourceAddress { get; init; }

    /// <summary>Confirmed plain-DGB funds on the address.</summary>
    public required long AmountSatoshis { get; init; }

    /// <summary>EstimateFee(inputs, 1, rate, Legacy). Estimate only: the final fee comes from the signed transaction.</summary>
    public required long EstimatedFeeSatoshis { get; init; }

    /// <summary>Amount minus fee. Must be at least 10,000 sat (0.0001 DGB), else the item is excluded as "too small".</summary>
    public required long NetSatoshis { get; init; }

    /// <summary>Fresh BIP84 receive address. Unique within the plan and unused. Never reused across items (FR-008a).</summary>
    public required string DestinationAddress { get; init; }

    /// <summary>Receive index of <see cref="DestinationAddress"/>.</summary>
    public required int DestinationIndex { get; init; }
}

/// <summary>What the user reviews before confirming a sweep.</summary>
public sealed record DigiIdSweepPlan
{
    /// <summary>One per swept address. At most 19 (ReceiveGapLimit minus 1), and fewer if unused addresses already handed out leave less room.</summary>
    public required IReadOnlyList<DigiIdSweepPlanItem> Items { get; init; }

    /// <summary>Eligible addresses left for a later round because of the cap.</summary>
    public int Remaining { get; init; }

    /// <summary>Selected addresses that were left out, with the reason.</summary>
    public IReadOnlyList<DigiIdSweepExclusion> Excluded { get; init; } = [];

    /// <summary>Not below the wallet floor (100).</summary>
    public required int FeeRateSatPerVByte { get; init; }

    public long TotalAmountSatoshis => Items.Sum(i => i.AmountSatoshis);
    public long TotalFeeSatoshis => Items.Sum(i => i.EstimatedFeeSatoshis);
    public long TotalNetSatoshis => Items.Sum(i => i.NetSatoshis);
}

/// <summary>Where a sweep stands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DigiIdSweepStatus
{
    /// <summary>Recorded before broadcast. Without a TxId it means a run was interrupted.</summary>
    Sending,
    /// <summary>Broadcast, not yet confirmed.</summary>
    Pending,
    /// <summary>At least one confirmation.</summary>
    Confirmed,
    /// <summary>Not sent, rejected, dropped or interrupted. The funds are still on the Digi-ID address.</summary>
    Failed,
}

/// <summary>
/// One sweep of one Digi-ID address, stored per wallet. Contains no key, seed, raw transaction or signature.
/// </summary>
public sealed record DigiIdSweepRecord
{
    public required string Id { get; init; }
    public required string Domain { get; init; }
    public required string SourceAddress { get; init; }
    public required string DestinationAddress { get; init; }

    public required long AmountSatoshis { get; init; }

    /// <summary>Final amounts from the signed transaction (fee is the real one).</summary>
    public required long FeeSatoshis { get; init; }

    /// <summary>Null until broadcast succeeds.</summary>
    public string? TxId { get; init; }

    public required DigiIdSweepStatus Status { get; init; }

    /// <summary>Short text, set when <see cref="DigiIdSweepStatus.Failed"/>.</summary>
    public string? FailureReason { get; init; }

    public required DateTime CreatedUtc { get; init; }
    public required DateTime UpdatedUtc { get; init; }

    /// <summary>Sending or Pending: the address's funds are in flight and the address is not offered again.</summary>
    [JsonIgnore]
    public bool IsInFlight => Status is DigiIdSweepStatus.Sending or DigiIdSweepStatus.Pending;
}
