using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The planner is pure: it decides which Digi-ID addresses are worth sweeping, what each would cost, and which fresh receive
/// address each pays to (FR-008a, FR-009, FR-010). No key and no network is involved.
/// </summary>
public class DigiIdSweepPlannerTests
{
    private const long FeeOneInputAt100 = 19_200; // (10 + 148 + 34) vB x 100 sat/vB

    private static DigiIdSweepCandidate Candidate(int n, long amount = 5_000_000, int inputs = 1, bool inFlight = false, bool failed = false) =>
        new($"site{n}.example", $"DSource{n}", amount, inputs, inFlight, failed);

    private static IReadOnlyList<(int Index, string Address)> Destinations(int count, int firstIndex = 10) =>
        Enumerable.Range(0, count).Select(i => (firstIndex + i, $"dgb1qdest{firstIndex + i}")).ToList();

    private static DigiIdSweepPlan Plan(IReadOnlyList<DigiIdSweepCandidate> candidates, int feeRate = 100, int? maxItems = null, int destinations = 30) =>
        DigiIdSweepPlanner.Build(candidates, feeRate, maxItems ?? DigiIdSweepPlanner.MaxItemsPerRound, Destinations(destinations));

    // ─── Fee ───

    [Theory]
    [InlineData(1, 19_200)]   // 10 + 148 + 34 = 192 vB
    [InlineData(2, 34_000)]   // 10 + 296 + 34 = 340 vB
    [InlineData(3, 48_800)]   // 10 + 444 + 34 = 488 vB
    public void EstimateFee_UsesTheLegacyInputSize_AtTheFloorRate(int inputs, long expected)
    {
        Assert.Equal(expected, DigiIdSweepPlanner.EstimateFee(inputs, 100));
    }

    [Fact]
    public void EstimateFee_NeverUsesARateBelowTheFloor()
    {
        Assert.Equal(DigiIdSweepPlanner.EstimateFee(1, 100), DigiIdSweepPlanner.EstimateFee(1, 5));
        Assert.Equal(DigiIdSweepPlanner.EstimateFee(1, 100), DigiIdSweepPlanner.EstimateFee(1, 0));
        Assert.Equal(DigiIdSweepPlanner.EstimateFee(1, 100), DigiIdSweepPlanner.EstimateFee(1, -3));
    }

    [Fact]
    public void EstimateFee_AHigherRateCostsMore()
    {
        Assert.Equal(2 * FeeOneInputAt100, DigiIdSweepPlanner.EstimateFee(1, 200));
    }

    [Fact]
    public void Build_RecordsTheRateActuallyUsed_NotTheOneAskedFor()
    {
        var plan = Plan([Candidate(1)], feeRate: 5);

        Assert.Equal(100, plan.FeeRateSatPerVByte);
        Assert.Equal(FeeOneInputAt100, plan.Items.Single().EstimatedFeeSatoshis);
    }

    // ─── Too small (FR-010) ───

