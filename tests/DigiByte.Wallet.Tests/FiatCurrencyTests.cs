using System.Net;
using System.Text;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The display-currency catalog and the USD exchange-rate provider behind the non-CoinGecko currencies.</summary>
public class FiatCurrencyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Codes_AreUnique_UpperCase_AndHaveASymbolAndName()
    {
        Assert.Equal(FiatCurrencies.All.Count, FiatCurrencies.All.Select(c => c.Code).Distinct().Count());
        Assert.All(FiatCurrencies.All, c =>
        {
            Assert.Equal(3, c.Code.Length);
            Assert.Equal(c.Code.ToUpperInvariant(), c.Code);
            Assert.False(string.IsNullOrWhiteSpace(c.Symbol));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.Contains(c.Region, FiatCurrencies.Regions);
        });
    }

    [Fact]
    public void ExistingChoices_Remain_WithTheSameSymbols()
    {
        Assert.Equal("$", FiatCurrencies.SymbolFor("USD"));
        Assert.Equal("€", FiatCurrencies.SymbolFor("EUR"));
        Assert.Equal("£", FiatCurrencies.SymbolFor("GBP"));
        Assert.Equal("₱", FiatCurrencies.SymbolFor("PHP"));
        Assert.Equal("¥", FiatCurrencies.SymbolFor("JPY"));
    }

    [Theory]
    [InlineData("Asia", "INR")]
    [InlineData("Asia", "IDR")]
    [InlineData("Asia", "VND")]
    [InlineData("Africa", "NGN")]
    [InlineData("Africa", "ZAR")]
    [InlineData("Africa", "KES")]
    [InlineData("Africa", "GHS")]
    [InlineData("Africa", "EGP")]
    public void AsiaAndAfrica_AreCovered(string region, string code) =>
        Assert.Contains(FiatCurrencies.InRegion(region), c => c.Code == code);

    [Fact]
    public void Africa_HasBroadCoverage() => Assert.True(FiatCurrencies.InRegion(FiatCurrencies.Africa).Count() >= 15);

    [Fact]
    public void UnknownOrBlank_FallBackSafely()
    {
        Assert.Equal("XYZ ", FiatCurrencies.SymbolFor("xyz"));
        Assert.Equal("$", FiatCurrencies.SymbolFor(null));
        Assert.Equal(2, FiatCurrencies.DecimalsFor("XYZ"));
        Assert.Null(FiatCurrencies.Find(""));
    }

    [Theory]
    [InlineData("JPY", 0)]
    [InlineData("IDR", 0)]
    [InlineData("UGX", 0)]
    [InlineData("USD", 2)]
    [InlineData("KES", 2)]
    [InlineData("KWD", 3)]
    public void Decimals_FollowTheCurrency(string code, int decimals) => Assert.Equal(decimals, FiatCurrencies.DecimalsFor(code));

    [Theory]
    [InlineData("USD", true)]
    [InlineData("inr", true)]
    [InlineData("NGN", true)]
    [InlineData("ZAR", true)]
    [InlineData("KES", false)]
    [InlineData("GHS", false)]
    [InlineData("EGP", false)]
    [InlineData("", false)]
    public void CoinGeckoNative_IsRecognised(string code, bool native) => Assert.Equal(native, FiatCurrencies.IsCoinGeckoNative(code));

    [Fact]
    public void EveryNonNativeCurrency_NeedsAnExchangeRate_AndIsOfferedOnlyThen()
    {
        var nonNative = FiatCurrencies.All.Where(c => !FiatCurrencies.IsCoinGeckoNative(c.Code)).Select(c => c.Code).ToList();
        Assert.Contains("KES", nonNative);
        Assert.DoesNotContain("USD", nonNative);
    }

    // ---- Flags ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DigiByte.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void EveryCurrency_HasAFlagFile_ExceptTheSharedCfaFrancs()
    {
        var flags = Path.Combine(RepoRoot(), "src", "DigiByte.Web", "wwwroot", "images", "flags");
        var noFlag = new[] { "XOF", "XAF" };

        foreach (var c in FiatCurrencies.All)
        {
            var flag = FiatCurrencies.FlagFor(c.Code);
            if (noFlag.Contains(c.Code)) { Assert.Null(flag); continue; }
            Assert.False(string.IsNullOrEmpty(flag), $"{c.Code} has no flag code");
            Assert.True(File.Exists(Path.Combine(flags, flag + ".svg")), $"missing flag file for {c.Code}: {flag}.svg");
        }
    }

    [Fact]
    public void Flags_AreSmallEnoughToBundle()
    {
        var flags = Path.Combine(RepoRoot(), "src", "DigiByte.Web", "wwwroot", "images", "flags");
        var total = Directory.GetFiles(flags, "*.svg").Sum(f => new FileInfo(f).Length);

        Assert.True(total < 600_000, $"flags are {total} bytes");
    }

    [Theory]
    [InlineData("Kenya", "KES")]
    [InlineData("nigeria", "NGN")]
    [InlineData("Senegal", "XOF")]
    [InlineData("Germany", "EUR")]
    public void CountryNames_MakeCurrenciesSearchable(string country, string code) =>
        Assert.Contains(FiatCurrencies.CountryNames(code), n => n.Equals(country, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void UnknownCurrency_HasNoFlagOrCountries()
    {
        Assert.Null(FiatCurrencies.FlagFor("XYZ"));
        Assert.Empty(FiatCurrencies.CountryNames(null));
    }

    // ---- FiatRateProvider ----

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            LastUri = request.RequestUri;
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Rates = "{\"base\":\"USD\",\"rates\":{\"USD\":1,\"KES\":129.25,\"GHS\":10.5,\"BAD\":0,\"TXT\":\"x\"}}";

    [Fact]
    public async Task Usd_NeedsNoNetwork()
    {
        var h = new Handler(() => Json(Rates));
        var rate = await new FiatRateProvider(new HttpClient(h), "https://api.example/api/price").GetPerUsdAsync("usd");

        Assert.Equal(1m, rate);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Rate_IsReadFromTheProxy_AndCachedForSixHours()
    {
        var now = T0;
        var h = new Handler(() => Json(Rates));
        var sut = new FiatRateProvider(new HttpClient(h), "https://api.example/api/price/", () => now);

        Assert.Equal(129.25m, await sut.GetPerUsdAsync("KES"));
        Assert.Equal(10.5m, await sut.GetPerUsdAsync("ghs")); // case-insensitive, same cached table
        Assert.Equal(1, h.Calls);
        Assert.Equal("https://api.example/api/price/fx", h.LastUri!.ToString());

        now = T0.AddHours(5);
        await sut.GetPerUsdAsync("KES");
        Assert.Equal(1, h.Calls);

        now = T0.AddHours(7);
        await sut.GetPerUsdAsync("KES");
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task UnknownZeroOrNonNumericRates_AreNull()
    {
        var sut = new FiatRateProvider(new HttpClient(new Handler(() => Json(Rates))), "https://api.example/api/price");

        Assert.Null(await sut.GetPerUsdAsync("XYZ"));
        Assert.Null(await sut.GetPerUsdAsync("BAD"));
        Assert.Null(await sut.GetPerUsdAsync("TXT"));
    }

    [Fact]
    public async Task NoProxyConfigured_MeansNoRate()
    {
        var h = new Handler(() => Json(Rates));

        Assert.Null(await new FiatRateProvider(new HttpClient(h), null).GetPerUsdAsync("KES"));
        Assert.Equal(0, h.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ProxyFailure_ReturnsNull_AndBacksOffForAMinute(HttpStatusCode status)
    {
        var now = T0;
        var h = new Handler(() => new HttpResponseMessage(status));
        var sut = new FiatRateProvider(new HttpClient(h), "https://api.example/api/price", () => now);

        Assert.Null(await sut.GetPerUsdAsync("KES"));
        Assert.Null(await sut.GetPerUsdAsync("KES"));
        Assert.Equal(1, h.Calls);

        now = T0.AddMinutes(2);
        await sut.GetPerUsdAsync("KES");
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task MalformedPayload_IsTreatedAsUnavailable()
    {
        var sut = new FiatRateProvider(new HttpClient(new Handler(() => Json("{\"nope\":1}"))), "https://api.example/api/price");

        Assert.Null(await sut.GetPerUsdAsync("KES"));
    }

    [Fact]
    public async Task StaleRates_AreKept_WhenARefreshFails()
    {
        var now = T0;
        var ok = true;
        var h = new Handler(() => ok ? Json(Rates) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = new FiatRateProvider(new HttpClient(h), "https://api.example/api/price", () => now);

        Assert.Equal(129.25m, await sut.GetPerUsdAsync("KES"));
        ok = false;
        now = T0.AddHours(8);

        Assert.Equal(129.25m, await sut.GetPerUsdAsync("KES")); // an old rate beats no rate for a display value
    }

    [Fact]
    public async Task Cancellation_IsRespected()
    {
        var sut = new FiatRateProvider(new HttpClient(new Handler(() => Json(Rates))), "https://api.example/api/price");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GetPerUsdAsync("KES", cts.Token));
    }
}
