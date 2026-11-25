using System.Globalization;
using System.Text.Json;
using DigiByte.Api.Services;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services.Export;
using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Api.Endpoints;

/// <summary>
/// GET /api/price/range (spec 031, contracts/price-range-api.md): one price per day for an export's date range. Archived days
/// come from <see cref="PriceArchive"/>; only days after the archive are asked live. Currencies CoinGecko doesn't price DGB in
/// are converted at each day's USD rate here, so the wallet makes one request. The request carries only a currency, an asset
/// and two dates; nothing about any wallet. Logs carry status codes only.
/// </summary>
public static class PriceRangeEndpoint
{
    public const string RateLimitPolicy = "price-range";
    public const string HttpClientName = "PriceRangeSources";

    /// <summary>DGB price data begins around here; earlier requests are refused.</summary>
    public static readonly DateOnly EarliestDay = new(2014, 1, 1);

    /// <summary>CoinGecko's free tier serves this much history; older days come from the archive only.</summary>
    private const int LiveHistoryDays = 365;

    /// <summary>Live rates for currencies the ECB doesn't publish are one request per day: only this many recent days are asked.</summary>
    private const int LiveSnapshotDays = 45;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    /// <summary>Currencies the European Central Bank publishes daily (via Frankfurter); others use the exchange-api snapshots.</summary>
    private static readonly HashSet<string> EcbCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUD", "BGN", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR", "ISK", "JPY",
        "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "ZAR",
    };

    public static async Task<IResult> HandleAsync(string? currency, string? asset, string? from, string? to,
        PriceArchive archive, IHttpClientFactory httpFactory, IMemoryCache cache, TimeProvider time, ILoggerFactory loggers,
        CancellationToken ct)
    {
        var cur = (currency ?? "").Trim().ToLowerInvariant();
        var kind = string.IsNullOrWhiteSpace(asset) ? "dgb" : asset.Trim().ToLowerInvariant();
        var tomorrow = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(1);
        if (FiatCurrencies.Find(cur) is null || kind is not ("dgb" or "dd")
            || !TryDay(from, out var f) || !TryDay(to, out var t) || f > t || t > tomorrow || f < EarliestDay)
            return Results.BadRequest(new { error = "Expected currency (a wallet currency), asset (dgb or dd), and from ≤ to as yyyy-MM-dd." });

        var key = $"price:range:{kind}:{cur}:{Iso(f)}:{Iso(t)}";
        if (cache.TryGetValue(key, out object? cached) && cached is not null)
            return Results.Json(cached);

        var log = loggers.CreateLogger("PriceRange");
        var sources = new Sources(httpFactory.CreateClient(HttpClientName), archive, log, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));

        string basis;
        SortedDictionary<DateOnly, decimal> prices;
        bool primaryFailed;
        if (kind == "dd")
        {
            basis = "peg";
            if (cur == "usd")
            {
                prices = new SortedDictionary<DateOnly, decimal>();
                for (var d = f; d <= t; d = d.AddDays(1)) prices[d] = 1m;
                primaryFailed = false;
            }
            else
            {
                var (rates, failed) = await sources.RatesAsync(cur, f.AddDays(-PriceArchive.RateCarryForwardDays), t, ct);
                prices = Convert(Every(f, t, 1m), rates);
                primaryFailed = failed;
            }
        }
        else if (FiatCurrencies.IsCoinGeckoNative(cur))
        {
            basis = "native";
            (prices, primaryFailed) = await sources.DgbAsync(cur, f, t, ct);
        }
        else
        {
            basis = "convertedFromUsd";
            var (usd, failed) = await sources.DgbAsync("usd", f, t, ct);
            var (rates, _) = await sources.RatesAsync(cur, f.AddDays(-PriceArchive.RateCarryForwardDays), t, ct);
            prices = Convert(usd, rates);
            primaryFailed = failed;
        }

        if (primaryFailed && prices.Count == 0)
            return Results.StatusCode(StatusCodes.Status502BadGateway);

        var body = new
        {
            currency = cur,
            asset = kind,
            basis,
            from = Iso(f),
            to = Iso(t),
            prices = prices.Select(p => new { date = Iso(p.Key), price = p.Value }).ToList(),
        };
        if (!primaryFailed) cache.Set(key, (object)body, CacheLifetime);
        return Results.Json(body);
    }

    /// <summary>A price per day times that day's rate (carried forward up to 7 days). Days without a rate are left out.</summary>
    private static SortedDictionary<DateOnly, decimal> Convert(SortedDictionary<DateOnly, decimal> usd, SortedDictionary<DateOnly, decimal> rates)
    {
        var converted = new SortedDictionary<DateOnly, decimal>();
        foreach (var (day, price) in usd)
            if (PriceArchive.RateOn(rates, day) is { } rate) converted[day] = price * rate;
        return converted;
    }

    private static SortedDictionary<DateOnly, decimal> Every(DateOnly f, DateOnly t, decimal value)
    {
        var days = new SortedDictionary<DateOnly, decimal>();
        for (var d = f; d <= t; d = d.AddDays(1)) days[d] = value;
        return days;
    }

    private static bool TryDay(string? s, out DateOnly day) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The archive plus the live sources, for one request.</summary>
    private sealed class Sources(HttpClient http, PriceArchive archive, ILogger log, DateOnly today)
    {
        /// <summary>DGB prices per day: archive first, then CoinGecko for days after it (within its free window).</summary>
        public async Task<(SortedDictionary<DateOnly, decimal> Prices, bool Failed)> DgbAsync(string cur, DateOnly f, DateOnly t,
            CancellationToken ct)
        {
            var archived = archive.Prices(cur);
            var prices = archived is null ? new SortedDictionary<DateOnly, decimal>() : PriceArchive.Range(archived, f, t);
            var liveFrom = Later(f, archived is null ? null : PriceArchive.LastDate(archived)?.AddDays(1));
            liveFrom = Later(liveFrom, today.AddDays(-(LiveHistoryDays - 1)));
            if (liveFrom > t) return (prices, false);

            try
            {
                var fromUnix = new DateTimeOffset(liveFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
                var toUnix = new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds();
                using var response = await http.GetAsync(
                    $"https://api.coingecko.com/api/v3/coins/digibyte/market_chart/range?vs_currency={cur}&from={fromUnix}&to={toUnix}", ct);
                if (!response.IsSuccessStatusCode)
                {
                    log.LogWarning("Live DGB prices unavailable: {Status}", (int)response.StatusCode);
                    return (prices, true);
                }
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                foreach (var (day, price) in DailyPriceReducer.FromCoinGecko(doc.RootElement))
                    if (day >= liveFrom && day <= t) prices.TryAdd(day, price);   // archive values win
                return (prices, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning("Live DGB prices unavailable: {Error}", ex.GetType().Name);
                return (prices, true);
            }
        }

        /// <summary>Units of <paramref name="cur"/> per USD per day: archive first, then the live source for days after it.</summary>
        public async Task<(SortedDictionary<DateOnly, decimal> Rates, bool Failed)> RatesAsync(string cur, DateOnly f, DateOnly t,
            CancellationToken ct)
        {
            if (cur == "usd") return (Every(f, t, 1m), false);
            var archived = archive.Rates(cur);
            var rates = archived is null ? new SortedDictionary<DateOnly, decimal>() : PriceArchive.Range(archived, f, t);
            var liveFrom = Later(f, archived is null ? null : PriceArchive.LastDate(archived)?.AddDays(1));
            if (liveFrom > t) return (rates, false);

            try
            {
                if (EcbCurrencies.Contains(cur))
                {
                    using var response = await http.GetAsync(
                        $"https://api.frankfurter.app/{Iso(liveFrom)}..{Iso(t)}?from=USD&to={cur.ToUpperInvariant()}", ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        log.LogWarning("Live rates unavailable: {Status}", (int)response.StatusCode);
                        return (rates, true);
                    }
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    foreach (var day in doc.RootElement.GetProperty("rates").EnumerateObject())
                        if (TryDay(day.Name, out var d) && day.Value.TryGetProperty(cur.ToUpperInvariant(), out var r))
                            rates.TryAdd(d, r.GetDecimal());
                    return (rates, false);
                }

                // Not published by the ECB: one daily snapshot per day, recent days only.
                var failed = false;
                for (var d = Later(liveFrom, t.AddDays(-(LiveSnapshotDays - 1))); d <= t; d = d.AddDays(1))
                {
                    using var response = await http.GetAsync(
                        $"https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{Iso(d)}/v1/currencies/usd.json", ct);
                    if (!response.IsSuccessStatusCode) { failed = true; continue; }
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("usd", out var usd) && usd.TryGetProperty(cur, out var r))
                        rates.TryAdd(d, r.GetDecimal());
                }
                if (failed) log.LogWarning("Some live rate snapshots were unavailable");
                return (rates, failed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning("Live rates unavailable: {Error}", ex.GetType().Name);
                return (rates, true);
            }
        }

        private static DateOnly Later(DateOnly a, DateOnly? b) => b is { } x && x > a ? x : a;
    }
}
