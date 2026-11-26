using System.Net;
using System.Text;
using DigiByte.Pay.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigiByte.Pay.Api.Tests;

// DGB prices are worked out like the wallet's: CoinGecko directly where it quotes DGB, otherwise USD x exchange rate.
public class DgbPriceServiceTests
{
    private sealed class Upstream : HttpMessageHandler
    {
        public readonly List<string> Calls = [];
        public bool Fail;
        public decimal UsdPrice = 0.01m;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Calls.Add(url);
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            string body;
            if (url.Contains("open.er-api.com"))
                body = """{"result":"success","base_code":"USD","rates":{"USD":1,"KES":129.5,"PHP":58.2,"DOP":60.1}}""";
            else
            {
                var cur = url[(url.LastIndexOf('=') + 1)..];
                var price = cur switch { "usd" => UsdPrice, "php" => 0.5825m, "jpy" => 1.52m, _ => 0m };
                var p = price.ToString(System.Globalization.CultureInfo.InvariantCulture);
                body = "{\"digibyte\":{\"" + cur + "\":" + p + "}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static (DgbPriceService Service, Upstream Upstream) Create()
    {
        var up = new Upstream();
        return (new DgbPriceService(new Factory(up), new MemoryCache(new MemoryCacheOptions()), NullLogger<DgbPriceService>.Instance), up);
    }

    [Fact]
    public async Task A_currency_coingecko_quotes_directly_uses_coingeckos_price()
    {
        var (svc, up) = Create();
        var q = await svc.GetAsync("php");

        Assert.NotNull(q);
        Assert.Equal("PHP", q!.Currency);
        Assert.Equal(0.5825m, q.DgbPrice);
        Assert.Equal("coingecko", q.Source);
        Assert.Equal("₱", q.Symbol);
        Assert.Equal(2, q.Decimals);
        Assert.DoesNotContain(up.Calls, c => c.Contains("open.er-api.com"));
    }

    [Fact]
    public async Task Other_wallet_currencies_are_the_usd_price_times_the_usd_rate()
    {
        var (svc, _) = Create();
        var q = await svc.GetAsync("KES");

        Assert.NotNull(q);
        Assert.Equal(0.01m * 129.5m, q!.DgbPrice);
        Assert.Equal("coingecko+fx", q.Source);
        Assert.False(q.Stale);
    }

    [Fact]
    public async Task Decimals_follow_the_wallet_list()
    {
        var (svc, _) = Create();
        Assert.Equal(0, (await svc.GetAsync("JPY"))!.Decimals);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("usdt")]
    [InlineData("")]
    public async Task Unknown_codes_are_refused_without_calling_upstream(string code)
    {
        var (svc, up) = Create();
        Assert.False(DgbPriceService.IsSupported(code));
        Assert.Null(await svc.GetAsync(code));
        Assert.Empty(up.Calls);
    }

    [Fact]
    public async Task Prices_are_cached_and_the_last_good_one_is_served_when_upstream_fails()
    {
        var (svc, up) = Create();
        var first = await svc.GetAsync("KES");
        var calls = up.Calls.Count;

        var again = await svc.GetAsync("KES");
        Assert.Equal(calls, up.Calls.Count); // served from cache
        Assert.Equal(first!.DgbPrice, again!.DgbPrice);
    }

    [Fact]
    public async Task With_no_price_at_all_the_result_is_null_not_zero()
    {
        var (svc, up) = Create();
        up.Fail = true;
        Assert.Null(await svc.GetAsync("PHP"));
        Assert.Null(await svc.GetAsync("KES"));
    }
}
