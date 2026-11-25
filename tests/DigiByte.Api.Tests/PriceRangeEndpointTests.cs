using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.Api.Tests;

/// <summary>
/// GET /api/price/range (spec 031, contracts/price-range-api.md): daily DGB prices for a date range, from the price archive
/// first and the live sources for newer days, converted at each day's USD rate where needed. Upstreams are stubbed.
/// </summary>
public class PriceRangeEndpointTests : IDisposable
{
    private readonly string _archive = Directory.CreateTempSubdirectory("price-archive-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_archive, recursive: true); } catch { /* best effort */ }
    }

    // ── Upstream stub: CoinGecko market_chart/range, Frankfurter and exchange-api ──

    private sealed class Upstream : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<Uri> Requests { get; } = [];
        public HttpStatusCode CoinGeckoStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>CoinGecko points: (UTC time, price). Several per day; the endpoint keeps the last of each day.</summary>
        public List<(DateTime At, decimal Price)> Points { get; } = [];

        /// <summary>Frankfurter rates per published day (no weekends), units per USD.</summary>
        public Dictionary<DateOnly, decimal> EcbRates { get; } = [];

        public int CountHost(string host) { lock (_gate) return Requests.Count(u => u.Host == host); }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            lock (_gate) Requests.Add(uri);

            if (uri.Host == "api.coingecko.com")
            {
                if (CoinGeckoStatus != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(CoinGeckoStatus));
                var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var from = DateTimeOffset.FromUnixTimeSeconds(long.Parse(q["from"]!)).UtcDateTime;
                var to = DateTimeOffset.FromUnixTimeSeconds(long.Parse(q["to"]!)).UtcDateTime;
                var prices = Points.Where(p => p.At >= from && p.At <= to)
                    .Select(p => new object[] { new DateTimeOffset(p.At).ToUnixTimeMilliseconds(), p.Price });
                return Json(new { prices });
            }

