using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests.Export;

/// <summary>What to export: everything, what the list shows, or a date range (spec 031, US2, FR-002).</summary>
public class ExportRequestTests
{
    private static readonly DateOnly Today = new(2026, 3, 15);

    [Theory]
    [InlineData(ExportPreset.ThisMonth, "2026-03-01", "2026-03-15")]
    [InlineData(ExportPreset.LastMonth, "2026-02-01", "2026-02-28")]
    [InlineData(ExportPreset.ThisYear, "2026-01-01", "2026-03-15")]
    public void Presets_ResolveToLocalDateRanges(ExportPreset preset, string from, string to)
    {
        var request = new ExportRequest { Asset = ExportAsset.Dgb, Scope = ExportScope.DateRange, Preset = preset };
        var (f, t) = request.ResolveRange(Today)!.Value;
        Assert.Equal(DateOnly.Parse(from), f);
        Assert.Equal(DateOnly.Parse(to), t);
    }

    [Fact]
    public void LastMonth_InJanuary_IsDecemberOfLastYear()
    {
        var request = new ExportRequest { Asset = ExportAsset.Dgb, Scope = ExportScope.DateRange, Preset = ExportPreset.LastMonth };
        Assert.Equal((new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)), request.ResolveRange(new DateOnly(2026, 1, 10)));
    }

    [Fact]
    public void Custom_UsesFromAndTo()
    {
        var request = Custom("2026-01-10", "2026-02-20");
        Assert.True(request.IsValid(Today));
        Assert.Equal((new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 20)), request.ResolveRange(Today));
    }

    [Theory]
    [InlineData("2026-02-20", "2026-01-10")]   // From after To
    [InlineData("2026-03-01", "2026-03-16")]   // To after today
    [InlineData(null, "2026-03-01")]
    [InlineData("2026-03-01", null)]
    public void Custom_Invalid(string? from, string? to) => Assert.False(Custom(from, to).IsValid(Today));

    [Fact]
    public void Everything_IsAlwaysValid_AndHasNoRange()
    {
        var request = new ExportRequest { Asset = ExportAsset.Dgb };
        Assert.True(request.IsValid(Today));
        Assert.Null(request.ResolveRange(Today));
    }

    [Theory]
    [InlineData("2026-02-01", true)]
    [InlineData("2026-02-28", true)]
    [InlineData("2026-01-31", false)]
    [InlineData("2026-03-01", false)]
    public void DateRange_IsInclusiveOfBothLocalDays(string localDate, bool expected)
    {
        var request = new ExportRequest { Asset = ExportAsset.Dgb, Scope = ExportScope.DateRange, Preset = ExportPreset.LastMonth };
        var row = TransactionCsvWriterTests.Row("x", DateTime.UtcNow) with { LocalDate = DateOnly.Parse(localDate) };
        Assert.Equal(expected, request.Matches(row, null, Today));
    }

    [Fact]
    public void CurrentView_AppliesActivitysFilter()
    {
        var view = new ActivityView("sent", "", null, _ => null, new Dictionary<string, string>());
        var request = new ExportRequest { Asset = ExportAsset.Dgb, Scope = ExportScope.CurrentView, View = view };
        var sent = new TransactionRecord
        {
            TxId = "s", Direction = TransactionDirection.Sent, AmountSatoshis = 1, FeeSatoshis = 1, Timestamp = DateTime.UtcNow, Confirmations = 1,
        };
        var received = new TransactionRecord
        {
            TxId = "r", Direction = TransactionDirection.Received, AmountSatoshis = 1, FeeSatoshis = 0, Timestamp = DateTime.UtcNow, Confirmations = 1,
        };
        var row = TransactionCsvWriterTests.Row("x", DateTime.UtcNow);
        Assert.True(request.Matches(row, sent, Today));
        Assert.False(request.Matches(row, received, Today));
    }

    private static ExportRequest Custom(string? from, string? to) => new()
    {
        Asset = ExportAsset.Dgb,
        Scope = ExportScope.DateRange,
        Preset = ExportPreset.Custom,
        From = from is null ? null : DateOnly.Parse(from),
        To = to is null ? null : DateOnly.Parse(to),
    };
}
