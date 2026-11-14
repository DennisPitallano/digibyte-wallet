using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Works out which of the wallet's own addresses a transaction ties to, so activity rows can show them.
/// Pure: no storage and no network.
/// </summary>
internal static class TransactionAddressResolver
{
    /// <param name="tx">The explorer's view of the transaction.</param>
    /// <param name="isSent">True when the wallet funded the transaction.</param>
    /// <param name="ownByKey">Every address the wallet scans, keyed by <see cref="AddressKey.Normalize"/>; the value is the form the wallet generated, which is what gets shown.</param>
    /// <param name="changeKeys">Normalized change addresses of an HD wallet.</param>
    /// <param name="singleKey">A single-key wallet sends its change back to its own addresses, so on a send every own output counts as change.</param>
    public static (List<string> Wallet, List<string> ReceivedTo) Resolve(
        TransactionInfo tx,
        bool isSent,
        IReadOnlyDictionary<string, string> ownByKey,
        IReadOnlySet<string> changeKeys,
        bool singleKey)
    {
        var wallet = new List<string>();
        var receivedTo = new List<string>();
        var seen = new HashSet<string>();

        void Add(List<string> into, string? address, bool skipChange)
        {
            var key = AddressKey.Normalize(address);
            if (key.Length == 0 || !ownByKey.TryGetValue(key, out var own)) return;
            if (skipChange && changeKeys.Contains(key)) return;
            if (seen.Add(into == wallet ? "w:" + key : "r:" + key)) into.Add(own);
        }

        if (isSent)
        {
            foreach (var input in tx.Inputs) Add(wallet, input.Address, skipChange: false);
            if (!singleKey)
                foreach (var output in tx.Outputs) Add(receivedTo, output.Address, skipChange: true);
        }
        else
        {
            foreach (var output in tx.Outputs) Add(wallet, output.Address, skipChange: true);
        }

        return (wallet, receivedTo);
    }

    /// <summary>
    /// Copies the explorer's addresses onto local records that have none, because a local record replaces the
    /// explorer's version when the two are merged. Returns the records that changed.
    /// </summary>
    public static List<TransactionRecord> CopyMissing(
        IEnumerable<TransactionRecord> local,
        IReadOnlyDictionary<string, TransactionRecord> explorerByTxId)
    {
        var changed = new List<TransactionRecord>();
        foreach (var record in local)
        {
            if (record.WalletAddresses.Count > 0 || record.WalletReceivedAddresses.Count > 0) continue;
            if (!explorerByTxId.TryGetValue(record.TxId, out var found)) continue;
            if (found.WalletAddresses.Count == 0 && found.WalletReceivedAddresses.Count == 0) continue;

            record.WalletAddresses = [.. found.WalletAddresses];
            record.WalletReceivedAddresses = [.. found.WalletReceivedAddresses];
            changed.Add(record);
        }
        return changed;
    }
}
