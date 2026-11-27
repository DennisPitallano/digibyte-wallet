using DigiByte.Pay.Web.Components.Pages;

namespace DigiByte.Pay.Web.Tests;

// specs/033 US1 (T017): a final payment state shows DigiDollar amounts from USD cents, and DGB from satoshis.
public class TerminalStateTests : BunitContext
{
    [Fact]
    public void An_underpaid_DD_payment_shows_dollars_from_cents()
    {
        var cut = Render<TerminalState>(p => p
            .Add(x => x.Status, "underpaid")
            .Add(x => x.Currency, "DD")
            .Add(x => x.ReceivedSatoshis, 1250)
            .Add(x => x.ExpectedSatoshis, 2000));

        Assert.Contains("12.50 DD", cut.Markup);
        Assert.Contains("20.00 DD", cut.Markup);
        Assert.DoesNotContain("DGB", cut.Markup);
    }

    [Fact]
    public void An_underpaid_DGB_payment_still_shows_DGB_from_satoshis()
    {
        var cut = Render<TerminalState>(p => p
            .Add(x => x.Status, "underpaid")
            .Add(x => x.ReceivedSatoshis, 150_000_000)
            .Add(x => x.ExpectedSatoshis, 200_000_000));

        Assert.Contains("1.5 DGB", cut.Markup);
        Assert.Contains("2 DGB", cut.Markup);
    }

    [Theory]
    [InlineData("12.50", "USD", "12.50")]
    [InlineData("12.5", "usd", "12.5")]
    [InlineData("12.50", "DD", "12.50")]
    [InlineData("1250", "PHP", "")]   // never converted
    [InlineData("12.5", "DGB", "")]   // never converted
    [InlineData("12.505", "USD", "")] // more decimals than DD takes
    public void Switching_to_DD_keeps_only_a_USD_amount(string amount, string from, string kept) =>
        Assert.Equal(kept, PosEntry.CarryToDd(amount, from));

    [Theory]
    [InlineData(0.99, false)]
    [InlineData(1.00, true)]
    [InlineData(100_000.00, true)]
    [InlineData(100_000.01, false)]
    public void DD_amounts_run_from_one_dollar_to_a_hundred_thousand(double dollars, bool ok) =>
        Assert.Equal(ok, PosEntry.DdInRange((decimal)dollars));

    [Fact]
    public void DD_takes_two_decimals_and_is_written_after_the_number()
    {
        Assert.Equal(2, PosEntry.DecimalsFor("DD"));
        Assert.Equal("DD", PosEntry.NormalizeCurrency(" dd "));
        Assert.Equal("1,250.00 DD", PosEntry.Money(1250m, "DD"));
        Assert.Equal("", PosEntry.Symbol("DD"));
        Assert.Equal("0.00", PosEntry.Placeholder("DD"));
    }
}
