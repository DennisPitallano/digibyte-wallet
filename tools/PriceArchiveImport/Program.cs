using System.Globalization;
using PriceArchiveImport;

// Seeds and refreshes src/DigiByte.Api/PriceArchive (spec 031, research R3).
//
//   import-coingecko <coingecko-export.csv> <archive-dir> <currency>
//       One-time seed: converts CoinGecko's "Historical data" CSV export of DGB in <currency> into dgb-daily-<currency>.csv.
//   seed-rates <archive-dir> <currency> <from yyyy-MM-dd>
//       One-time seed of usd-fx-<currency>.csv from Frankfurter (ECB) when it publishes the currency, else exchange-api snapshots.
//   append-from-api <archive-dir>
//       Monthly refresh: appends days after each file's last date (never today) from the free live sources. Exits 1 when a file
//       is still more than 300 days behind afterwards, so a neglected archive fails loudly.

var today = DateOnly.FromDateTime(DateTime.UtcNow);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet-PriceArchive/1.0");

switch (args.FirstOrDefault())
{
    case "import-coingecko" when args.Length == 4:
    {
        using var reader = new StreamReader(args[1]);
        var days = ArchiveFormat.FromCoinGeckoExport(reader);
        var path = Path.Combine(args[2], $"dgb-daily-{args[3].ToLowerInvariant()}.csv");
        ArchiveFormat.Write(path, days);
        Console.WriteLine($"{path}: {days.Count} days");
        return 0;
    }
    case "seed-rates" when args.Length == 4:
    {
        var from = DateOnly.ParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = Path.Combine(args[1], $"usd-fx-{args[2].ToLowerInvariant()}.csv");
        var days = ArchiveFormat.Read(path);
        var added = await Sources.AppendRatesAsync(http, days, args[2], from, today);
        ArchiveFormat.Write(path, days);
        Console.WriteLine($"{path}: +{added} days");
        return 0;
    }
    case "append-from-api" when args.Length == 2:
    {
        var stale = false;
        foreach (var path in Directory.GetFiles(args[1], "*.csv").Order())
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var days = ArchiveFormat.Read(path);
            var from = days.Count == 0 ? today.AddDays(-364) : days.Keys.Last().AddDays(1);
            int added;
            if (name.StartsWith("dgb-daily-", StringComparison.Ordinal))
                added = await Sources.AppendDgbAsync(http, days, name["dgb-daily-".Length..], today);
            else if (name.StartsWith("usd-fx-", StringComparison.Ordinal))
                added = await Sources.AppendRatesAsync(http, days, name["usd-fx-".Length..], from, today);
            else continue;
            ArchiveFormat.Write(path, days);
            var behind = days.Count == 0 ? int.MaxValue : today.DayNumber - days.Keys.Last().DayNumber;
            Console.WriteLine($"{Path.GetFileName(path)}: +{added} days, last {(days.Count == 0 ? "none" : days.Keys.Last().ToString("yyyy-MM-dd"))}");
            if (behind > 300) { stale = true; Console.Error.WriteLine($"{Path.GetFileName(path)} is {behind} days behind."); }
        }
        return stale ? 1 : 0;
    }
    default:
        Console.Error.WriteLine("Usage: import-coingecko <export.csv> <archive-dir> <currency> | seed-rates <archive-dir> <currency> <from> | append-from-api <archive-dir>");
        return 2;
}

internal static class Sources
{
    private static readonly HashSet<string> Ecb = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUD", "BGN", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR", "ISK", "JPY",
        "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "ZAR",
    };

    /// <summary>CoinGecko's free daily series for the last 365 days, appended after the file's last day.</summary>
    public static async Task<int> AppendDgbAsync(HttpClient http, SortedDictionary<DateOnly, decimal> days, string currency, DateOnly today)
    {
        var json = await http.GetStringAsync(
            $"https://api.coingecko.com/api/v3/coins/digibyte/market_chart?vs_currency={currency}&days=365&interval=daily");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var fresh = new SortedDictionary<DateOnly, decimal>();
        foreach (var point in doc.RootElement.GetProperty("prices").EnumerateArray())
        {
            var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(point[0].GetInt64()).UtcDateTime);
            fresh[day] = point[1].GetDecimal();
        }
        return ArchiveFormat.Append(days, fresh, today);
    }

    /// <summary>Daily USD rates from <paramref name="from"/>: Frankfurter (ECB) when it publishes the currency, else exchange-api snapshots.</summary>
    public static async Task<int> AppendRatesAsync(HttpClient http, SortedDictionary<DateOnly, decimal> days, string currency,
        DateOnly from, DateOnly today)
    {
        var to = today.AddDays(-1);
        if (from > to) return 0;
        if (Ecb.Contains(currency))
        {
            var json = await http.GetStringAsync(
                $"https://api.frankfurter.app/{from:yyyy-MM-dd}..{to:yyyy-MM-dd}?from=USD&to={currency.ToUpperInvariant()}");
            return ArchiveFormat.Append(days, ArchiveFormat.FromFrankfurter(json, currency), today);
        }

        var fresh = new SortedDictionary<DateOnly, decimal>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            try
            {
                var json = await http.GetStringAsync(
                    $"https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{d:yyyy-MM-dd}/v1/currencies/usd.json");
                if (ArchiveFormat.FromExchangeApi(json, currency) is { } rate) fresh[rate.Day] = rate.Rate;
            }
            catch (HttpRequestException)
            {
                // No snapshot for that day (before the source began, or a gap): the day stays absent.
            }
        }
        return ArchiveFormat.Append(days, fresh, today);
    }
}
