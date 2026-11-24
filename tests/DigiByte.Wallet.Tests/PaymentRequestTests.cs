using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Payment links are read by other wallets, so their amount always uses a point, whatever language the
/// wallet shows (spec 028, research R5). These run under Spanish, where the display decimal mark is a comma.
/// </summary>
public class PaymentRequestTests
{
    private const string Address = "dgb1qexampleaddressforpaymentlinktests0000";

    [Fact]
    public void ToUri_WritesTheAmountWithAPoint_UnderSpanish()
    {
        using var _ = new CultureScope("es");

        var uri = new PaymentRequest { Id = "", Address = Address, AmountDgb = 1.5m }.ToUri();

        Assert.Equal($"digibyte:{Address}?amount=1.50000000", uri);
    }

    [Fact]
    public void FromUri_ReadsAPointAmount_UnderSpanish()
    {
        using var _ = new CultureScope("es");

        var request = PaymentRequest.FromUri($"digibyte:{Address}?amount=1.5");

        Assert.NotNull(request);
        Assert.Equal(1.5m, request!.AmountDgb);
    }

    [Theory]
    [InlineData("1,5")]      // a comma is not valid in a payment link and must not read as 15
    [InlineData("1,000.5")]  // nor a thousands mark
    [InlineData("1.000,5")]
    public void FromUri_LeavesTheAmountEmpty_WhenItHasACommaOrThousandsMark(string amount)
    {
        using var _ = new CultureScope("es");

        var request = PaymentRequest.FromUri($"digibyte:{Address}?amount={amount}");

        Assert.NotNull(request);
        Assert.Equal(Address, request!.Address);
        Assert.Null(request.AmountDgb);
    }

    [Fact]
    public void ToUriThenFromUri_KeepsTheAmount_UnderEnglishAndSpanish()
    {
        foreach (var culture in new[] { "en-US", "es" })
        {
            using var _ = new CultureScope(culture);
            var uri = new PaymentRequest { Id = "", Address = Address, AmountDgb = 1234.56789012m }.ToUri();

            Assert.Equal(1234.56789012m, PaymentRequest.FromUri(uri)!.AmountDgb);
        }
    }
}
