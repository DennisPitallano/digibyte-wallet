using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Tests;

/// <summary>The derived values of an address summary and the change check that decides whether its history is fetched (spec 023, T000a).</summary>
public class AddressSummaryTests
{
    private static AddressSummary Make(int ct = 2, int pt = 1, long cf = 900, long cs = 300, long pf = 50, long ps = 20) => new()
    {
        Address = "dgb1qexample",
        ConfirmedTxCount = ct,
        PendingTxCount = pt,
        ConfirmedFunded = cf,
        ConfirmedSpent = cs,
        PendingFunded = pf,
        PendingSpent = ps,
        ReadAtUtc = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Confirmed_IsFundedMinusSpent()
    {
        Assert.Equal(600, Make().Confirmed);
    }

    [Fact]
    public void PendingNet_IsPendingFundedMinusPendingSpent()
    {
        Assert.Equal(30, Make().PendingNet);
    }

    [Fact]
    public void PendingNet_CanBeNegative()
    {
        Assert.Equal(-480, Make(pf: 20, ps: 500).PendingNet);
    }

    [Fact]
    public void IsUsed_FalseWithNoTransactions()
    {
        Assert.False(Make(ct: 0, pt: 0, cf: 0, cs: 0, pf: 0, ps: 0).IsUsed);
    }

    [Fact]
    public void IsUsed_TrueWithOnlyAPendingTransaction()
    {
        Assert.True(Make(ct: 0, pt: 1, cf: 0, cs: 0).IsUsed);
    }

    [Fact]
    public void DiffersFrom_FalseForEqualValues_EvenIfReadAtAnotherTime()
    {
        var a = Make();
        var b = Make() with { ReadAtUtc = a.ReadAtUtc.AddMinutes(5) };
        Assert.False(a.DiffersFrom(b));
    }

    public static TheoryData<string> ChangedFields => new()
    {
        nameof(AddressSummary.ConfirmedTxCount),
        nameof(AddressSummary.PendingTxCount),
        nameof(AddressSummary.ConfirmedFunded),
        nameof(AddressSummary.ConfirmedSpent),
        nameof(AddressSummary.PendingFunded),
        nameof(AddressSummary.PendingSpent),
    };

    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void DiffersFrom_TrueWhenAnySingleFieldChanges(string field)
    {
        var a = Make();
        var b = field switch
        {
            nameof(AddressSummary.ConfirmedTxCount) => a with { ConfirmedTxCount = a.ConfirmedTxCount + 1 },
            nameof(AddressSummary.PendingTxCount) => a with { PendingTxCount = a.PendingTxCount + 1 },
            nameof(AddressSummary.ConfirmedFunded) => a with { ConfirmedFunded = a.ConfirmedFunded + 1 },
            nameof(AddressSummary.ConfirmedSpent) => a with { ConfirmedSpent = a.ConfirmedSpent + 1 },
            nameof(AddressSummary.PendingFunded) => a with { PendingFunded = a.PendingFunded + 1 },
            _ => a with { PendingSpent = a.PendingSpent + 1 },
        };
        Assert.True(a.DiffersFrom(b));
        Assert.True(b.DiffersFrom(a));
    }

    [Fact]
    public void PendingConfirming_WithSameTotal_IsAChange()
    {
        // One pending transaction confirms: total count stays 3, but confirmed +1 and pending −1.
        var before = Make(ct: 2, pt: 1);
        var after = Make(ct: 3, pt: 0);
        Assert.True(before.DiffersFrom(after));
    }
}
