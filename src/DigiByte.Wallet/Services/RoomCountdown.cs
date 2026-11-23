using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The time left in a signing room (specs/026-multisig-wallet-experience, FR-031): how it reads, and when it starts
/// to stand out. Part seconds round down, so the clock never shows time the room does not have.
/// </summary>
public static class RoomCountdown
{
    /// <summary>At this much time left, or less, the countdown stands out and is announced once.</summary>
    public static readonly TimeSpan UrgentAt = TimeSpan.FromMinutes(5);

    public static RoomCountdownModel For(TimeSpan remaining)
    {
        var left = Whole(remaining);
        var text = left.TotalHours >= 1
            ? $"{(int)left.TotalHours}:{left.Minutes:D2}:{left.Seconds:D2}"
            : $"{left.Minutes}:{left.Seconds:D2}";
        return new RoomCountdownModel(text, left <= UrgentAt);
    }

    /// <summary>The same time in words, for assistive technology ("27 minutes 40 seconds left").</summary>
    public static string Spoken(TimeSpan remaining)
    {
        var left = Whole(remaining);
        if (left == TimeSpan.Zero) return "No time left";

        var parts = new List<string>();
        var hours = (int)left.TotalHours;
        if (hours > 0) parts.Add(Unit(hours, "hour"));
        if (left.Minutes > 0) parts.Add(Unit(left.Minutes, "minute"));
        // Seconds are noise once hours are involved.
        if (hours == 0 && left.Seconds > 0) parts.Add(Unit(left.Seconds, "second"));
        return string.Join(" ", parts) + " left";
    }

    private static TimeSpan Whole(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Floor(remaining.TotalSeconds));

    private static string Unit(int count, string name) => count == 1 ? $"1 {name}" : $"{count} {name}s";
}
