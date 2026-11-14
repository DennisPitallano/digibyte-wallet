using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>One of the wallet's own addresses that appears in the history.</summary>
/// <param name="Address">The address as the wallet generated it.</param>
/// <param name="Key">Normalized form, for grouping.</param>
/// <param name="Label">The user's label for the address, if any.</param>
/// <param name="TransactionCount">How many transactions involve it.</param>
/// <param name="LastUsed">Time of the newest of those transactions.</param>
public sealed record UsedAddress(string Address, string Key, string? Label, int TransactionCount, DateTime LastUsed);

/// <summary>
/// What the activity list shows about a transaction's own wallet addresses (spec 010). Pure: no I/O.
/// </summary>
public static class ActivityAddressIndex
{
    /// <summary>Turns a label store (address → label) into a lookup keyed by normalized address.</summary>
    public static IReadOnlyDictionary<string, string> BuildLabelLookup(IEnumerable<KeyValuePair<string, string>> labelsByAddress)
    {
        var lookup = new Dictionary<string, string>();
        foreach (var (address, label) in labelsByAddress)
        {
            if (string.IsNullOrWhiteSpace(label)) continue;
            lookup[AddressKey.Normalize(address)] = label;
        }
        return lookup;
    }

    /// <summary>
    /// Every wallet address that appears in the history, once each (compared in normalized form), with its label, how many
    /// transactions involve it and when it was last used. Newest first. Addresses with no transaction never appear, and
    /// change-only addresses are never in a record's address lists, so they do not appear either.
    /// </summary>
    public static IReadOnlyList<UsedAddress> Build(
        IEnumerable<TransactionRecord> transactions, IReadOnlyDictionary<string, string> labelLookup)
    {
        var byKey = new Dictionary<string, (string Address, int Count, DateTime Last)>();
        foreach (var tx in transactions)
        {
            // A transaction counts once per address, even if the address is in both lists.
            foreach (var address in tx.WalletAddresses.Concat(tx.WalletReceivedAddresses).DistinctBy(AddressKey.Normalize))
            {
                var key = AddressKey.Normalize(address);
                if (key.Length == 0) continue;
                byKey[key] = byKey.TryGetValue(key, out var seen)
                    ? (seen.Address, seen.Count + 1, tx.Timestamp > seen.Last ? tx.Timestamp : seen.Last)
                    : (address, 1, tx.Timestamp);
            }
        }

        return byKey
            .Select(kv => new UsedAddress(
                kv.Value.Address, kv.Key,
                labelLookup.TryGetValue(kv.Key, out var label) ? label : null,
                kv.Value.Count, kv.Value.Last))
            .OrderByDescending(u => u.LastUsed)
            .ToList();
    }

    /// <summary>
    /// The address filter: true when no address is chosen, or when the address is one of the transaction's wallet
    /// addresses (funding or paid). Encoding-independent.
    /// </summary>
    public static bool Matches(TransactionRecord tx, string? address)
    {
        if (string.IsNullOrEmpty(address)) return true;
        var key = AddressKey.Normalize(address);
        return Contains(tx.WalletAddresses, key) || Contains(tx.WalletReceivedAddresses, key);
    }

    /// <summary>Search: true when the text is found in one of the transaction's wallet addresses or their labels.</summary>
    public static bool MatchesText(TransactionRecord tx, string text, IReadOnlyDictionary<string, string> labelLookup)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;
        foreach (var address in tx.WalletAddresses.Concat(tx.WalletReceivedAddresses))
        {
            if (address.Contains(text, ci)) return true;
            if (labelLookup.TryGetValue(AddressKey.Normalize(address), out var label) && label.Contains(text, ci)) return true;
        }
        return false;
    }

    private static bool Contains(List<string> addresses, string key)
    {
        foreach (var a in addresses)
            if (AddressKey.Normalize(a) == key) return true;
        return false;
    }

    /// <summary>
    /// The line a row shows: the label of the first wallet address when it has one, else the address itself,
    /// plus how many other wallet addresses are involved. <c>Primary</c> is null when none is known.
    /// </summary>
    public static (string? Primary, int MoreCount) Display(
        TransactionRecord tx, IReadOnlyDictionary<string, string> labelLookup)
    {
        var addresses = tx.WalletAddresses;
        if (addresses.Count == 0) return (null, 0);

        var first = addresses[0];
        var primary = labelLookup.TryGetValue(AddressKey.Normalize(first), out var label) ? label : first;
        return (primary, addresses.Count - 1);
    }
}
