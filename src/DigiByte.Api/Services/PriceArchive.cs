using System.Collections.Concurrent;
using System.Globalization;

namespace DigiByte.Api.Services;

/// <summary>
/// The price service's daily archive (spec 031, research R3): read-only data files shipped with the service and updated only by
/// reviewed pull requests from the monthly refresh workflow. <c>dgb-daily-{currency}.csv</c> holds the DGB price per UTC day;
/// <c>usd-fx-{currency}.csv</c> holds units of the currency per 1 USD per published day. Lines are <c>yyyy-MM-dd,value</c>.
/// Each file is loaded once per process. Public market data only: nothing about any wallet.
/// </summary>
public sealed class PriceArchive(string directory)
{
    /// <summary>A day without a published rate (weekend, holiday) uses the latest earlier rate up to this old (spec FR-007).</summary>
    public const int RateCarryForwardDays = 7;

    private readonly ConcurrentDictionary<string, Lazy<SortedDictionary<DateOnly, decimal>?>> _files = new();

    public string Directory { get; } = directory;

    /// <summary>The DGB price per day in <paramref name="currency"/>, or null when there is no archive file for it.</summary>
    public SortedDictionary<DateOnly, decimal>? Prices(string currency) => Load($"dgb-daily-{currency.ToLowerInvariant()}.csv");

    /// <summary>Units of <paramref name="currency"/> per 1 USD per published day, or null when there is no archive file for it.</summary>
    public SortedDictionary<DateOnly, decimal>? Rates(string currency) => Load($"usd-fx-{currency.ToLowerInvariant()}.csv");

    /// <summary>The archived days from <paramref name="from"/> to <paramref name="to"/>, both included. Missing days stay absent.</summary>
    public static SortedDictionary<DateOnly, decimal> Range(IReadOnlyDictionary<DateOnly, decimal> days, DateOnly from, DateOnly to)
    {
        var range = new SortedDictionary<DateOnly, decimal>();
        foreach (var (day, value) in days)
            if (day >= from && day <= to) range[day] = value;
        return range;
    }

    public static DateOnly? LastDate(SortedDictionary<DateOnly, decimal> days) => days.Count == 0 ? null : days.Keys.Last();

    /// <summary>The rate for <paramref name="day"/>: that day's, else the latest earlier one up to <see cref="RateCarryForwardDays"/> old.</summary>
    public static decimal? RateOn(IReadOnlyDictionary<DateOnly, decimal> rates, DateOnly day)
    {
        for (var back = 0; back <= RateCarryForwardDays; back++)
            if (rates.TryGetValue(day.AddDays(-back), out var rate) && rate > 0) return rate;
        return null;
    }

    private SortedDictionary<DateOnly, decimal>? Load(string file) =>
        _files.GetOrAdd(file, f => new Lazy<SortedDictionary<DateOnly, decimal>?>(() => Read(Path.Combine(Directory, f)))).Value;

    /// <summary>Reads one archive file. A malformed line fails with the file name and line number, so a bad refresh breaks tests, not production.</summary>
    private static SortedDictionary<DateOnly, decimal>? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var days = new SortedDictionary<DateOnly, decimal>();
        var number = 0;
        foreach (var raw in File.ReadLines(path))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length != 2
                || !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                || value <= 0)
                throw new FormatException($"{Path.GetFileName(path)} line {number}: expected 'yyyy-MM-dd,value', found '{raw}'.");
            days[day] = value;
        }
        return days;
    }
}