    [Fact]
    public void Build_NetExactlyTheMinimum_IsIncluded()
    {
        var plan = Plan([Candidate(1, amount: FeeOneInputAt100 + DigiIdSweepPlanner.MinNetSatoshis)]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(DigiIdSweepPlanner.MinNetSatoshis, item.NetSatoshis);
    }

    [Fact]
    public void Build_NetOneSatoshiBelowTheMinimum_IsExcludedAsTooSmall()
    {
        var plan = Plan([Candidate(1, amount: FeeOneInputAt100 + DigiIdSweepPlanner.MinNetSatoshis - 1)]);

        Assert.Empty(plan.Items);
        var excluded = Assert.Single(plan.Excluded);
        Assert.Equal(DigiIdSweepExclusionKind.TooSmall, excluded.Kind);
        Assert.Contains("too small", excluded.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(19_199)]
    [InlineData(19_200)]
    public void Build_FundsNotAboveTheFee_NeverGiveAnItem_OrANegativeNet(long amount)
    {
        var plan = Plan([Candidate(1, amount: amount)]);

        Assert.Empty(plan.Items);
        Assert.Equal(DigiIdSweepExclusionKind.TooSmall, Assert.Single(plan.Excluded).Kind);
    }

    [Fact]
    public void Build_TheMinimumIs10000Satoshis_ThatIs0Point0001Dgb()
    {
        Assert.Equal(10_000, DigiIdSweepPlanner.MinNetSatoshis);
    }

    // ─── Other exclusions ───

    [Fact]
    public void Build_NothingConfirmed_IsExcluded()
    {
        var plan = Plan([Candidate(1, amount: 0, inputs: 0)]);

        Assert.Empty(plan.Items);
        Assert.Equal(DigiIdSweepExclusionKind.NothingConfirmed, Assert.Single(plan.Excluded).Kind);
    }

    [Fact]
    public void Build_AddressAlreadyBeingSwept_IsExcluded()
    {
        var plan = Plan([Candidate(1, inFlight: true), Candidate(2)]);

        Assert.Equal(["DSource2"], plan.Items.Select(i => i.SourceAddress));
        var excluded = Assert.Single(plan.Excluded);
        Assert.Equal(DigiIdSweepExclusionKind.InFlight, excluded.Kind);
        Assert.Equal("DSource1", excluded.SourceAddress);
    }

    [Fact]
    public void Build_AddressThatCouldNotBeRechecked_IsExcludedWithItsReason()
    {
        var plan = Plan([Candidate(1, failed: true), Candidate(2)]);

        Assert.Equal(["DSource2"], plan.Items.Select(i => i.SourceAddress));
        Assert.Equal(DigiIdSweepExclusionKind.LookupFailed, Assert.Single(plan.Excluded).Kind);
    }

    [Fact]
    public void Build_EveryExclusionExplainsItself()
    {
        var plan = Plan([Candidate(1, amount: 1), Candidate(2, amount: 0, inputs: 0), Candidate(3, inFlight: true), Candidate(4, failed: true)]);

        Assert.Equal(4, plan.Excluded.Count);
        Assert.All(plan.Excluded, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason)));
    }

    // ─── Cap (research R-4) ───

    [Fact]
    public void MaxItemsPerRound_IsOneBelowTheWalletsUnusedAddressLimit()
    {
        Assert.Equal(WalletService.ReceiveGapLimit - 1, DigiIdSweepPlanner.MaxItemsPerRound);
        Assert.Equal(19, DigiIdSweepPlanner.MaxItemsPerRound);
    }

    [Fact]
    public void Build_MoreEligibleThanTheCap_TakesTheFirstOnesAndCountsTheRest()
    {
        var candidates = Enumerable.Range(1, 25).Select(n => Candidate(n)).ToList();

        var plan = Plan(candidates);

        Assert.Equal(19, plan.Items.Count);
        Assert.Equal(6, plan.Remaining);
        Assert.Equal(candidates.Take(19).Select(c => c.SourceAddress), plan.Items.Select(i => i.SourceAddress));
    }

    [Fact]
    public void Build_ExclusionsDoNotUseUpTheCap()
    {
        var candidates = Enumerable.Range(1, 22).Select(n => Candidate(n, inFlight: n <= 3)).ToList();

        var plan = Plan(candidates);

        Assert.Equal(19, plan.Items.Count);
        Assert.Equal(0, plan.Remaining);
        Assert.Equal(3, plan.Excluded.Count);
    }

    [Fact]
    public void Build_ALowerCapThanTheDefault_IsHonouredAndTheRestIsRemaining()
    {
        var plan = Plan([Candidate(1), Candidate(2), Candidate(3), Candidate(4)], maxItems: 2);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(2, plan.Remaining);
    }

    [Fact]
    public void Build_NoRoomAtAll_GivesNoItems_AndEverythingRemaining()
    {
        var plan = Plan([Candidate(1), Candidate(2)], maxItems: 0);

        Assert.Empty(plan.Items);
        Assert.Equal(2, plan.Remaining);
    }

    // ─── Destinations (FR-008a) ───

    [Fact]
    public void Build_GivesEachItemItsOwnDestination_InOrder()
    {
        var plan = Plan([Candidate(1), Candidate(2), Candidate(3)]);

        Assert.Equal(["dgb1qdest10", "dgb1qdest11", "dgb1qdest12"], plan.Items.Select(i => i.DestinationAddress));
        Assert.Equal([10, 11, 12], plan.Items.Select(i => i.DestinationIndex));
        Assert.Equal(plan.Items.Count, plan.Items.Select(i => i.DestinationAddress).Distinct().Count());
    }

