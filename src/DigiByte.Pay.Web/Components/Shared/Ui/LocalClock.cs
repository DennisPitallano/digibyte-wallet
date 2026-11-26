using System.Globalization;
using Microsoft.JSInterop;

namespace DigiByte.Pay.Web.Components.Shared.Ui;

/// <summary>
/// Shows API times (UTC) in the viewer's own time zone (merchant dashboard, shopper receipt). The page runs on the server, so
/// <c>ToLocalTime()</c> would use the server's zone; this asks the browser for its zone once
/// (Intl.DateTimeFormat) and converts with <see cref="TimeZoneInfo"/>. If the zone is unknown,
/// times stay in UTC and say so. One format everywhere: "d MMM yyyy, HH:mm".
/// </summary>
public sealed class LocalClock
{
    public const string Format = "d MMM yyyy, HH:mm";
    public const string DateFormat = "d MMM yyyy";

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;
    public bool IsUtc => Zone == TimeZoneInfo.Utc;
    public bool Ready { get; private set; }

    /// <summary>Asks the browser for its time zone. Returns true when it changed what is shown.</summary>
    public async Task<bool> InitAsync(IJSRuntime js)
    {
        if (Ready) return false;
        string? id = null;
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "/js/digipay-dashboard.js");
            if (module is not null) id = await module.InvokeAsync<string?>("timeZone");
        }
        catch (Exception) { /* no browser zone: stay on UTC */ }
        Use(id);
        Ready = true;
        return !IsUtc;
    }

    /// <summary>Uses an IANA zone id such as "Europe/Amsterdam"; unknown or empty means UTC.</summary>
    public void Use(string? zoneId)
    {
        Zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(zoneId)) return;
        try { Zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
    }

    public DateTime ToLocal(DateTime utc)
    {
        var u = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc), // the API's times are UTC
        };
        return TimeZoneInfo.ConvertTimeFromUtc(u, Zone);
    }

    /// <summary>"26 Nov 2026, 14:05", with " UTC" when the browser's zone isn't known.</summary>
    public string Show(DateTime utc) =>
        ToLocal(utc).ToString(Format, CultureInfo.InvariantCulture) + (IsUtc ? " UTC" : "");

    public string Show(DateTime? utc) => utc is DateTime t ? Show(t) : "";

    /// <summary>
    /// "26 Nov 2026, 14:05 UTC+1": the time with its offset from UTC on that date (summer time
    /// included), for pages read without other context, like a receipt. " UTC" when the zone isn't known.
    /// </summary>
    public string ShowWithZone(DateTime utc)
    {
        var local = ToLocal(utc);
        var text = local.ToString(Format, CultureInfo.InvariantCulture);
        var offset = IsUtc ? TimeSpan.Zero : Zone.GetUtcOffset(local);
        if (offset == TimeSpan.Zero) return text + " UTC";
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        var hours = ((int)abs.TotalHours).ToString(CultureInfo.InvariantCulture);
        return $"{text} UTC{sign}{hours}" + (abs.Minutes == 0 ? "" : $":{abs.Minutes:00}");
    }

    /// <summary>"26 Nov 2026".</summary>
    public string ShowDate(DateTime utc) => ToLocal(utc).ToString(DateFormat, CultureInfo.InvariantCulture);
}
