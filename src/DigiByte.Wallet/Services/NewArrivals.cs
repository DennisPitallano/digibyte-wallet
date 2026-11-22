using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Which transactions in a freshly loaded list have just arrived, for the "payment received" notice. It goes by transaction
/// ID, not by a change in the balance figure: a balance read can come back low when an explorer drops some address lookups,
/// and the next good read would then look like money arriving.
/// </summary>
public static class NewArrivals
{
    /// <summary>An older transaction showing up later than this is history filling in, not an arrival.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Received transactions that are not in <paramref name="known"/> and are recent: at most 2 confirmations, or newer than
    /// <see cref="RecentWindow"/>. A null <paramref name="known"/> means this is the first list for the wallet, which is
    /// recorded without announcing anything. The caller adds the list's IDs to its known set afterwards.
    /// </summary>
    public static IReadOnlyList<TransactionRecord> Find(
        IReadOnlySet<string>? known, IReadOnlyList<TransactionRecord> transactions, DateTime utcNow)
    {
        if (known is null) return [];

        var cutoff = utcNow - RecentWindow;
        return transactions
            .Where(t => !known.Contains(t.TxId)
                        && t.Direction == TransactionDirection.Received
                        && (t.Confirmations <= 2 || t.Timestamp.ToUniversalTime() >= cutoff))
            .ToList();
    }
}