    [Fact]
    public void Build_ExcludedItemsDoNotConsumeADestination()
    {
        var plan = Plan([Candidate(1, inFlight: true), Candidate(2), Candidate(3)]);

        Assert.Equal([10, 11], plan.Items.Select(i => i.DestinationIndex));
    }

    [Fact]
    public void Build_NoDestinationIsASourceAddress()
    {
        var plan = Plan(Enumerable.Range(1, 5).Select(n => Candidate(n)).ToList());

        var sources = plan.Items.Select(i => i.SourceAddress).ToHashSet();
        Assert.DoesNotContain(plan.Items, i => sources.Contains(i.DestinationAddress));
    }

    [Fact]
    public void Build_FewerDestinationsThanItems_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DigiIdSweepPlanner.Build([Candidate(1), Candidate(2), Candidate(3)], 100, 19, Destinations(2)));
    }

    [Fact]
    public void Build_DuplicateDestinations_Throws()
    {
        var dupes = new List<(int Index, string Address)> { (10, "dgb1qsame"), (11, "dgb1qsame") };

        Assert.Throws<ArgumentException>(() => DigiIdSweepPlanner.Build([Candidate(1), Candidate(2)], 100, 19, dupes));
    }

    // ─── Totals and shape ───

    [Fact]
    public void Build_TotalsEqualTheSumsOfTheItems()
    {
        var plan = Plan([Candidate(1, amount: 5_000_000), Candidate(2, amount: 3_000_000, inputs: 3), Candidate(3, amount: 800_000, inputs: 2)]);

        Assert.Equal(plan.Items.Sum(i => i.AmountSatoshis), plan.TotalAmountSatoshis);
        Assert.Equal(plan.Items.Sum(i => i.EstimatedFeeSatoshis), plan.TotalFeeSatoshis);
        Assert.Equal(plan.Items.Sum(i => i.NetSatoshis), plan.TotalNetSatoshis);
        Assert.Equal(plan.TotalAmountSatoshis - plan.TotalFeeSatoshis, plan.TotalNetSatoshis);
        Assert.Equal(8_800_000, plan.TotalAmountSatoshis);
    }

    [Fact]
    public void Build_FeeGrowsWithTheNumberOfInputsOnAnAddress()
    {
        var plan = Plan([Candidate(1, inputs: 1), Candidate(2, inputs: 4)]);

        Assert.True(plan.Items[1].EstimatedFeeSatoshis > plan.Items[0].EstimatedFeeSatoshis);
        Assert.Equal(DigiIdSweepPlanner.EstimateFee(4, 100), plan.Items[1].EstimatedFeeSatoshis);
    }

    [Fact]
    public void Build_NetIsAlwaysAmountMinusEstimatedFee()
    {
        var plan = Plan(Enumerable.Range(1, 6).Select(n => Candidate(n, amount: 1_000_000L * n, inputs: n)).ToList());

        Assert.All(plan.Items, i => Assert.Equal(i.AmountSatoshis - i.EstimatedFeeSatoshis, i.NetSatoshis));
    }

    [Fact]
    public void Build_NoCandidates_GivesAnEmptyPlan()
    {
        var plan = Plan([]);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.Excluded);
        Assert.Equal(0, plan.Remaining);
        Assert.Equal(0, plan.TotalNetSatoshis);
    }

    [Fact]
    public void Build_SameSourceAddressTwice_IsPlannedOnce()
    {
        var plan = Plan([Candidate(1), Candidate(1)]);

        Assert.Single(plan.Items);
    }

    [Fact]
    public void Build_ItemsCarryTheSiteAndSource()
    {
        var plan = Plan([Candidate(7)]);

        var item = Assert.Single(plan.Items);
        Assert.Equal("site7.example", item.Domain);
        Assert.Equal("DSource7", item.SourceAddress);
        Assert.Equal(5_000_000, item.AmountSatoshis);
    }
}
