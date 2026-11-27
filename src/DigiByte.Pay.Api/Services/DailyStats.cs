using DigiByte.Pay.Api.Data;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Per-day sales figures for a store, bucketed by the merchant's own calendar days
/// (GET /v1/pay/stores/{id}/stats/daily). The database is only asked for a UTC range; each
/// payment's UTC creation time is then turned into a local date here, so the same code works on
/// Postgres and SQLite (which hands DateTimes back without a kind) and handles summer time.
/// </summary>
public static class DailyStats
{
    public const int MaxDays = 365;

    /// <summary>The window: <paramref name="days"/> local days ending today, plus the same number just before it.</summary>
    public sealed record Window(DateOnly From, DateOnly To, DateOnly PreviousFrom, DateTime FromUtc, DateTime ToUtc, DateTime PreviousFromUtc);

    /// <summary>One payment, as much as the stats need.</summary>
    public readonly record struct Row(DateTime CreatedAt, PaySessionStatus Status, long AmountSatoshis, string Currency, long? AmountCentsDd = null);

    /// <summary>Counts and DGB revenue for one day or a whole period.</summary>
    public sealed class Totals
    {
        public int Payments { get; set; }
        /// <summary>Paid or confirmed: the money arrived.</summary>
        public int Paid { get; set; }
        /// <summary>Pending or seen: still open.</summary>
        public int Open { get; set; }
        public int Expired { get; set; }
        public int Underpaid { get; set; }
        public int Refunded { get; set; }
        public int Voided { get; set; }
        /// <summary>DGB charged on paid and confirmed DGB payments (refunded ones gave it back).</summary>
        public decimal RevenueDgb { get; set; }
        /// <summary>USD cents charged on paid and confirmed DigiDollar payments. Never added into DGB (specs/033 FR-009).</summary>
        public long RevenueDdCents { get; set; }

        internal void Add(Row r)
        {
            Payments++;
            switch (r.Status)
            {
                case PaySessionStatus.Paid or PaySessionStatus.Confirmed:
                    Paid++;
                    if (string.Equals(r.Currency, "DGB", StringComparison.OrdinalIgnoreCase))
                        RevenueDgb += r.AmountSatoshis / 100_000_000m;
                    else if (string.Equals(r.Currency, "DD", StringComparison.OrdinalIgnoreCase))
                        RevenueDdCents += r.AmountCentsDd ?? 0;
                    break;
                case PaySessionStatus.Pending or PaySessionStatus.Seen: Open++; break;
                case PaySessionStatus.Expired: Expired++; break;
                case PaySessionStatus.Underpaid: Underpaid++; break;
                case PaySessionStatus.Refunded: Refunded++; break;
                case PaySessionStatus.Voided: Voided++; break;
            }
        }
    }

    public sealed record Day(DateOnly Date, Totals Totals);

    /// <summary>
    /// Resolves an IANA (or Windows) zone id; null or blank is UTC. False for an unknown id.
    /// </summary>
    public static bool TryZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return true;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }

    public static Window WindowFor(DateTime utcNow, int days, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcNow), zone));
        var from = today.AddDays(-(days - 1));
        var previousFrom = from.AddDays(-days);
        return new Window(from, today, previousFrom,
            StartOfDayUtc(from, zone), StartOfDayUtc(today.AddDays(1), zone), StartOfDayUtc(previousFrom, zone));
    }

    /// <summary>The UTC instant a local day starts. Where summer time skips midnight, the first valid local time.</summary>
    public static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var i = 0; i < 8 && zone.IsInvalidTime(local); i++) local = local.AddMinutes(15);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>The merchant's local date for a stored (UTC) time.</summary>
    public static DateOnly LocalDate(DateTime storedUtc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(storedUtc), zone));

    /// <summary>
    /// Buckets <paramref name="rows"/> into one entry per day of the window (zero days included,
    /// oldest first) and totals for the window and the period before it.
    /// </summary>
    public static (List<Day> Series, Totals Current, Totals Previous) Aggregate(IEnumerable<Row> rows, Window w, TimeZoneInfo zone)
    {
        var count = w.To.DayNumber - w.From.DayNumber + 1;
        var series = Enumerable.Range(0, count).Select(i => new Day(w.From.AddDays(i), new Totals())).ToList();
        var current = new Totals();
        var previous = new Totals();
        foreach (var r in rows)
        {
            var date = LocalDate(r.CreatedAt, zone);
            if (date >= w.From && date <= w.To)
            {
                series[date.DayNumber - w.From.DayNumber].Totals.Add(r);
                current.Add(r);
            }
            else if (date >= w.PreviousFrom && date < w.From)
            {
                previous.Add(r);
            }
        }
        return (series, current, previous);
    }

    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc), // the API stores UTC; SQLite drops the kind
    };
}
