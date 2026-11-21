using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Optional ability of a chain source (spec 023): per-address summaries and paged history. A source that can't report counts
/// (the own node, the gateway) does not implement it, and the wallet then reads every address's history as before.
/// </summary>
public interface IAddressSummarySource
{
    /// <summary>
    /// One lookup. Throws on any failure or incomplete reply; never returns an empty summary for a failure.
    /// <paramref name="recordFailure"/> false means a failure records no cooldown or outage (used by address discovery);
    /// sources without cooldowns ignore it.
    /// </summary>
    Task<AddressSummary> GetAddressSummaryAsync(string address, bool recordFailure = true);

    /// <summary>
    /// Newest-first page of an address's history. <paramref name="afterTxId"/> null gives the newest page (including pending);
    /// otherwise the next confirmed page after that txid. An empty page is the end of the history. Throws on failure.
    /// </summary>
    Task<AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId);
}

/// <summary>No source can give address summaries right now; the caller uses the full-read path for this refresh.</summary>
public sealed class SummaryUnavailableException : Exception
{
    public SummaryUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
