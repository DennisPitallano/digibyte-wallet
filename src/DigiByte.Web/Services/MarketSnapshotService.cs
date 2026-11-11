using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigiByte.Web.Services;

/// <summary>24h price change and a 7-day sparkline for the Home balance card.</summary>
/// <param name="Change24h">Percent change over 24 hours (USD basis).</param>
/// <param name="SparkPath">SVG path for a 100x24 viewBox, or null when no chart data came back.</param>
/// <param name="SparkUp">True when the 7-day series ends at or above where it started.</param>
public sealed record MarketSnapshot(decimal Change24h, string? SparkPath, bool SparkUp);

/// <summary>
/// Fetches the same CoinGecko coin document the Analytics page uses (through the price proxy when configured)
/// and keeps it for five minutes. Returns null on any failure: the card just leaves the line out.
/// </summary>
public sealed class MarketSnapshotService(HttpClient http, IConfiguration config, MemoryCacheService cache)
{
    private const string CacheKey = "market:snapshot";
    private const double W = 100, H = 24, Pad = 2;

    public async Task<MarketSnapshot?> GetAsync()
    {
        if (cache.TryGet<MarketSnapshot>(CacheKey, out var cached)) return cached;
        try
        {
            var apiUrl = config["PriceApiUrl"];
            var url = apiUrl != null
                ? $"{apiUrl.TrimEnd('/')}/coin"
                : "https://api.coingecko.com/api/v3/coins/digibyte?localization=false&tickers=false&community_data=false&developer_data=false&sparkline=true";
            var data = await http.GetFromJsonAsync<JsonElement>(url);
            if (!data.TryGetProperty("market_data", out var md)) return null;
            if (!md.TryGetProperty("price_change_percentage_24h", out var pc) || pc.ValueKind != JsonValueKind.Number) return null;

            string? path = null;
            var up = true;
            if (md.TryGetProperty("sparkline_7d", out var sp) && sp.TryGetProperty("price", out var prices)
                && prices.ValueKind == JsonValueKind.Array)
            {
                var points = new List<double>();
                foreach (var p in prices.EnumerateArray())
                    if (p.TryGetDouble(out var v)) points.Add(v);
                (path, up) = BuildPath(points);
            }

            var snapshot = new MarketSnapshot(pc.GetDecimal(), path, up);
            cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(5));
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    internal static (string? Path, bool Up) BuildPath(List<double> points)
    {
        if (points.Count < 2) return (null, true);

        // About 40 points is plenty at this size.
        var step = Math.Max(1, points.Count / 40);
        var sampled = new List<double>();
        for (var i = 0; i < points.Count; i += step) sampled.Add(points[i]);
        if (sampled[^1] != points[^1]) sampled.Add(points[^1]);
        if (sampled.Count < 2) return (null, true);

        var min = sampled.Min();
        var range = sampled.Max() - min;
        if (range == 0) range = 1;

        var parts = new List<string>(sampled.Count);
        for (var i = 0; i < sampled.Count; i++)
        {
            var x = i / (double)(sampled.Count - 1) * W;
            var y = Pad + (1 - (sampled[i] - min) / range) * (H - 2 * Pad);
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1}", x, y));
        }
        return ("M" + string.Join(" L", parts), sampled[^1] >= sampled[0]);
    }
}
