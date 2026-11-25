using System.Net;
using System.Text;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests.Export;

/// <summary>
/// The wallet's daily-price client and series (spec 031, US3, contracts/price-range-api.md): one request per export, carrying
/// only the currency, the asset and two dates; never throws; the price service does any conversion.
/// </summary>
public class DailyPriceSeriesTests
{
    private sealed class Recorder(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private const string ProxyReply = """
        {"currency":"kes","asset":"dgb","basis":"convertedFromUsd","from":"2026-11-19","to":"2026-11-23",
         "prices":[{"date":"2026-11-20","price":1.3},{"date":"2026-11-21","price":2.6}]}
        """;

    // ── Series ──

    [Fact]
    public void TryGet_IsByDate_AndAMissingDayIsAbsentNeverZero()
    {
        var series = new DailyPriceSeries("usd", PriceBasis.Native,
            new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 11, 20)] = 0.0112m }, new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 21));

        Assert.True(series.TryGet(new DateOnly(2026, 11, 20), out var p));
        Assert.Equal(0.0112m, p);
        Assert.False(series.TryGet(new DateOnly(2026, 11, 21), out _));
        Assert.Equal("USD", series.Currency);
    }

    [Fact]
    public void UsdPeg_IsOnePerDay()
    {
        var series = DailyPriceSeries.UsdPeg(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22));
        Assert.Equal(PriceBasis.Peg, series.Basis);
        Assert.Equal(3, series.Count);
        Assert.True(series.TryGet(new DateOnly(2026, 11, 21), out var p));
        Assert.Equal(1m, p);
    }

    // ── Client ──

    [Fact]
    public async Task Proxy_OneRequest_WithOnlyCurrencyAssetAndTwoDates()
    {
        var handler = new Recorder(HttpStatusCode.OK, ProxyReply);

        var series = await DailyPriceClient.FetchAsync(new HttpClient(handler), "https://price.test/api/price", "KES", "dgb",
            new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 23), CancellationToken.None);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("/api/price/range", uri.AbsolutePath);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(["currency", "asset", "from", "to"], q.AllKeys);
        Assert.Equal("kes", q["currency"]);
        Assert.Equal("dgb", q["asset"]);
        Assert.Equal("2026-11-19", q["from"]);
        Assert.Equal("2026-11-23", q["to"]);

        Assert.Equal(PriceBasis.ConvertedFromUsd, series.Basis);   // from the reply: the wallet converts nothing
        Assert.False(series.Failed);
        Assert.True(series.TryGet(new DateOnly(2026, 11, 21), out var p));
        Assert.Equal(2.6m, p);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"prices":"oops"}""")]
    public async Task UpstreamFailure_IsAnEmptyFailedSeries_NeverAThrow(HttpStatusCode status, string body)
    {
        var series = await DailyPriceClient.FetchAsync(new HttpClient(new Recorder(status, body)), "https://price.test/api/price", "usd", "dgb",
            new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 23), CancellationToken.None);

        Assert.True(series.Failed);
        Assert.Equal(0, series.Count);
    }

    [Fact]
    public async Task DirectMode_NativeCurrency_CallsCoinGeckoRange_AndReducesToDays()
    {
        var reply = """{"prices":[[1763600400000,0.010],[1763679600000,0.012],[1763820000000,0.013]]}""";
        var handler = new Recorder(HttpStatusCode.OK, reply);

        var series = await DailyPriceClient.FetchAsync(new HttpClient(handler), null, "usd", "dgb",
            new DateOnly(2025, 11, 19), new DateOnly(2025, 11, 23), CancellationToken.None);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("api.coingecko.com", uri.Host);
        Assert.Equal("/api/v3/coins/digibyte/market_chart/range", uri.AbsolutePath);
        Assert.Equal(PriceBasis.Native, series.Basis);
        Assert.Equal(2, series.Count);   // two UTC days
    }

    [Fact]
    public async Task DirectMode_ConvertedCurrency_IsEmpty_WithNoRequest()
    {
        var handler = new Recorder(HttpStatusCode.OK, "{}");

        var series = await DailyPriceClient.FetchAsync(new HttpClient(handler), null, "KES", "dgb",
            new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 23), CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Equal(0, series.Count);
    }

    // ── Reducer (shared with the price service) ──

    [Fact]
    public void Reducer_KeepsTheLastPointOfEachUtcDay()
    {
        var points = new[]
        {
            (new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 0.010m),
            (new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 0.012m),
            (new DateTimeOffset(2026, 11, 21, 0, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 0.013m),
        };

        var days = DailyPriceReducer.Reduce(points);

        Assert.Equal(0.012m, days[new DateOnly(2026, 11, 20)]);
        Assert.Equal(0.013m, days[new DateOnly(2026, 11, 21)]);
    }
}
