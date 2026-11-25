using DigiByte.Api.Services;

namespace DigiByte.Api.Tests;

/// <summary>The price service's daily archive files (spec 031, research R3): loading, range lookup and the rate carry-forward.</summary>
public class PriceArchiveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("price-archive-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void Write(string file, params string[] lines) => File.WriteAllLines(Path.Combine(_dir, file), lines);

    [Fact]
    public void LoadsPricesAndRates()
    {
        Write("dgb-daily-usd.csv", "2026-11-20,0.0112", "2026-11-21,0.0115");
        Write("usd-fx-kes.csv", "2026-11-20,129.5");
        var archive = new PriceArchive(_dir);

        Assert.Equal(0.0115m, archive.Prices("usd")![new DateOnly(2026, 11, 21)]);
        Assert.Equal(129.5m, archive.Rates("KES")![new DateOnly(2026, 11, 20)]);
    }

    [Fact]
    public void LoadsEachFileOncePerProcess()
    {
        Write("dgb-daily-usd.csv", "2026-11-20,0.0112");
        var archive = new PriceArchive(_dir);
        var first = archive.Prices("usd");
        Write("dgb-daily-usd.csv", "2026-11-20,99");
        Assert.Same(first, archive.Prices("usd"));
    }

    [Fact]
    public void Range_IsAscendingAndInclusive_AndMissingDaysStayAbsent()
    {
        Write("dgb-daily-usd.csv", "2026-11-19,1", "2026-11-20,2", "2026-11-22,4", "2026-11-23,5");
        var archive = new PriceArchive(_dir);

        var range = PriceArchive.Range(archive.Prices("usd")!, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22));

        Assert.Equal([new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22)], range.Keys);
    }

    [Fact]
    public void UnknownCurrency_HasNoArchive()
    {
        var archive = new PriceArchive(_dir);
        Assert.Null(archive.Prices("eur"));
        Assert.Null(archive.Rates("kes"));
    }

    [Fact]
    public void MissingDirectory_HasNoArchive() =>
        Assert.Null(new PriceArchive(Path.Combine(_dir, "nope")).Prices("usd"));

    [Fact]
    public void MalformedLine_FailsWithTheFileAndLineNumber()
    {
        Write("dgb-daily-usd.csv", "2026-11-20,0.0112", "2026-11-21;0.0115");
        var archive = new PriceArchive(_dir);

        var ex = Assert.Throws<FormatException>(() => archive.Prices("usd"));
        Assert.Contains("dgb-daily-usd.csv", ex.Message);
        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void LastDate_IsTheFilesLastDay()
    {
        Write("dgb-daily-usd.csv", "2026-11-20,1", "2026-11-23,2");
        Assert.Equal(new DateOnly(2026, 11, 23), PriceArchive.LastDate(new PriceArchive(_dir).Prices("usd")!));
    }

    [Theory]
    [InlineData("2026-11-20", 130.0)]   // the day itself
    [InlineData("2026-11-22", 130.0)]   // Sunday: Friday's rate
    [InlineData("2026-11-27", 130.0)]   // 7 days later: still allowed
    public void RateOn_CarriesTheLatestRateForwardUpToSevenDays(string day, double expected) =>
        Assert.Equal((decimal)expected, PriceArchive.RateOn(Rates(), DateOnly.Parse(day)));

    [Fact]
    public void RateOn_MoreThanSevenDaysAfterTheLastRate_IsNull() =>
        Assert.Null(PriceArchive.RateOn(Rates(), new DateOnly(2026, 11, 28)));

    [Fact]
    public void RateOn_BeforeTheFirstRate_IsNull() =>
        Assert.Null(PriceArchive.RateOn(Rates(), new DateOnly(2026, 11, 19)));

    private static SortedDictionary<DateOnly, decimal> Rates() => new() { [new DateOnly(2026, 11, 20)] = 130m };
}
