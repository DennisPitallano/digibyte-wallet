using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Whether a receiving address has been used (it appears in the wallet's transaction history).</summary>
public enum ReceiveAddressStatus
{
    /// <summary>The history is not known, so nothing can be said. Never shown as <see cref="Unused"/>.</summary>
    Unknown,
    Unused,
    Used,
}

/// <summary>One receiving address in the "Your addresses" list on Receive.</summary>
/// <param name="Index">Position in generation order (0 is the first address).</param>
/// <param name="Address">The SegWit address as the wallet generated it.</param>
/// <param name="Label">The user's name for it, or null.</param>
/// <param name="Status">Used, unused or unknown.</param>
/// <param name="TransactionCount">Transactions that involve it; only meaningful when used.</param>
/// <param name="IsOnShow">This is the address in the QR code now.</param>
public sealed record ReceiveAddressEntry(
    int Index, string Address, string? Label, ReceiveAddressStatus Status, int TransactionCount, bool IsOnShow);

/// <summary>
/// What the Receive page's address list shows (spec 012): the window of addresses, their names and whether they were used.
/// Pure: no I/O.
/// </summary>
public static class ReceiveAddressList
{
    /// <summary>How many addresses show at first and how many "Show more" adds.</summary>
    public const int PageSize = 10;

    /// <summary>The list size after "Show more": ten more, but never more than the number of generated addresses.</summary>
    public static int NextWindow(int currentCount, int generatedCount) =>
        Math.Min(currentCount + PageSize, generatedCount);

    /// <summary>
    /// The index range to derive for the newest <paramref name="shownCount"/> addresses when the newest is
    /// <paramref name="currentIndex"/>. The start never goes below index 0.
    /// </summary>
    public static (int StartIndex, int Count) WindowStart(int currentIndex, int shownCount)
    {
        var start = Math.Max(0, currentIndex - shownCount + 1);
        return (start, currentIndex - start + 1);
    }

    /// <summary>
    /// Builds the list entries from addresses already derived, newest first. <paramref name="labelLookup"/> is keyed by
    /// normalized address (see <see cref="ActivityAddressIndex.BuildLabelLookup"/>). With no history every status is
    /// <see cref="ReceiveAddressStatus.Unknown"/>.
    /// </summary>
    public static IReadOnlyList<ReceiveAddressEntry> Build(
        IReadOnlyList<(int Index, string Address)> addressesNewestFirst,
        IReadOnlyList<TransactionRecord>? history,
        IReadOnlyDictionary<string, string> labelLookup,
        string? onShowAddress)
    {
        var onShowKey = onShowAddress is null ? null : AddressKey.Normalize(onShowAddress);
        var entries = new List<ReceiveAddressEntry>(addressesNewestFirst.Count);
        foreach (var (index, address) in addressesNewestFirst)
        {
            var key = AddressKey.Normalize(address);
            var (status, count) = StatusOf(address, history);
            entries.Add(new ReceiveAddressEntry(
                index, address,
                labelLookup.TryGetValue(key, out var label) ? label : null,
                status, count,
                onShowKey is not null && key == onShowKey));
        }
        return entries;
    }

    /// <summary>The status and transaction count of one address, with the same rules as <see cref="Build"/>.</summary>
    public static (ReceiveAddressStatus Status, int TransactionCount) StatusOf(
        string address, IReadOnlyList<TransactionRecord>? history) =>
        (ReceiveAddressStatus.Unknown, 0);
}
