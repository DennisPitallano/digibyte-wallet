using DigiByte.Crypto.Transactions;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// DigiDollar input selection (research R15, FR-012): pick spendable outputs for an amount so the DD change is
/// exactly zero or at least $1.00 whenever the balance allows it, preferring few inputs.
/// </summary>
public class DigiDollarCoinSelectorTests
{
    private static long[] Cents(params long[] c) => c;

    private static (bool Ok, long[] Picked, long Change) Run(long[] utxos, long amount)
    {
        var r = DigiDollarCoinSelector.Select(utxos, amount);
        var picked = r.Indexes.Select(i => utxos[i]).ToArray();
        return (r.Error is null, picked, picked.Sum() - amount);
    }

    [Fact]
    public void ExactSingleMatch_IsPreferred()
    {
        var r = DigiDollarCoinSelector.Select(Cents(5000, 1000, 300), 1000);
        Assert.Null(r.Error);
        Assert.Equal([1], r.Indexes);
    }

    [Fact]
    public void ExactSubsetMatch_IsFoundWhenNoSingleOneMatches()
    {
        var (ok, picked, change) = Run(Cents(5000, 700, 300, 900), 1000);
        Assert.True(ok);
        Assert.Equal(0, change);
        Assert.Equal(new long[] { 300, 700 }, picked.OrderBy(x => x));
    }

    [Fact]
    public void LargestFirst_WhenChangeIsFine()
    {
        var (ok, picked, change) = Run(Cents(500, 5000, 200), 1000);
        Assert.True(ok);
        Assert.Equal(new long[] { 5000 }, picked);
        Assert.Equal(4000, change);
    }

    [Fact]
    public void UsesFewestInputs_ByTakingTheLargestFirst()
    {
        var (ok, picked, _) = Run(Cents(400, 400, 400, 2500), 2000);
        Assert.True(ok);
        Assert.Single(picked);
    }

    [Fact]
    public void AvoidsSubMinimumChange_ByAddingAnotherInput()
    {
        // 1000 + 1450 would leave 50 cents of change; adding a third input makes the change valid.
        var (ok, picked, change) = Run(Cents(1000, 1450, 400), 2400);
        Assert.True(ok);
        Assert.True(change == 0 || change >= 100, $"change was {change}");
        Assert.Equal(picked.Sum() - 2400, change);
    }

    [Fact]
    public void ChangeOfExactlyTheMinimum_IsAccepted()
    {
        var (ok, _, change) = Run(Cents(2500), 2400);
        Assert.True(ok);
        Assert.Equal(100, change);
    }

    [Fact]
    public void SendAll_UsesEveryInput()
    {
        var (ok, picked, change) = Run(Cents(1000, 2500, 300), 3800);
        Assert.True(ok);
        Assert.Equal(3, picked.Length);
        Assert.Equal(0, change);
    }

    [Fact]
    public void InsufficientFunds_IsReported()
    {
        var r = DigiDollarCoinSelector.Select(Cents(500, 400), 1000);
        Assert.Equal(DdBuildError.InsufficientDd, r.Error);
        Assert.Empty(r.Indexes);
    }

    [Fact]
    public void NoInputs_IsInsufficient()
    {
        Assert.Equal(DdBuildError.InsufficientDd, DigiDollarCoinSelector.Select([], 100).Error);
    }

    [Fact]
    public void WhenEveryInputIsNeeded_AndChangeStillTooSmall_ItIsReported()
    {
        // Total 2450 for an amount of 2400: the only possible change is 50 cents.
        var r = DigiDollarCoinSelector.Select(Cents(2000, 450), 2400);
        Assert.Equal(DdBuildError.ChangeBelowMinimum, r.Error);
    }

    [Fact]
    public void ChangeAboveTheMaximum_WhenOnlyAHugeInputExists_IsReported()
    {
        var r = DigiDollarCoinSelector.Select(Cents(30_000_000), 1000);
        Assert.Equal(DdBuildError.ChangeAboveMaximum, r.Error);
    }

    [Fact]
    public void ChangeAboveTheMaximum_IsAvoided_WhenSmallerInputsCanCoverTheAmount()
    {
        var (ok, picked, change) = Run(Cents(30_000_000, 5000, 4000), 8000);
        Assert.True(ok);
        Assert.Equal(new long[] { 4000, 5000 }.OrderBy(x => x), picked.OrderBy(x => x));
        Assert.Equal(1000, change);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAmount_Throws(long amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DigiDollarCoinSelector.Select(Cents(1000), amount));

    [Fact]
    public void ManyInputs_StillSelectsQuickly()
    {
        var utxos = Enumerable.Range(1, 400).Select(i => (long)(100 + i * 7)).ToArray();
        var started = DateTime.UtcNow;

        var r = DigiDollarCoinSelector.Select(utxos, 12_345);

        Assert.True((DateTime.UtcNow - started).TotalSeconds < 2);
        Assert.Null(r.Error);
        var change = r.Indexes.Sum(i => utxos[i]) - 12_345;
        Assert.True(change == 0 || change >= 100);
    }

    [Fact]
    public void IndexesAreDistinct_AndInRange()
    {
        var utxos = Cents(900, 800, 700, 600);
        var r = DigiDollarCoinSelector.Select(utxos, 2000);
        Assert.Null(r.Error);
        Assert.Equal(r.Indexes.Distinct().Count(), r.Indexes.Count);
        Assert.All(r.Indexes, i => Assert.InRange(i, 0, utxos.Length - 1));
    }
}
