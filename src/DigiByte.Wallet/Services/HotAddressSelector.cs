using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Hints from the wallet for the hot set (spec 024, FR-002): the receive address on show and the newest handed-out ones (each in
/// SegWit and, when watched, legacy format), the next change address, and the wallet's addresses a pending send spent from.
/// </summary>
public sealed record HotAddressHints(
    IReadOnlyList<string> OnShow,
    IReadOnlyList<string> HandedOut,
    IReadOnlyList<string> NextChange,
    IReadOnlyCollection<string> PendingSendInputs)
{
    public static readonly HotAddressHints Empty = new([], [], [], []);
}

/// <summary>Everything the selector looks at; all of it is already in memory, so choosing the hot set costs no request.</summary>
public sealed record HotAddressInputs(
    IReadOnlyList<string> Watched,
    IReadOnlyDictionary<string, AddressSummary> Summaries,
    HotAddressHints Hints,
    IReadOnlySet<string> RecentActivity);

/// <summary>The watched addresses split into hot (read every refresh) and cold (read on the 5-minute cycle).</summary>
public sealed record HotSet(IReadOnlySet<string> Hot, IReadOnlySet<string> Cold, bool Tiered);

/// <summary>
/// Decides which watched addresses can plausibly change and so are read on every refresh (spec 024). Pure: the result depends only
/// on the inputs, and an address joins the hot set as soon as any input shows activity on it (FR-003).
/// </summary>
public static class HotAddressSelector
{
    /// <summary>Wallets watching fewer addresses than this are not tiered (FR-016).</summary>
    public const int MinAddressesToTier = 10;

    /// <summary>How many handed-out receive addresses (by index) stay hot.</summary>
    public const int HandedOutKept = 3;

    /// <summary>Addresses with activity this recent stay hot.</summary>
    public static readonly TimeSpan RecentActivityWindow = TimeSpan.FromDays(7);

    public static HotSet Select(HotAddressInputs inputs)
    {
        var watched = inputs.Watched.Distinct().ToList();
        if (watched.Count < MinAddressesToTier)
            return new HotSet(watched.ToHashSet(), new HashSet<string>(), Tiered: false);

        var hot = new HashSet<string>();
        foreach (var address in watched)
        {
            if (inputs.Summaries.TryGetValue(address, out var s)
                && (s.Confirmed > 0 || s.PendingTxCount > 0 || s.PendingFunded > 0 || s.PendingSpent > 0))
                hot.Add(address);
        }

        var hints = inputs.Hints;
        foreach (var address in hints.OnShow.Concat(hints.HandedOut).Concat(hints.NextChange).Concat(hints.PendingSendInputs).Concat(inputs.RecentActivity))
            hot.Add(address);

        var watchedSet = watched.ToHashSet();
        hot.IntersectWith(watchedSet);
        var cold = watchedSet.Where(a => !hot.Contains(a)).ToHashSet();
        return new HotSet(hot, cold, Tiered: true);
    }
}
