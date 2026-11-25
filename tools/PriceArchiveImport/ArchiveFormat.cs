using System.Globalization;
using System.Text.Json;

namespace PriceArchiveImport;

/// <summary>
/// Reads the free price and rate sources into the archive format (<c>yyyy-MM-dd,value</c>, one line per day, ascending) and
/// appends new days (spec 031, research R3). Days are UTC calendar days; values keep the precision the source gave.
/// </summary>
public static class ArchiveFormat
{
    /// <summary>
    /// CoinGecko's "Historical data" CSV export for DGB in one currency (<c>snapped_at,price,market_cap,total_volume</c>, with
    /// <c>snapped_at</c> like <c>2026-11-20 00:00:00 UTC</c>). A later row for the same day wins; empty or zero prices are dropped.
    /// </summary>
    public static SortedDictionary<DateOnly, decimal> FromCoinGeckoExport(TextReader reader)
    {
        var days = new SortedDictionary<DateOnly, decimal>();
        string? line;
        var first = true;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            if (first) { first = false; if (line.StartsWith("snapped_at", StringComparison.OrdinalIgnoreCase)) continue; }
            var parts = line.Split(',');
            if (parts.Length < 2 || parts[0].Length < 10) continue;
            if (!DateOnly.TryParseExact(parts[0][..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            if (!decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || price <= 0) continue;
            days[day] = price;
        }
        return days;
    }

    /// <summary>A Frankfurter (ECB) time series reply with <c>from=USD</c>: units of <paramref name="currency"/> per USD per published day.</summary>
    public static SortedDictionary<DateOnly, decimal> FromFrankfurter(string json, string currency)
    {
        var days = new SortedDictionary<DateOnly, decimal>();
        using var doc = JsonDocument.Parse(json);
        foreach (var day in doc.RootElement.GetProperty("rates").EnumerateObject())
            if (DateOnly.TryParseExact(day.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                && day.Value.TryGetProperty(currency.ToUpperInvariant(), out var rate))
                days[d] = rate.GetDecimal();
        return days;
    }

    /// <summary>One exchange-api daily snapshot (<c>{"date":…,"usd":{"kes":…}}</c>): that day's units of <paramref name="currency"/> per USD.</summary>
    public static (DateOnly Day, decimal Rate)? FromExchangeApi(string json, string currency)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("date", out var date)
            || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return null;
        if (!root.TryGetProperty("usd", out var usd) || !usd.TryGetProperty(currency.ToLowerInvariant(), out var rate)) return null;
        return (day, rate.GetDecimal());
    }

    /// <summary>
    /// Adds the days of <paramref name="fresh"/> that come after the last day of <paramref name="existing"/>, never
    /// <paramref name="today"/> (an unfinished day). Existing days are never changed. Returns how many days were added.
    /// </summary>
    public static int Append(SortedDictionary<DateOnly, decimal> existing, IReadOnlyDictionary<DateOnly, decimal> fresh, DateOnly today)
    {
        var last = existing.Count == 0 ? DateOnly.MinValue : existing.Keys.Last();
        var added = 0;
        foreach (var (day, value) in fresh.OrderBy(kv => kv.Key))
        {
            if (day <= last || day >= today || value <= 0) continue;
            existing[day] = value;
            added++;
        }
        return added;
    }

    /// <summary>The archive file's lines.</summary>
    public static IEnumerable<string> Lines(IReadOnlyDictionary<DateOnly, decimal> days) =>
        days.OrderBy(kv => kv.Key).Select(kv =>
            kv.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "," + kv.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Reads an archive file (missing → empty).</summary>
    public static SortedDictionary<DateOnly, decimal> Read(string path)
    {
        var days = new SortedDictionary<DateOnly, decimal>();
        if (!File.Exists(path)) return days;
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length == 2
                && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
                days[d] = v;
        }
        return days;
    }

    public static void Write(string path, IReadOnlyDictionary<DateOnly, decimal> days) =>
        File.WriteAllText(path, string.Join('\n', Lines(days)) + "\n");
}
