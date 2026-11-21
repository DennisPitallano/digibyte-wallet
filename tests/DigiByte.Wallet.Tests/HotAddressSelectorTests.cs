using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Which watched addresses are hot, read on every refresh (spec 024, FR-002, FR-003, FR-016, T004).</summary>
public class HotAddressSelectorTests
{
    private static readonly List<string> Watched = Enumerable.Range(0, 60).Select(i => $"dgb1qaddr{i:00}").ToList();

    private static AddressSummary Summary(string address, int ct = 0, int pt = 0, long cf = 0, long cs = 0, long pf = 0, long ps = 0) => new()
    {
        Address = address, ConfirmedTxCount = ct, PendingTxCount = pt, ConfirmedFunded = cf, ConfirmedSpent = cs,
        PendingFunded = pf, PendingSpent = ps, ReadAtUtc = DateTime.UtcNow,
    };

    private static HotSet Select(
        IReadOnlyDictionary<string, AddressSummary>? summaries = null, HotAddressHints? hints = null,
        IReadOnlySet<string>? recent = null, IReadOnlyList<string>? watched = null) =>
        HotAddressSelector.Select(new HotAddressInputs(
            watched ?? Watched,
            summaries ?? new Dictionary<string, AddressSummary>(),
            hints ?? HotAddressHints.Empty,
            recent ?? new HashSet<string>()));

    [Fact]
    public void FundedAddress_IsHot()
    {
        var set = Select(new Dictionary<string, AddressSummary> { [Watched[5]] = Summary(Watched[5], ct: 1, cf: 1000) });
        Assert.Contains(Watched[5], set.Hot);
    }

    [Fact]
    public void AddressWithOnlyPendingActivity_IsHot()
    {
        var set = Select(new Dictionary<string, AddressSummary> { [Watched[6]] = Summary(Watched[6], pt: 1, pf: 500) });
        Assert.Contains(Watched[6], set.Hot);
    }

    [Fact]
    public void EmptiedUsedAddress_WithoutHintOrRecentActivity_IsCold()
    {
        var set = Select(new Dictionary<string, AddressSummary> { [Watched[7]] = Summary(Watched[7], ct: 2, cf: 1000, cs: 1000) });
        Assert.Contains(Watched[7], set.Cold);
    }

    [Fact]
    public void Hints_AreHot()
    {
        var hints = new HotAddressHints(
            OnShow: [Watched[10], Watched[11]],
            HandedOut: [Watched[12], Watched[13], Watched[14]],
            NextChange: [Watched[40]],
            PendingSendInputs: [Watched[41]]);

        var set = Select(hints: hints);

        foreach (var i in new[] { 10, 11, 12, 13, 14, 40, 41 })
            Assert.Contains(Watched[i], set.Hot);
        Assert.Equal(7, set.Hot.Count);
    }

    [Fact]
    public void RecentActivity_IsHot()
    {
        var set = Select(recent: new HashSet<string> { Watched[20] });
        Assert.Contains(Watched[20], set.Hot);
    }

    [Fact]
    public void UnusedAddressOutsideEveryHint_IsCold()
    {
        var set = Select();
        Assert.Empty(set.Hot);
        Assert.Equal(Watched.Count, set.Cold.Count);
        Assert.True(set.Tiered);
    }

    [Fact]
    public void ColdAddress_BecomesHotOnceItsSummaryShowsActivity()
    {
        Assert.Contains(Watched[30], Select().Cold);

        var set = Select(new Dictionary<string, AddressSummary> { [Watched[30]] = Summary(Watched[30], pt: 1, pf: 100) });

        Assert.Contains(Watched[30], set.Hot);
    }

    [Fact]
    public void FewerThanTenWatched_IsNotTiered_EveryAddressIsHot()
    {
        var small = Watched.Take(HotAddressSelector.MinAddressesToTier - 1).ToList();

        var set = Select(watched: small);

        Assert.False(set.Tiered);
        Assert.Equal(small.Count, set.Hot.Count);
        Assert.Empty(set.Cold);
    }

    [Fact]
    public void HotAndCold_CoverWatched_WithoutOverlap()
    {
        var set = Select(
            new Dictionary<string, AddressSummary> { [Watched[1]] = Summary(Watched[1], ct: 1, cf: 5) },
            new HotAddressHints([Watched[2]], [Watched[3]], [Watched[4]], [Watched[1]]),
            new HashSet<string> { Watched[5], "dgb1qnotwatched" });

        Assert.Empty(set.Hot.Intersect(set.Cold));
        Assert.Equal(Watched.OrderBy(a => a), set.Hot.Concat(set.Cold).OrderBy(a => a));
        Assert.DoesNotContain("dgb1qnotwatched", set.Hot);
    }
}
