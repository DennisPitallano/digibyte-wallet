using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A request made in the person's own currency fixes its DGB amount at the price shown, for 15 minutes (spec 036,
/// FR-016 to FR-018, research R5).
/// </summary>
public class RequestAmountsTests
{
    [Fact]
    public void ToDgb_DividesByThePrice_RoundedHalfUpTo8Decimals()
    {
        // 10 / 3 = 3.333333333… → 3.33333333; 2 / 3 = 0.666666666… → 0.66666667
        Assert.Equal(3.33333333m, RequestAmounts.ToDgb(10m, 3m, "USD"));
        Assert.Equal(0.66666667m, RequestAmounts.ToDgb(2m, 3m, "USD"));
        Assert.Equal(125m, RequestAmounts.ToDgb(12.50m, 0.1m, "USD"));
    }

    [Fact]
    public void ToDgb_FirstRoundsTheCurrencyAmountToItsDecimals()
    {
        // JPY has no decimals: 100.4 yen is 100 yen; USD has 2: 1.005 is 1.01.
        Assert.Equal(0, FiatCurrencies.DecimalsFor("JPY"));
        Assert.Equal(RequestAmounts.ToDgb(100m, 2m, "JPY"), RequestAmounts.ToDgb(100.4m, 2m, "JPY"));
        Assert.Equal(RequestAmounts.ToDgb(1.01m, 1m, "USD"), RequestAmounts.ToDgb(1.005m, 1m, "USD"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToDgb_RejectsAPriceThatIsNotPositive(decimal price)
    {
        Assert.Null(RequestAmounts.ToDgb(10m, price, "USD"));
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData(null)]
    public void ToDgb_RejectsAnUnknownCurrency(string? currency)
    {
        Assert.Null(RequestAmounts.ToDgb(10m, 1m, currency));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ToDgb_RejectsAnAmountThatIsNotPositive(decimal amount)
    {
        Assert.Null(RequestAmounts.ToDgb(amount, 1m, "USD"));
    }

    [Fact]
    public void ToDgb_RejectsAnAmountTooSmallToBeAnyDgb()
    {
        Assert.Null(RequestAmounts.ToDgb(0.01m, 100_000_000_000m, "USD"));
    }

    [Fact]
    public void ExpiresAt_IsFifteenMinutesAfterCreation()
    {
        var created = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(created.AddMinutes(15), RequestAmounts.ExpiresAt(created));
        Assert.Equal(TimeSpan.FromMinutes(15), RequestAmounts.PriceLock);
    }

    [Fact]
    public void MissingDgb_IsTheAmountLessWhatConfirmed()
    {
        var r = new PaymentRequest
        {
            Id = "r",
            Address = "a",
            AmountDgb = 100m,
            Payments =
            [
                new RequestPayment { TxId = "t1", AmountSatoshis = 40 * 100_000_000L, Confirmed = true },
                new RequestPayment { TxId = "t2", AmountSatoshis = 10 * 100_000_000L, Confirmed = false },
            ],
        };

        Assert.Equal(60m, RequestAmounts.MissingDgb(r));
    }

    [Fact]
    public void MissingDgb_IsZeroOnceFullyPaid_AndNullWithoutAnAmount()
    {
        var paid = new PaymentRequest
        {
            Id = "r", Address = "a", AmountDgb = 1m,
            Payments = [new RequestPayment { TxId = "t", AmountSatoshis = 2 * 100_000_000L, Confirmed = true }],
        };
        Assert.Equal(0m, RequestAmounts.MissingDgb(paid));
        Assert.Null(RequestAmounts.MissingDgb(new PaymentRequest { Id = "r", Address = "a" }));
    }

    [Fact]
    public void RestInCurrency_IsTheMissingDgbAtTheCurrentPrice_RoundedToTheCurrency()
    {
        // 60 DGB missing at 0.123 USD → 7.38 USD
        Assert.Equal(7.38m, RequestAmounts.RestInCurrency(60m, 0.123m, "USD"));
        Assert.Null(RequestAmounts.RestInCurrency(60m, 0m, "USD"));
        Assert.Null(RequestAmounts.RestInCurrency(0m, 0.1m, "USD"));
        Assert.Null(RequestAmounts.RestInCurrency(60m, 0.1m, "XYZ"));
    }
}
