using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Models;

/// <summary>
/// What the source last reported for one watched address (spec 023): confirmed and pending transaction counts and amounts.
/// Used to compute the balance and to decide whether the address's history needs fetching.
/// </summary>
public sealed record AddressSummary
{
    /// <summary>The address exactly as derived (not normalised).</summary>
    public required string Address { get; init; }

    public int ConfirmedTxCount { get; init; }
    public int PendingTxCount { get; init; }
    public long ConfirmedFunded { get; init; }
    public long ConfirmedSpent { get; init; }
    public long PendingFunded { get; init; }
    public long PendingSpent { get; init; }

    /// <summary>When the source answered.</summary>
    public DateTime ReadAtUtc { get; init; }

    /// <summary>The confirmed balance of the address.</summary>
    public long Confirmed => ConfirmedFunded - ConfirmedSpent;

    /// <summary>Net pending amount: pending received minus pending spent. May be negative.</summary>
    public long PendingNet => PendingFunded - PendingSpent;

    /// <summary>Has appeared in any transaction, confirmed or pending (the same rule as <c>HasAddressHistoryAsync</c>).</summary>
    public bool IsUsed => ConfirmedTxCount + PendingTxCount > 0;

    /// <summary>True if any count or amount differs. The read time is ignored.</summary>
    public bool DiffersFrom(AddressSummary other) =>
        ConfirmedTxCount != other.ConfirmedTxCount
        || PendingTxCount != other.PendingTxCount
        || ConfirmedFunded != other.ConfirmedFunded
        || ConfirmedSpent != other.ConfirmedSpent
        || PendingFunded != other.PendingFunded
        || PendingSpent != other.PendingSpent;
}

/// <summary>How much of one address's history has been fetched and saved.</summary>
public sealed record AddressHistoryCursor
{
    /// <summary>Every confirmed transaction up to the summary's count has been fetched.</summary>
    public bool Complete { get; init; }

    /// <summary>The 2,000-transaction safety cap was hit.</summary>
    public bool Truncated { get; init; }

    /// <summary>Newest confirmed txid saved for this address; later fetches stop at it.</summary>
    public string? NewestConfirmedTxId { get; init; }

    /// <summary>Pending txids last seen for this address.</summary>
    public string[] PendingTxIds { get; init; } = [];
}

/// <summary>
/// One newest-first page of an address's history. <see cref="LastConfirmedTxId"/> is the txid to ask for the next page after;
/// null when the page held no confirmed transaction. <see cref="BlockHeights"/> gives the block of each confirmed transaction
/// on the page, so confirmations can be worked out again later from the tip.
/// </summary>
public sealed record AddressHistoryPage(
    IReadOnlyList<TransactionInfo> Transactions,
    string? LastConfirmedTxId,
    IReadOnlyDictionary<string, int>? BlockHeights = null);
