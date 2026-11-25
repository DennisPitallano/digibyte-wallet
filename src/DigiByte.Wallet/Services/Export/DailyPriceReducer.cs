namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// Turns price points (unix milliseconds, price) into one price per UTC calendar day: the last point of each day, the day's
/// closing price. Shared by the wallet's direct mode and the price service (DigiByte.Api), so both reduce the same way.
/// </summary>
public static class DailyPriceReducer
{
    public static SortedDictionary<DateOnly, decimal> Reduce(IEnumerable<(long UnixMs, decimal Price)> points)
    {
        var days = new SortedDictionary<DateOnly, decimal>();
        var latest = new Dictionary<DateOnly, long>();
        foreach (var (ms, price) in points)
        {
            if (price <= 0) continue;
            var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime);
            if (latest.TryGetValue(day, out var seen) && seen > ms) continue;
            latest[day] = ms;
            days[day] = price;
        }
        return days;
    }

    /// <summary>Reads CoinGecko's <c>{"prices":[[ms, price], …]}</c> reply. Throws on a reply of another shape.</summary>
    public static SortedDictionary<DateOnly, decimal> FromCoinGecko(System.Text.Json.JsonElement root)
    {
        var points = new List<(long, decimal)>();
        foreach (var point in root.GetProperty("prices").EnumerateArray())
            points.Add((point[0].GetInt64(), point[1].GetDecimal()));
        return Reduce(points);
    }
}
