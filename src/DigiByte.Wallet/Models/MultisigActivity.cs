namespace DigiByte.Wallet.Models;

public enum ActivityKind
{
    Received,
    Sent,
    ProposalAwaiting,
    ProposalReady,
    Failed,
    Cancelled,
}

public enum ActivityDirection
{
    In,
    Out,
    None,
}

/// <summary>
/// One line of a multisig's activity: a payment received, a payment sent, or a proposal that has not been broadcast.
/// Computed from chain transactions and local proposals; never stored.
/// </summary>
public class ActivityEntry
{
    public required ActivityKind Kind { get; init; }
    public required ActivityDirection Direction { get; init; }
    public string? TxId { get; init; }

    /// <summary>Received: paid to the multisig address. Sent: outputs not paid back to the multisig (change excluded).</summary>
    public long AmountSatoshis { get; init; }
    public long? FeeSatoshis { get; init; }

    /// <summary>First external output (Sent) or first external input (Received) address.</summary>
    public string? Counterparty { get; init; }

    /// <summary>How many further counterparties there are beyond <see cref="Counterparty"/>.</summary>
    public int MoreCount { get; init; }
    public string? Memo { get; init; }
    public required DateTime Timestamp { get; init; }
    public int? Confirmations { get; init; }
    public string? ProposalId { get; init; }
    public int? SignedCount { get; init; }
    public int? RequiredCount { get; init; }

    public bool IsPending => Kind is ActivityKind.ProposalAwaiting or ActivityKind.ProposalReady;
    public bool IsFailed => Kind is ActivityKind.Failed or ActivityKind.Cancelled;
}

public enum ActivityFilter
{
    All,
    Received,
    Sent,
    Pending,
    Failed,
}

/// <summary>A page of activity plus whether the chain could not be reached.</summary>
public class ActivityPage
{
    public required List<ActivityEntry> Entries { get; init; }

    /// <summary>True when chain data could not be loaded, so only local proposals are shown.</summary>
    public bool IsPartial { get; init; }

    /// <summary>True when asking for the next page may return more chain transactions.</summary>
    public bool HasMore { get; init; }
}
