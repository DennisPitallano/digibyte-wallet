using System.Globalization;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>FR-004 / FR-025: DigiDollar amounts are US dollars with two decimals, formatted for the user's locale.</summary>
public class DigiDollarFormatTests
{
    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(1, "$0.01")]
    [InlineData(100, "$1.00")]
    [InlineData(2550, "$25.50")]
    [InlineData(123456, "$1,234.56")]
    [InlineData(10_000_000, "$100,000.00")]
    [InlineData(-100, "-$1.00")]
    [InlineData(-5, "-$0.05")]
    public void English_FormatsCentsAsDollars(long cents, string expected) =>
        Assert.Equal(expected, DigiDollarFormat.Usd(cents, DigiDollarFormat.CultureFor("en")));

    [Fact]
    public void Spanish_UsesItsDecimalAndGroupSeparators()
    {
        var es = DigiDollarFormat.CultureFor("es");
        var text = DigiDollarFormat.Usd(123456, es);

        Assert.StartsWith("$", text);
        Assert.Contains("1.234,56", text);
    }

    [Fact]
    public void AlwaysTwoDecimals_AndAlwaysADollarSign()
    {
        foreach (var locale in new[] { "en", "es", "fr", "pt", "ja", "ar", "hi", "tl", "ko", "zh" })
        {
            var text = DigiDollarFormat.Usd(5, DigiDollarFormat.CultureFor(locale));
            Assert.Contains("$", text);
            Assert.True(text.TrimEnd().Length >= 5, $"{locale}: {text}"); // $0.05 or locale equivalent
        }
    }

    [Theory]
    [InlineData("zz-NOT-A-LOCALE")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownLocale_FallsBackToInvariant_NotAnException(string? locale)
    {
        var culture = DigiDollarFormat.CultureFor(locale);
        Assert.Equal(CultureInfo.InvariantCulture.Name, culture.Name);
        Assert.Equal("$1.00", DigiDollarFormat.Usd(100, culture));
    }

    [Theory]
    [InlineData("25", 2500)]
    [InlineData("25.5", 2550)]
    [InlineData("25.50", 2550)]
    [InlineData("0.01", 1)]
    [InlineData("1,234.56", 123456)]
    [InlineData("$25.00", 2500)]
    [InlineData(" 25.00 ", 2500)]
    [InlineData("100000", 10_000_000)]
    [InlineData(".5", 50)]
    public void ParsesDollarsToExactCents_InEnglish(string text, long expected)
    {
        Assert.True(DigiDollarFormat.TryParseUsdToCents(text, CultureInfo.GetCultureInfo("en"), out var cents));
        Assert.Equal(expected, cents);
    }

    [Fact]
    public void ParsesWithTheLocalesSeparators_Spanish()
    {
        var es = CultureInfo.GetCultureInfo("es");
        Assert.True(DigiDollarFormat.TryParseUsdToCents("1.234,56", es, out var cents));
        Assert.Equal(123456, cents);
        Assert.True(DigiDollarFormat.TryParseUsdToCents("25,5", es, out cents));
        Assert.Equal(2550, cents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12.345")]    // more than two decimals: never silently rounded
    [InlineData("1e3")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("1,2,3")]
    [InlineData("25..00")]
    [InlineData("$")]
    [InlineData("99999999999999999999")]
    public void RejectsAnythingThatIsNotAPositiveAmountInCents(string text) =>
        Assert.False(DigiDollarFormat.TryParseUsdToCents(text, CultureInfo.GetCultureInfo("en"), out _));

    [Fact]
    public void RejectsNull() =>
        Assert.False(DigiDollarFormat.TryParseUsdToCents(null, CultureInfo.InvariantCulture, out _));

    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        var en = CultureInfo.GetCultureInfo("en");
        foreach (var cents in new long[] { 1, 99, 100, 2550, 123456, 10_000_000 })
        {
            var text = DigiDollarFormat.Usd(cents, en);
            Assert.True(DigiDollarFormat.TryParseUsdToCents(text, en, out var parsed), text);
            Assert.Equal(cents, parsed);
        }
    }

    [Fact]
    public void Dgb_FormatsSatoshisAsDgb_WithoutTrailingZeros()
    {
        Assert.Equal("0.1", DigiDollarFormat.Dgb(10_000_000, CultureInfo.InvariantCulture));
        Assert.Equal("1", DigiDollarFormat.Dgb(100_000_000, CultureInfo.InvariantCulture));
        Assert.Equal("0.12345678", DigiDollarFormat.Dgb(12_345_678, CultureInfo.InvariantCulture));
        Assert.Equal("0", DigiDollarFormat.Dgb(0, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1000, 1000, 0)]
    [InlineData(1000, 998, 30)]   // 2 blocks of 15 s
    [InlineData(1000, 980, 300)]  // 20 blocks: the stale threshold
    [InlineData(1000, 1002, 0)]   // an oracle height ahead of the node tip is never negative
    public void OracleAge_IsBlocksTimesFifteenSeconds(int nodeHeight, int oracleHeight, int expectedSeconds)
    {
        var health = new DigiByte.Wallet.Models.NetworkHealth
        {
            Available = true, NodeHeight = nodeHeight, OracleHeight = oracleHeight, OraclePriceMicroUsd = 6310,
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DigiDollarFormat.OracleAge(health));
    }

    [Fact]
    public void OracleAge_IsNull_WithoutAHeight()
    {
        var health = new DigiByte.Wallet.Models.NetworkHealth { Available = true, NodeHeight = 10, OraclePriceMicroUsd = 6310 };
        Assert.Null(DigiDollarFormat.OracleAge(health));
    }

    [Fact]
    public void MicroUsdPerDgb_FormatsAsAFourDecimalPrice()
    {
        // The oracle reports USD per DGB times 10^6 (data-model NetworkHealth); show it as a price per DGB.
        Assert.Equal("$0.0063", DigiDollarFormat.DgbPrice(6310, CultureInfo.InvariantCulture));
        Assert.Equal("$0.0100", DigiDollarFormat.DgbPrice(10_000, CultureInfo.InvariantCulture));
        Assert.Equal("$12.5000", DigiDollarFormat.DgbPrice(12_500_000, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DgbUsdValue_IsBalanceTimesPrice_RoundedToCents()
    {
        // 1,000 DGB at 0.00631 USD = 6.31 USD
        Assert.Equal(631, DigiDollarFormat.DgbValueCents(100_000_000_000L, 6310));
        Assert.Equal(0, DigiDollarFormat.DgbValueCents(0, 6310));
        Assert.Equal(0, DigiDollarFormat.DgbValueCents(100_000_000, 0));
        // 0.5 DGB at 0.00631: below half a cent, rounds to 0
        Assert.Equal(0, DigiDollarFormat.DgbValueCents(50_000_000, 6310));
    }
}
