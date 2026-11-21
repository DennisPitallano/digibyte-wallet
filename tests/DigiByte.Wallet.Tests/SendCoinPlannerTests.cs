using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Which addresses a send reads coins from first (spec 024, FR-010, FR-011, T006).</summary>
public class SendCoinPlannerTests
{
    private static readonly List<string> Addresses = Enumerable.Range(0, 20).Select(i => $"dgb1qaddr{i:00}").ToList();

    private static Dictionary<string, AddressSummary> Summaries(params (int Index, long Confirmed, int Pending)[] funded)
    {
        var map = Addresses.ToDictionary(a => a, a => new AddressSummary { Address = a, ReadAtUtc = DateTime.UtcNow });
        foreach (var (i, confirmed, pending) in funded)
            map[Addresses[i]] = map[Addresses[i]] with { ConfirmedTxCount = 1, ConfirmedFunded = confirmed, PendingTxCount = pending, PendingFunded = pending * 10 };
        return map;
    }

    [Fact]
    public void WithSummaries_FundedHasBalanceOrPending_RestHasTheOthers()
    {
        var plan = SendCoinPlanner.Plan(Addresses, Summaries((2, 5000, 0), (7, 0, 1)));

        Assert.True(plan.Planned);
        Assert.Equal([Addresses[2], Addresses[7]], plan.Funded);
        Assert.Equal(Addresses.Count - 2, plan.Rest.Count);
        Assert.DoesNotContain(Addresses[2], plan.Rest);
    }

    [Fact]
    public void WithoutSummaries_EveryAddressIsFunded_NothingIsLeft()
    {
        var plan = SendCoinPlanner.Plan(Addresses, null);

        Assert.False(plan.Planned);
        Assert.Equal(Addresses, plan.Funded);
        Assert.Empty(plan.Rest);
    }

    [Fact]
    public void ASummaryMissingForAnyAddress_MeansNoPlan()
    {
        var partial = Summaries((2, 5000, 0));
        partial.Remove(Addresses[9]);

        var plan = SendCoinPlanner.Plan(Addresses, partial);

        Assert.False(plan.Planned);
        Assert.Equal(Addresses, plan.Funded);
    }

    [Theory]
    [InlineData(100_000_000, 99_000_000, true)]   // short of amount + 0.01 DGB
    [InlineData(100_000_000, 100_500_000, true)]  // covers the amount but not the fee allowance
    [InlineData(100_000_000, 101_000_000, false)] // amount + 0.01 DGB covered
    public void NeedsRest_ComparesWithAmountPlusFeeAllowance(long amount, long found, bool expected)
    {
        var plan = SendCoinPlanner.Plan(Addresses, Summaries((2, found, 0)));
        Assert.Equal(expected, SendCoinPlanner.NeedsRest(plan, found, amount));
    }

    [Fact]
    public void NeedsRest_IsFalseWhenNothingIsLeftToRead()
    {
        var plan = SendCoinPlanner.Plan(Addresses, null);
        Assert.False(SendCoinPlanner.NeedsRest(plan, 0, 100_000_000));
    }

    [Theory]
    [InlineData(5000, 4000, true)]
    [InlineData(5000, 5000, false)]
    public void NeedsRestForConsolidate_ComparesWithTheSummariesConfirmedTotal(long summariesConfirmed, long found, bool expected)
    {
        var plan = SendCoinPlanner.Plan(Addresses, Summaries((2, summariesConfirmed, 0)));
        Assert.Equal(expected, SendCoinPlanner.NeedsRestForConsolidate(plan, found, summariesConfirmed));
    }
}
