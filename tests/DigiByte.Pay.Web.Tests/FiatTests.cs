using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

// Fiat amounts are written like the wallet writes them: symbol, grouping, the currency's own decimals.
public class FiatTests
{
    [Theory]
    [InlineData(1250, "PHP", "₱1,250.00")]
    [InlineData(1250, "JPY", "¥1,250")]
    [InlineData(1250.5, "KWD", "KWD 1,250.500")]
    [InlineData(12.5, "USD", "$12.50")]
    [InlineData(99.999, "EUR", "€100.00")]
    [InlineData(5, "XYZ", "XYZ 5.00")]
    public void Format_uses_the_wallet_symbol_and_decimals(decimal amount, string code, string expected) =>
        Assert.Equal(expected, Fiat.Format(amount, code));

    [Fact]
    public void Plain_is_what_a_copy_button_puts_on_the_clipboard()
    {
        Assert.Equal("1250.00", Fiat.Plain(1250m, "PHP"));
        Assert.Equal("1250", Fiat.Plain(1250.4m, "JPY"));
    }

    [Fact]
    public void Rounding_is_half_away_from_zero_at_the_currencys_precision()
    {
        Assert.Equal(1.13m, Fiat.Round(1.125m, "USD"));
        Assert.Equal(13m, Fiat.Round(12.5m, "JPY"));
    }

    [Fact]
    public void Converting_to_dgb_never_asks_for_less_than_the_quote()
    {
        // 10 USD at 0.03 USD/DGB is 333.333333333… DGB: rounded on the satoshi grid.
        Assert.Equal(333.33333333m, Fiat.ToDgb(10m, 0.03m));
        Assert.Equal(0.00000002m, Fiat.ToDgb(0.000000015m, 1m)); // half a satoshi rounds up
        Assert.Equal(0m, Fiat.ToDgb(10m, 0m));
    }

    [Fact]
    public void Small_dgb_prices_keep_enough_digits()
    {
        Assert.Equal("$0.009812", Fiat.Rate(0.009812m, "USD"));
        Assert.Equal("₱0.5825", Fiat.Rate(0.5825m, "PHP"));
    }
}