            if (uri.Host == "api.frankfurter.app")
            {
                // /{from}..{to}?from=USD&to=CUR
                var range = uri.AbsolutePath.Trim('/').Split("..");
                var f = DateOnly.Parse(range[0]);
                var t = DateOnly.Parse(range[1]);
                var to = System.Web.HttpUtility.ParseQueryString(uri.Query)["to"]!;
                var rates = EcbRates.Where(kv => kv.Key >= f && kv.Key <= t)
                    .ToDictionary(kv => kv.Key.ToString("yyyy-MM-dd"), kv => new Dictionary<string, decimal> { [to] = kv.Value });
                return Json(new { amount = 1.0, @base = "USD", rates });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(object body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            });
    }

    private sealed class Factory(string archive) : WebApplicationFactory<Program>
    {
        public Upstream Stub { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("PriceArchive:Path", archive);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddHttpClient("PriceRangeSources").ConfigurePrimaryHttpMessageHandler(() => Stub);
            });
        }
    }

    private void Archive(string file, params (string Date, decimal Value)[] rows) =>
        File.WriteAllLines(Path.Combine(_archive, file),
            rows.Select(r => $"{r.Date},{r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(Factory f, string query)
    {
        var response = await f.CreateClient().GetAsync("/api/price/range?" + query);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    private static Dictionary<string, decimal> Prices(JsonElement body) =>
        body.GetProperty("prices").EnumerateArray()
            .ToDictionary(p => p.GetProperty("date").GetString()!, p => p.GetProperty("price").GetDecimal());

    // ── Validation ──

    [Theory]
    [InlineData("currency=xyz&from=2026-11-01&to=2026-11-02")]            // not a wallet currency
    [InlineData("currency=usd&asset=btc&from=2026-11-01&to=2026-11-02")]  // unknown asset
    [InlineData("currency=usd&from=2026-11-03&to=2026-11-02")]            // from after to
    [InlineData("currency=usd&from=2026-11-01&to=2026-11-29")]            // to after tomorrow
    [InlineData("currency=usd&from=2013-12-31&to=2014-01-02")]            // before price data
    [InlineData("currency=usd&from=11/01/2026&to=2026-11-02")]            // not yyyy-MM-dd
    public async Task BadParameters_Are400(string query)
    {
        using var f = new Factory(_archive);
        var (status, _) = await GetAsync(f, query);
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task SeveralYears_AreAccepted_NoSpanLimit()
    {
        Archive("dgb-daily-usd.csv", ("2021-06-01", 0.25m), ("2026-10-31", 0.011m));
        using var f = new Factory(_archive);
        var (status, body) = await GetAsync(f, "currency=usd&from=2021-01-01&to=2026-10-31");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0.25m, Prices(body)["2021-06-01"]);
    }

    // ── Live source: one price per UTC day ──

    [Fact]
    public async Task LivePoints_AreReducedToTheLastPointOfEachUtcDay_Ascending()
    {
        using var f = new Factory(_archive);
        f.Stub.Points.AddRange([
            (new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc), 0.010m),
            (new DateTime(2026, 11, 20, 23, 0, 0, DateTimeKind.Utc), 0.012m),
            (new DateTime(2026, 11, 22, 8, 0, 0, DateTimeKind.Utc), 0.013m),
        ]);

        var (status, body) = await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-22");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("native", body.GetProperty("basis").GetString());
        var prices = Prices(body);
        Assert.Equal(["2026-11-20", "2026-11-22"], prices.Keys);   // 21st has no data: absent, never zero
        Assert.Equal(0.012m, prices["2026-11-20"]);
    }

    // ── Archive first ──

    [Fact]
    public async Task ArchivedDays_ComeFromTheArchive_AndWinOverLiveValues()
    {
        Archive("dgb-daily-usd.csv", ("2026-11-20", 0.5m), ("2026-11-21", 0.6m));
        using var f = new Factory(_archive);
        f.Stub.Points.Add((new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc), 9m));
        f.Stub.Points.Add((new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc), 0.7m));

        var (_, body) = await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-22");

        var prices = Prices(body);
        Assert.Equal(0.6m, prices["2026-11-21"]);   // archive, not the live 9
        Assert.Equal(0.7m, prices["2026-11-22"]);   // after the archive: live
    }

    [Fact]
    public async Task RangeEndingWithinTheArchive_MakesNoUpstreamCall()
    {
        Archive("dgb-daily-usd.csv", ("2026-11-20", 0.5m), ("2026-11-21", 0.6m));
        using var f = new Factory(_archive);

        var (status, _) = await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-21");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(f.Stub.Requests);
    }

    [Fact]
    public async Task UpstreamFails_WithArchivedDays_Returns200WithThem()
    {
        Archive("dgb-daily-usd.csv", ("2026-11-20", 0.5m));
        using var f = new Factory(_archive);
        f.Stub.CoinGeckoStatus = HttpStatusCode.TooManyRequests;

        var (status, body) = await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-25");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0.5m, Prices(body)["2026-11-20"]);
    }

    [Fact]
    public async Task UpstreamFails_WithNothingArchived_PassesTheErrorBack()
    {
        using var f = new Factory(_archive);
        f.Stub.CoinGeckoStatus = HttpStatusCode.ServiceUnavailable;

        var (status, _) = await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-25");

        Assert.Equal(HttpStatusCode.BadGateway, status);
    }

    // ── Converted currencies: each day's USD rate ──

    [Fact]
    public async Task ConvertedCurrency_UsesThatDaysRate_AndCarriesFridaysRateOverTheWeekend()
    {
        // KES is not priced by CoinGecko directly in the wallet's list.
        Archive("dgb-daily-usd.csv", ("2026-11-20", 0.01m), ("2026-11-21", 0.02m), ("2026-11-22", 0.03m));
        Archive("usd-fx-kes.csv", ("2026-11-20", 130m));   // Friday; nothing published Sat/Sun
        using var f = new Factory(_archive);

        var (_, body) = await GetAsync(f, "currency=kes&from=2026-11-20&to=2026-11-22");

        Assert.Equal("convertedFromUsd", body.GetProperty("basis").GetString());
        var prices = Prices(body);
        Assert.Equal(1.3m, prices["2026-11-20"]);
        Assert.Equal(2.6m, prices["2026-11-21"]);   // Saturday price × Friday rate
        Assert.Equal(3.9m, prices["2026-11-22"]);
    }

    [Fact]
    public async Task ARateOlderThanSevenDays_IsNotUsed()
    {
        Archive("dgb-daily-usd.csv", ("2026-11-20", 0.01m));
        Archive("usd-fx-kes.csv", ("2026-11-10", 130m));
        using var f = new Factory(_archive);

        var (_, body) = await GetAsync(f, "currency=kes&from=2026-11-20&to=2026-11-20");

        Assert.Empty(Prices(body));
    }

    [Fact]
    public async Task ConvertedCurrency_NewerDays_UseTheLiveEcbRate()
    {
        Archive("dgb-daily-usd.csv", ("2026-11-23", 0.01m));
        using var f = new Factory(_archive);
        f.Stub.EcbRates[new DateOnly(2026, 11, 23)] = 58m;

        var (_, body) = await GetAsync(f, "currency=php&asset=dd&from=2026-11-23&to=2026-11-23");

        Assert.Equal("peg", body.GetProperty("basis").GetString());
        Assert.Equal(58m, Prices(body)["2026-11-23"]);
        Assert.Equal(1, f.Stub.CountHost("api.frankfurter.app"));
    }

    // ── DigiDollar ──

    [Fact]
    public async Task DigiDollarInUsd_IsOnePerDay_WithNoUpstreamCall()
    {
        using var f = new Factory(_archive);
        var (_, body) = await GetAsync(f, "currency=usd&asset=dd&from=2026-11-20&to=2026-11-22");

        Assert.Equal("peg", body.GetProperty("basis").GetString());
        Assert.All(Prices(body).Values, p => Assert.Equal(1m, p));
        Assert.Equal(3, Prices(body).Count);
        Assert.Empty(f.Stub.Requests);
    }

    // ── Cache ──

    [Fact]
    public async Task ASecondIdenticalCall_IsServedFromCache()
    {
        using var f = new Factory(_archive);
        f.Stub.Points.Add((new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc), 0.7m));

        await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-22");
        await GetAsync(f, "currency=usd&from=2026-11-20&to=2026-11-22");

        Assert.Equal(1, f.Stub.CountHost("api.coingecko.com"));
    }
}
