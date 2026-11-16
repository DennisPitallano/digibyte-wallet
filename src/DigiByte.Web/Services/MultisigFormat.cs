using System.Globalization;

namespace DigiByte.Web.Services;

/// <summary>Display helpers shared by the multisig pages and components.</summary>
public static class MultisigFormat
{
    /// <summary>Amount in DGB with all eight decimals, e.g. 2.00000000.</summary>
    public static string Dgb(long satoshis) =>
        (satoshis / 100_000_000m).ToString("N8", CultureInfo.InvariantCulture);

    /// <summary>First and last characters of a long value, e.g. dgb1qxy…7k2a.</summary>
    public static string Short(string? value, int head = 8, int tail = 6)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= head + tail + 1 ? value : $"{value[..head]}…{value[^tail..]}";
    }

    /// <summary>"Today", "Yesterday", or a date, for grouping activity.</summary>
    public static string DayLabel(DateOnly day)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        return day.ToString(day.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>Local time of day, or a short date and time for older items.</summary>
    public static string When(DateTime utcOrLocal)
    {
        var local = utcOrLocal.Kind == DateTimeKind.Utc ? utcOrLocal.ToLocalTime() : utcOrLocal;
        return local.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>How long ago something happened, e.g. "5 min ago".</summary>
    public static string Age(DateTime utc)
    {
        var span = DateTime.UtcNow - (utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime());
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
        return utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
