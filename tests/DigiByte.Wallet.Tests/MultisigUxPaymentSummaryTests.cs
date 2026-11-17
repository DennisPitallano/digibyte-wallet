using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>One payment summary, same fields in the same order everywhere (spec 018, FR-006).</summary>
public class MultisigUxPaymentSummaryTests
{
    private const string Addr = "dgb1qabcdefghijklmnopqrstuvwxyz0123456789";

    [Fact]
    public void Total_is_amount_plus_fee()
    {
        var s = PaymentSummaryBuilder.Build(250_000_000, 10_000, Addr, null, null);
        Assert.Equal(250_010_000, s.TotalOutSat);
    }

    [Fact]
    public void Address_is_grouped_without_changing_its_value()
    {
        var s = PaymentSummaryBuilder.Build(1, 1, Addr, null, null);
        Assert.Equal(Addr, s.RecipientAddress);
        Assert.Equal(Addr, s.RecipientGrouped.Replace(" ", ""));
        Assert.All(s.RecipientGrouped.Split(' '), g => Assert.InRange(g.Length, 1, 4));
    }

    [Fact]
    public void Empty_memo_is_omitted_and_a_memo_is_trimmed()
    {
        Assert.Null(PaymentSummaryBuilder.Build(1, 1, Addr, "   ", null).Memo);
        Assert.Equal("rent", PaymentSummaryBuilder.Build(1, 1, Addr, " rent ", null).Memo);
    }

    [Fact]
    public void Change_note_is_omitted_when_there_is_no_change_or_it_is_unknown()
    {
        Assert.Null(PaymentSummaryBuilder.Build(1, 1, Addr, null, null).ChangeNote);
        Assert.Null(PaymentSummaryBuilder.Build(1, 1, Addr, null, 0).ChangeNote);
    }

    [Fact]
    public void Change_note_says_it_returns_to_the_wallet()
    {
        var note = PaymentSummaryBuilder.Build(1, 1, Addr, null, 150_000_000).ChangeNote;
        Assert.NotNull(note);
        Assert.Contains("1.5", note);
        Assert.Contains("this wallet", note);
    }
}
