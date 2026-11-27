using System.Collections.Concurrent;
using System.Text.Json;
using DigiByte.Wallet.Models;
using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Pay.Api.Services;

/// <summary>A DGB spot price in one fiat currency.</summary>
/// <param name="Currency">ISO code, upper case.</param>
/// <param name="DgbPrice">Units of <paramref name="Currency"/> for 1 DGB.</param>
/// <param name="FetchedAt">When the price was read upstream (UTC).</param>
/// <param name="Source">"coingecko" when CoinGecko prices DGB in the currency directly; "coingecko+fx" when it is the USD
/// price times a USD exchange rate.</param>
/// <param name="Decimals">Digits after the decimal point for amounts in this currency (0 for yen, rupiah and similar).</param>
/// <param name="Symbol">The symbol written before an amount.</param>
/// <param name="Stale">True when upstream failed and this is the last known price.</param>
public sealed record DgbPriceQuote(
    string Currency, decimal DgbPrice, DateTime FetchedAt, string Source, int Decimals, string Symbol, bool Stale = false);

/// <summary>
/// DGB prices for the POS, payment links and the checkout's price-drift notice, worked out the same way as the wallet
/// (<c>FallbackBlockchainService.GetDgbPriceAsync</c>): the wallet's currency list (<see cref="FiatCurrencies"/>); CoinGecko's
/// own price where it quotes DGB in that currency, otherwise the USD price times the USD exchange rate. Prices are fresh for
/// 60 seconds and the last good one is served for 10 minutes if upstream fails; exchange rates are kept for 6 hours (a day
/// if the rate source is down). One upstream request per key at a time, however many tablets ask.
/// </summary>
public sealed class DgbPriceService
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StaleFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RatesFreshFor = TimeSpan.FromHours(6);
    public static readonly TimeSpan RatesStaleFor = TimeSpan.FromHours(24);

    private const string DefaultCoinGeckoUrl = "https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies=";
    private const string DefaultFxUrl = "https://open.er-api.com/v6/latest/USD";

    // Overridable (DigiPay:Prices:CoinGeckoUrl / FxUrl) so the end-to-end run can use a local stub instead of the internet.
    private readonly string CoinGeckoUrl;
    private readonly string FxUrl;

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<DgbPriceService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public DgbPriceService(IHttpClientFactory http, IMemoryCache cache, ILogger<DgbPriceService> log, TimeProvider? clock = null,
        IConfiguration? config = null)
    {
        _http = http;
        _cache = cache;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        CoinGeckoUrl = config?["DigiPay:Prices:CoinGeckoUrl"] is { Length: > 0 } cg ? cg : DefaultCoinGeckoUrl;
        FxUrl = config?["DigiPay:Prices:FxUrl"] is { Length: > 0 } fx ? fx : DefaultFxUrl;
    }

    /// <summary>True for every currency in the wallet's list.</summary>
    public static bool IsSupported(string? code) => FiatCurrencies.Find(code) is not null;

    /// <summary>The price of 1 DGB in <paramref name="code"/>, or null when it is unsupported or no price is available.</summary>
    public async Task<DgbPriceQuote?> GetAsync(string? code, CancellationToken ct = default)
    {
        var currency = FiatCurrencies.Find(code);
        if (currency is null) return null;

        var native = FiatCurrencies.IsCoinGeckoNative(currency.Code);
        var usd = native ? null : await GetNativeAsync("USD", ct);
        if (native)
        {
            var direct = await GetNativeAsync(currency.Code, ct);
            return direct is null ? null : Quote(currency, direct.Value.Price, direct.Value.At, "coingecko", direct.Value.Stale);
        }

        if (usd is null) return null;
        var perUsd = await GetUsdRateWithStalenessAsync(currency.Code, ct);
        if (perUsd is null) return null;
        return Quote(currency, usd.Value.Price * perUsd.Value.Rate, usd.Value.At, "coingecko+fx", usd.Value.Stale || perUsd.Value.Stale);
    }

    private static DgbPriceQuote Quote(FiatCurrency c, decimal price, DateTime at, string source, bool stale) =>
        new(c.Code, price, at, source, c.Decimals, c.Symbol.Trim(), stale);

    // CoinGecko's own DGB price in a currency it quotes.
    private async Task<(decimal Price, DateTime At, bool Stale)?> GetNativeAsync(string code, CancellationToken ct)
    {
        var lower = code.ToLowerInvariant();
        var fresh = $"digipay.price.fresh:{lower}";
        var stale = $"digipay.price.stale:{lower}";
        if (_cache.TryGetValue(fresh, out (decimal, DateTime) hit)) return (hit.Item1, hit.Item2, false);

        var gate = _gates.GetOrAdd(fresh, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(fresh, out hit)) return (hit.Item1, hit.Item2, false);
            try
            {
                using var resp = await _http.CreateClient("DigiPayChain").GetAsync(CoinGeckoUrl + lower, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if (json.ValueKind == JsonValueKind.Object
                        && json.TryGetProperty("digibyte", out var dgb)
                        && dgb.TryGetProperty(lower, out var el)
                        && el.TryGetDecimal(out var price) && price > 0)
                    {
                        var at = _clock.GetUtcNow().UtcDateTime;
                        _cache.Set(fresh, (price, at), FreshFor);
                        _cache.Set(stale, (price, at), StaleFor);
                        return (price, at, false);
                    }
                }
                _log.LogInformation("CoinGecko returned no DGB price in {Currency} ({Status})", code, (int)resp.StatusCode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _log.LogInformation(ex, "CoinGecko price lookup failed for {Currency}", code); }

            return _cache.TryGetValue(stale, out (decimal, DateTime) last) ? (last.Item1, last.Item2, true) : null;
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Units of <paramref name="code"/> one US dollar buys, or null when the currency is unsupported or no rate is
    /// available. USD is exactly 1. Used to price DD (1 DD = 1 USD) from a fiat amount (specs/033 research R3).
    /// </summary>
    public async Task<decimal?> GetUsdRateAsync(string? code, CancellationToken ct = default)
    {
        var currency = FiatCurrencies.Find(code);
        if (currency is null) return null;
        if (currency.Code == "USD") return 1m;
        var rate = await GetUsdRateWithStalenessAsync(currency.Code, ct);
        return rate?.Rate;
    }

    // Units of a currency one US dollar buys (open.er-api.com, the same source as the wallet's price proxy /fx).
    private async Task<(decimal Rate, bool Stale)?> GetUsdRateWithStalenessAsync(string code, CancellationToken ct)
    {
        const string fresh = "digipay.fx.fresh";
        const string stale = "digipay.fx.stale";
        Dictionary<string, decimal>? rates;
        var isStale = false;
        if (!_cache.TryGetValue(fresh, out rates))
        {
            var gate = _gates.GetOrAdd(fresh, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                if (!_cache.TryGetValue(fresh, out rates))
                {
                    rates = await FetchRatesAsync(ct);
                    if (rates is not null)
                    {
                        _cache.Set(fresh, rates, RatesFreshFor);
                        _cache.Set(stale, rates, RatesStaleFor);
                    }
                    else if (_cache.TryGetValue(stale, out rates)) isStale = true;
                }
            }
            finally { gate.Release(); }
        }
        return rates is not null && rates.TryGetValue(code, out var rate) && rate > 0 ? (rate, isStale) : null;
    }

    private async Task<Dictionary<string, decimal>?> FetchRatesAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.CreateClient("DigiPayChain").GetAsync(FxUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("rates", out var el) || el.ValueKind != JsonValueKind.Object)
                return null;
            var parsed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in el.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var v) && v > 0)
                    parsed[p.Name] = v;
            return parsed.Count > 0 ? parsed : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Exchange-rate lookup failed");
            return null;
        }
    }
}
