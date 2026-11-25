using PriceArchiveImport;

namespace DigiByte.Api.Tests;

/// <summary>The import tool that seeds and refreshes the price archive (spec 031, research R3, tools/PriceArchiveImport).</summary>
public class PriceArchiveImportTests
{
    [Fact]
    public void CoinGeckoExport_BecomesOneLinePerUtcDay_Ascending_NoDuplicates()
    {
        const string export = """
            snapped_at,price,market_cap,total_volume
            2026-11-21 00:00:00 UTC,0.0115,190000000,3000000
            2026-11-20 00:00:00 UTC,0.0112,185000000,2900000
            2026-11-20 00:00:00 UTC,0.0113,185000000,2900000
            """;

        var days = ArchiveFormat.FromCoinGeckoExport(new StringReader(export));

        Assert.Equal(["2026-11-20,0.0113", "2026-11-21,0.0115"], ArchiveFormat.Lines(days));
    }

    [Theory]
    [InlineData("2026-11-20 00:00:00 UTC,,1,1")]
    [InlineData("2026-11-20 00:00:00 UTC,0,1,1")]
    public void CoinGeckoExport_DropsEmptyAndZeroPrices(string row)
    {
        var days = ArchiveFormat.FromCoinGeckoExport(new StringReader("snapped_at,price,market_cap,total_volume\n" + row));
        Assert.Empty(days);
    }

    [Fact]
    public void FrankfurterReply_BecomesRatesPerDay()
    {
        const string json = """{"amount":1.0,"base":"USD","start_date":"2026-11-20","end_date":"2026-11-23","rates":{"2026-11-23":{"PHP":58.2},"2026-11-20":{"PHP":58.1}}}""";

        var rates = ArchiveFormat.FromFrankfurter(json, "PHP");

        Assert.Equal(["2026-11-20,58.1", "2026-11-23,58.2"], ArchiveFormat.Lines(rates));
    }

    [Fact]
    public void ExchangeApiSnapshot_GivesThatDaysRate()
    {
        const string json = """{"date":"2026-11-20","usd":{"kes":129.5,"php":58.1}}""";

        var (day, rate) = ArchiveFormat.FromExchangeApi(json, "KES")!.Value;

        Assert.Equal(new DateOnly(2026, 11, 20), day);
        Assert.Equal(129.5m, rate);
    }

    [Fact]
    public void ExchangeApiSnapshot_WithoutTheCurrency_IsNull() =>
        Assert.Null(ArchiveFormat.FromExchangeApi("""{"date":"2026-11-20","usd":{"php":58.1}}""", "KES"));

    [Fact]
    public void Append_AddsOnlyDaysAfterTheLastOne_AndNeverToday()
    {
        var existing = new SortedDictionary<DateOnly, decimal> { [new DateOnly(2026, 11, 20)] = 1m };
        var fresh = new SortedDictionary<DateOnly, decimal>
        {
            [new DateOnly(2026, 11, 19)] = 9m,   // older: ignored
            [new DateOnly(2026, 11, 20)] = 9m,   // already there: kept as it was
            [new DateOnly(2026, 11, 21)] = 2m,
            [new DateOnly(2026, 11, 26)] = 3m,   // today: unfinished day, left out
        };

        var added = ArchiveFormat.Append(existing, fresh, today: new DateOnly(2026, 11, 26));

        Assert.Equal(1, added);
        Assert.Equal(["2026-11-20,1", "2026-11-21,2"], ArchiveFormat.Lines(existing));
    }
}
