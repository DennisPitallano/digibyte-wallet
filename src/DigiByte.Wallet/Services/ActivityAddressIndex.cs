using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

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
