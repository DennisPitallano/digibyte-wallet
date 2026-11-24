using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Which receive addresses the wallet checks (spec 027, data-model.md section 2): a base run from the first address that
/// only grows, watched indexes beyond it, and a tail of 20 after the highest paid watched index.
/// </summary>
public class ReceiveScanSetTests
{
    private static WatchedReceiveIndex P(int index) => new(index, true);
    private static WatchedReceiveIndex U(int index) => new(index, false);
    private static ReceiveScanSet Set(int next, int used, int floor, params WatchedReceiveIndex[] watched) =>
        ReceiveScanSet.From(next, used, floor, watched);
    private static int[] Run(int from, int to) => Enumerable.Range(from, to - from + 1).ToArray();

    /// <summary>The tail as "first to last", or none: the indexes after the highest paid watched index.</summary>
    private static (int First, int Last)? Tail(ReceiveScanSet set) =>
        set.MaxPaid is { } max ? (max + 1, max + ReceiveScanSet.TailLength) : null;

    private static void AssertRow(ReceiveScanSet set, int baseCount, (int, int)? tail, int checkedCount)
    {
        Assert.Equal(baseCount, set.BaseCount);
        Assert.Equal(tail, Tail(set));
        Assert.Equal(checkedCount, set.Indexes.Count);
        if (tail is { } t)
            Assert.All(Run(t.Item1, t.Item2), i => Assert.True(set.Contains(i)));
    }

    // ── With nothing watched this is exactly the behaviour before the feature ──

