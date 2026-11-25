using System.Globalization;
using System.Text.Json;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// Fetches one daily price series for an export (spec 031, contracts/price-range-api.md). With the price service configured it
/// asks <c>/api/price/range</c> once, with only the currency, the asset and two dates (FR-016); the service does any conversion.
/// Without it (developer direct mode) it asks CoinGecko for native currencies only. Never throws for an upstream failure.
/// </summary>
public static class DailyPriceClient
{
    private const string CoinGeckoRange = "https://api.coingecko.com/api/v3/coins/digibyte/market_chart/range";

    public static async Task<DailyPriceSeries> FetchAsync(HttpClient http, string? priceApiBaseUrl, string fiatCurrency, string asset,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var currency = fiatCurrency.Trim().ToLowerInvariant();
        asset = asset.Trim().ToLowerInvariant();
        try
        {
            if (priceApiBaseUrl is { Length: > 0 } proxy)
            {
                var url = $"{proxy.TrimEnd('/')}/range?currency={Uri.EscapeDataString(currency)}&asset={Uri.EscapeDataString(asset)}"
                    + $"&from={Iso(from)}&to={Iso(to)}";
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return Failed(currency, from, to);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                var basis = root.TryGetProperty("basis", out var b) ? ParseBasis(b.GetString()) : PriceBasis.Native;
                var prices = new Dictionary<DateOnly, decimal>();
                foreach (var p in root.GetProperty("prices").EnumerateArray())
                    prices[DateOnly.ParseExact(p.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)] =
                        p.GetProperty("price").GetDecimal();
                return new DailyPriceSeries(currency, basis, prices, from, to);
            }

            // Direct mode: CoinGecko knows DGB only in its native currencies, and no daily rates are available here.
            if (asset != "dgb" || !FiatCurrencies.IsCoinGeckoNative(currency))
                return DailyPriceSeries.Empty(currency, PriceBasis.Native, from, to, failed: false);

            var fromUnix = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
            var toUnix = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds();
            using var direct = await http.GetAsync($"{CoinGeckoRange}?vs_currency={currency}&from={fromUnix}&to={toUnix}", ct);
            if (!direct.IsSuccessStatusCode) return Failed(currency, from, to);
            using var cg = JsonDocument.Parse(await direct.Content.ReadAsStringAsync(ct));
            return new DailyPriceSeries(currency, PriceBasis.Native, DailyPriceReducer.FromCoinGecko(cg.RootElement), from, to);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Network failure, timeout or an unexpected reply: the export still runs with empty fiat cells (FR-007).
            return Failed(currency, from, to);
        }
    }

    private static DailyPriceSeries Failed(string currency, DateOnly from, DateOnly to) =>
        DailyPriceSeries.Empty(currency, PriceBasis.Native, from, to, failed: true);

    private static PriceBasis ParseBasis(string? basis) => basis switch
    {
        "convertedFromUsd" => PriceBasis.ConvertedFromUsd,
        "peg" => PriceBasis.Peg,
        _ => PriceBasis.Native,
    };

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
