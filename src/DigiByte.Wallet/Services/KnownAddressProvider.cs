using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Builds the set of addresses a user already knows (saved contacts plus addresses they have sent to),
/// so the send warning only fires for genuinely new destinations. Derived from existing data only:
/// nothing new is stored, and nothing secret is involved.
/// </summary>
public static class KnownAddressProvider
{
    public static IReadOnlySet<string> Build(IEnumerable<string?> contactAddresses, IEnumerable<string?> sentAddresses)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var address in contactAddresses.Concat(sentAddresses))
        {
            var key = Normalize(address);
            if (key.Length > 0) set.Add(key);
        }
        return set;
    }

    /// <summary>Destinations of the user's earlier DGB sends. Addresses that merely paid the user are not included.</summary>
    public static IEnumerable<string?> SentAddresses(IEnumerable<TransactionRecord> history) =>
        history
            .Where(t => t.Direction == TransactionDirection.Sent && !string.IsNullOrWhiteSpace(t.CounterpartyAddress))
            .Select(t => t.CounterpartyAddress);

    /// <summary>Destinations of the user's earlier DigiDollar sends.</summary>
    public static IEnumerable<string?> SentDigiDollarAddresses(IEnumerable<DigiDollarTransaction> history) =>
        history
            .Where(t => t.Direction == DdDirection.Sent && !string.IsNullOrWhiteSpace(t.CounterpartyAddress))
            .Select(t => t.CounterpartyAddress);

    /// <summary>Trims, and lower-cases bech32 (case-insensitive) while leaving base58 (case-sensitive) untouched.</summary>
    public static string Normalize(string? address)
    {
        var trimmed = address?.Trim() ?? "";
        return trimmed.StartsWith("dgb1", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("tdgb1", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("rdgb1", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : trimmed;
    }
}