    [Theory]
    [InlineData(0, 0, 20, 1)]
    [InlineData(25, 0, 26, 26)]
    [InlineData(0, 30, 30, 30)]
    public void WithNothingWatched_ItIsTheOldRange(int next, int used, int expectedCount, int expectedNextNew)
    {
        var set = Set(next, used, 0);

        Assert.Equal(Run(0, expectedCount - 1), set.Indexes);
        Assert.Equal(expectedCount, set.BaseCount);
        Assert.Equal(expectedNextNew, set.NextNewIndex);
        Assert.Null(set.MaxPaid);
        Assert.Equal(used, set.Anchor);
        Assert.False(set.HandOutIsWatched);
        Assert.Empty(set.UnpaidWatched);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 30)]
    public void WithNothingWatched_TheHandOutLimitIsTwentyPastTheLastUsed(int next, int used)
    {
        Assert.True(Set(used + 18, used, 0).CanHandOutNew);   // would hand out used + 19
        Assert.False(Set(used + 19, used, 0).CanHandOutNew);  // would hand out used + 20
        Assert.True(Set(next, used, 0).CanHandOutNew);
    }

    // ── Worked example A: restored wallet, far-off find, then ordinary use ──

    [Fact]
    public void ExampleA_EveryRow()
    {
        AssertRow(Set(0, 0, 0), 20, null, 20);
        AssertRow(Set(0, 0, 0, P(3000), P(3001)), 20, (3002, 3021), 42);
        AssertRow(Set(3002, 0, 20, P(3000), P(3001), U(3002)), 20, (3002, 3021), 42);
        AssertRow(Set(3003, 0, 20, P(3000), P(3001), U(3002), U(3003)), 20, (3002, 3021), 42);
        // Row 5: the address on show is found paid. The base run must not jump to 3,004.
        AssertRow(Set(3003, 0, 20, P(3000), P(3001), U(3002), P(3003)), 20, (3004, 3023), 44);
        AssertRow(Set(3003, 0, 20, P(3000), P(3001), U(3002), P(3003), P(3010)), 20, (3011, 3030), 45);
        AssertRow(Set(3003, 0, 20, P(3000), P(3001), P(3002), P(3003), P(3010)), 20, (3011, 3030), 45);
    }

    [Fact]
    public void ExampleA_AHandedOutAddressStaysCheckedWhateverTheTailDoes()
    {
        var set = Set(3003, 0, 20, P(3000), P(3001), U(3002), P(3003), P(3010));

        Assert.True(set.Contains(3002));
        Assert.Equal([3002], set.UnpaidWatched);
    }

    [Fact]
    public void ExampleA_HandingOutAfterAFind_IsOnePastTheHighestPaid_AndIsWatched()
    {
        var set = Set(0, 0, 0, P(3000), P(3001));

        Assert.Equal(3002, set.Anchor);
        Assert.Equal(3002, set.NextNewIndex);
        Assert.True(set.CanHandOutNew);
        Assert.True(set.HandOutIsWatched);
    }

    // ── Worked example B: a wallet that had already handed out addresses past 19 ──

    [Fact]
    public void ExampleB_EveryRow()
    {
        AssertRow(Set(34, 15, 0), 35, null, 35);
        AssertRow(Set(34, 15, 0, P(3000)), 35, (3001, 3020), 56);
        // Row 3: the floor keeps 20 to 34 checked once the address on show has moved to 3,001.
        var after = Set(3001, 15, 35, P(3000), U(3001));
        AssertRow(after, 35, (3001, 3020), 56);
        Assert.All(Run(20, 34), i => Assert.True(after.Contains(i)));
    }

    [Fact]
    public void ExampleB_AFindOnePastTheBaseCount()
    {
        var set = Set(34, 15, 0, P(36));

        AssertRow(set, 35, (37, 56), 56);
        Assert.False(set.Contains(35));
        Assert.Equal(37, set.NextNewIndex);
        Assert.True(set.HandOutIsWatched);
    }

    // ── Worked example C: a payment sitting in the tail when a search runs ──

    [Fact]
    public void ExampleC_ATailIndexIsStillASearchTarget()
    {
        var set = Set(0, 0, 0, P(3000));

        Assert.True(set.Contains(3010));
        Assert.Contains(3010, set.SearchTargets(5000));
        AssertRow(Set(0, 0, 0, P(3000), P(3010), P(3500)), 20, (3501, 3520), 43);
    }

    // ── Worked example D: the base run grows over the address on show; a find next to the base run ──

    [Fact]
    public void ExampleD_EveryRow()
    {
        AssertRow(Set(46, 0, 20, P(45), U(46)), 20, (46, 65), 41);
        // Row 2: the base walk found up to 46 used. Both entries are covered; the count comes from the used count.
        var grown = Set(46, 47, 20, P(45), U(46));
        AssertRow(grown, 47, null, 47);
        Assert.Empty(grown.UnpaidWatched);
        Assert.Equal(Run(0, 46), grown.Indexes);

        AssertRow(Set(0, 0, 0, P(20)), 20, (21, 40), 41);
        AssertRow(Set(21, 0, 20, P(20), U(21)), 20, (21, 40), 41);
        // Row 5: only an unpaid entry is left, so there is no highest paid index and no tail.
        var last = Set(21, 21, 20, P(20), U(21));
        AssertRow(last, 21, null, 22);
        Assert.Null(last.MaxPaid);
        Assert.Equal([21], last.UnpaidWatched);
    }

    [Fact]
    public void ExampleD_AFindExactlyAtTheBaseCount_MakesTheNextHandOutWatched()
    {
        var set = Set(0, 0, 0, P(20));

        Assert.Equal(21, set.NextNewIndex);
        Assert.True(set.HandOutIsWatched);
    }

    // ── Worked example E: a Digi-ID sweep after a far-off find ──

    [Fact]
    public void ExampleE_EveryRow()
    {
        AssertRow(Set(0, 0, 0, P(3000)), 20, (3001, 3020), 41);
        // Row 2: destinations and the new address on show are watched entries; the base run stays at 20.
        AssertRow(Set(3004, 0, 20, P(3000), U(3001), U(3002), U(3003), U(3004)), 20, (3001, 3020), 41);
    }

    [Fact]
    public void WithoutWatchedEntries_AFarAddressOnShowWouldBeTheWholeRun()
    {
        // What the rule in "Moving the address on show" prevents: this is the shape a writer must never produce.
        Assert.Equal(3005, Set(3004, 0, 20, P(3000)).BaseCount);
    }

    // ── The parts, one by one ──

    [Fact]
    public void ShownIsWatched_IsTestedAgainstTheListAsPassed()
    {
        // With 46 as a watched entry the address on show does not feed the base count; without it, it does.
        var set = Set(46, 30, 0, U(46));
        Assert.Equal(30, set.BaseCount);

        Assert.Equal(47, Set(46, 30, 0).BaseCount);
    }

    [Fact]
    public void BaseCount_NeverDecreasesAcrossExamplesAAndB()
    {
        int[] a =
        [
            Set(0, 0, 0).BaseCount,
            Set(0, 0, 0, P(3000), P(3001)).BaseCount,
            Set(3002, 0, 20, P(3000), P(3001), U(3002)).BaseCount,
            Set(3003, 0, 20, P(3000), P(3001), U(3002), U(3003)).BaseCount,
            Set(3003, 0, 20, P(3000), P(3001), U(3002), P(3003)).BaseCount,
            Set(3003, 0, 20, P(3000), P(3001), U(3002), P(3003), P(3010)).BaseCount,
        ];
        int[] b =
        [
            Set(34, 15, 0).BaseCount,
            Set(34, 15, 0, P(3000)).BaseCount,
            Set(3001, 15, 35, P(3000), U(3001)).BaseCount,
        ];

        Assert.Equal(a.OrderBy(x => x), a);
        Assert.Equal(b.OrderBy(x => x), b);
    }

    [Theory]
    [InlineData(0, 0, false)]     // next new 1, base 20
    [InlineData(19, 0, false)]    // next new 20, base 20: contiguous
    [InlineData(25, 30, false)]   // next new 30, base 30: contiguous
    public void HandOutIsWatched_IsTrueExactlyWhenTheNewIndexIsPastTheBaseCount(int next, int used, bool expected)
    {
        var set = Set(next, used, 0);

        Assert.Equal(expected, set.HandOutIsWatched);
        Assert.Equal(set.NextNewIndex > set.BaseCount, set.HandOutIsWatched);
    }

    [Fact]
    public void CanHandOutNew_StopsTwentyPastTheAnchor()
    {
        // Anchor 3,001. Addresses 3,001 to 3,020 may be out; 3,021 may not.
        var watched = new List<WatchedReceiveIndex> { P(3000) };
        watched.AddRange(Run(3001, 3019).Select(U));
        Assert.True(ReceiveScanSet.From(3019, 0, 20, watched).CanHandOutNew);

        watched.Add(U(3020));
        var full = ReceiveScanSet.From(3020, 0, 20, watched);
        Assert.Equal(3021, full.NextNewIndex);
        Assert.Equal(full.Anchor + 20, full.NextNewIndex);
        Assert.False(full.CanHandOutNew);
    }

    [Fact]
    public void SearchTargets_AreWhatIsNotAlreadyKnown()
    {
        var set = Set(0, 0, 0, P(100), U(150));

        var targets = set.SearchTargets(200);

        Assert.DoesNotContain(targets, i => i < 20);            // the base run
        Assert.DoesNotContain(100, targets);                    // a paid watched entry
        Assert.Contains(101, targets);                          // a tail index
        Assert.Contains(150, targets);                          // an unpaid watched entry
        Assert.Equal(Run(20, 199).Where(i => i != 100), targets);
        Assert.Empty(set.SearchTargets(20));
        Assert.Empty(set.SearchTargets(0));
    }

    [Fact]
    public void UnpaidWatched_ListsUnpaidEntriesOnly()
    {
        Assert.Equal([3001, 3005], Set(0, 0, 0, P(3000), U(3001), U(3005), P(3003)).UnpaidWatched);
    }

    [Fact]
    public void ListedIndexes_AreTheBaseRunAndWatchedEntriesUpToTheAddressOnShow()
    {
        Assert.Equal(Run(0, 7), Set(7, 0, 0).ListedIndexes);

        var listed = Set(3002, 0, 20, P(3000), P(3001), U(3002), P(3010)).ListedIndexes;
        Assert.Equal([.. Run(0, 19), 3000, 3001, 3002], listed);   // no tail-only index, nothing past the address on show
    }

    [Fact]
    public void Contains_AgreesWithIndexes()
    {
        var set = Set(34, 15, 0, P(3000), U(3500));

        Assert.All(set.Indexes, i => Assert.True(set.Contains(i)));
        Assert.All(new[] { 35, 2999, 3021, 3499, 3501, -1 }, i => Assert.False(set.Contains(i)));
    }

    [Fact]
    public void Indexes_AreAscendingWithNoDuplicates()
    {
        var set = Set(3003, 0, 20, P(3001), P(3000), U(3003), U(3002), P(3000));

        Assert.Equal(set.Indexes.Distinct().OrderBy(i => i), set.Indexes);
    }

    [Fact]
    public void Fingerprint_FollowsIndexes()
    {
        Assert.Equal(Set(0, 0, 0, P(3000)).Fingerprint, Set(5, 0, 0, P(3000)).Fingerprint);      // same indexes
        Assert.NotEqual(Set(0, 0, 0, P(3000)).Fingerprint, Set(0, 0, 0, P(3001)).Fingerprint);
        Assert.NotEqual(Set(0, 0, 0).Fingerprint, Set(20, 0, 0).Fingerprint);
        Assert.NotEqual(Set(0, 0, 0, P(3000)).Fingerprint, Set(0, 0, 0, P(3000), U(3500)).Fingerprint);
    }

    [Fact]
    public void NegativeInputs_BehaveAsZero()
    {
        var set = ReceiveScanSet.From(-5, -3, -1, [P(-2), P(3000)]);

        Assert.Equal(20, set.BaseCount);
        Assert.Equal(3000, set.MaxPaid);
        Assert.Equal(Set(0, 0, 0, P(3000)).Indexes, set.Indexes);
    }

    [Fact]
    public void WatchedEntriesBelowTheBaseCount_AreIgnored_IncludingForTheHighestPaid()
    {
        var set = Set(0, 40, 0, P(30), U(35));

        Assert.Null(set.MaxPaid);
        Assert.Empty(set.UnpaidWatched);
        Assert.Equal(40, set.Anchor);
        Assert.Equal(Run(0, 39), set.Indexes);
    }

    // ── Normalize and MarkPaid ──

    [Fact]
    public void Normalize_SortsKeepsOnePerIndexWithPaidWinning_AndDropsCoveredEntries()
    {
        var result = ReceiveScanSet.Normalize(20, [U(3002), P(3000), U(3000), P(10), U(19), U(20), P(3002)]);

        Assert.Equal([U(20), P(3000), P(3002)], result);
    }

    [Fact]
    public void Normalize_RemovesNothingElse()
    {
        // Neighbours, far-apart entries, unpaid entries: all kept.
        WatchedReceiveIndex[] entries = [P(20), U(21), P(22), U(4999)];

        Assert.Equal(entries, ReceiveScanSet.Normalize(20, entries));
    }

    [Fact]
    public void MarkPaid_AddsSetsAndIgnoresCoveredIndexes()
    {
        var result = ReceiveScanSet.MarkPaid(20, [U(3002), P(3000)], [3002, 3005, 7]);

        Assert.Equal([P(3000), P(3002), P(3005)], result);
    }

    [Fact]
    public void NothingUnmarksAPaidEntry()
    {
        // Paid survives every operation the type offers (FR-014): marking with nothing, and merging with an unpaid copy.
        Assert.Equal([P(3000)], ReceiveScanSet.MarkPaid(20, [P(3000)], []));
        Assert.Equal([P(3000)], ReceiveScanSet.Normalize(20, [P(3000), U(3000)]));
        Assert.Equal([P(3000)], ReceiveScanSet.Normalize(20, [U(3000), P(3000)]));
    }
}
