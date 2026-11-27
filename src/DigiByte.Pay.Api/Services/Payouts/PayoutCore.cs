using System.Security.Cryptography;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>DD payout settings (specs/034). Bound from <c>DigiPay:Payouts</c> plus the env vars in Program.cs.</summary>
public sealed class PayoutOptions
{
    /// <summary>Network confirmations a received payment needs before its DD can be paid out (FR-014).</summary>
    public int MinConfirmations { get; set; } = 6;

    /// <summary>Global pause for incidents (env <c>DIGIPAY_PAYOUTS_PAUSED</c>).</summary>
    public bool Paused { get; set; }

    /// <summary>Key for the operator endpoints (env <c>DIGIPAY_OPERATOR_KEY</c>). Unset = those endpoints answer 503.</summary>
    public string? OperatorKey { get; set; }

    /// <summary>How long a payout address change waits before it takes effect (FR-005).</summary>
    public TimeSpan ChangeHold { get; set; } = TimeSpan.FromHours(48);

    /// <summary>Worker cycle interval.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Set false in tests that drive the worker by hand.</summary>
    public bool WorkerEnabled { get; set; } = true;
}

/// <summary>Shared constants and helpers for the payout services.</summary>
public static class Payout
{
    /// <summary>The DigiDollar network can't send less than 1.00 DD (FR-015).</summary>
    public const long MinPayoutCents = 100;

    public const long MaxThresholdCents = 10_000_000;

    /// <summary>With a threshold, DD is paid out anyway once the oldest held payment is this old (FR-012).</summary>
    public static readonly TimeSpan ThresholdMaxAge = TimeSpan.FromDays(7);

    /// <summary>An unanswered send is declared failed after this long with the node healthy (research R3).</summary>
    public static readonly TimeSpan CheckingGiveUp = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan CheckingStuck = TimeSpan.FromHours(1);

    public static readonly TimeSpan NodeDownAlert = TimeSpan.FromHours(6);

    public static readonly TimeSpan StepUpLifetime = TimeSpan.FromMinutes(5);

    public const int AutoPauseAfterFailures = 3;

    public static readonly string[] Schedules = ["confirmed", "daily", "threshold"];

    public static string NewId(string prefix)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[16];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return prefix + new string(chars);
    }

    /// <summary>Statuses whose items count as covering a credit.</summary>
    public static readonly string[] LiveStatuses =
        [DdPayout.StatusWaiting, DdPayout.StatusSending, DdPayout.StatusChecking, DdPayout.StatusSent, DdPayout.StatusConfirmed];

    /// <summary>Statuses that have taken DD out of the held balance (a debit exists).</summary>
    public static readonly string[] DebitedStatuses =
        [DdPayout.StatusSending, DdPayout.StatusChecking, DdPayout.StatusSent, DdPayout.StatusConfirmed];

    public static readonly string[] InFlightStatuses = [DdPayout.StatusWaiting, DdPayout.StatusSending, DdPayout.StatusChecking];

    /// <summary>Loads a payout owner: the store, or the merchant-level settings when <paramref name="storeId"/> is null.</summary>
    public static async Task<IDdPayoutOwner?> LoadOwnerAsync(DigiPayDbContext db, string merchantId, string? storeId, CancellationToken ct = default) =>
        storeId is null
            ? await db.MerchantDdSettings.FirstOrDefaultAsync(m => m.MerchantId == merchantId, ct)
            : await db.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.MerchantId == merchantId, ct);

    public static async Task<bool> GloballyPausedAsync(DigiPayDbContext db, PayoutOptions options, CancellationToken ct = default) =>
        options.Paused || await db.OperatorSettings.AnyAsync(o => o.Key == OperatorSetting.PayoutsPausedAt && o.Value != null, ct);

    public static async Task<string?> GetSettingAsync(DigiPayDbContext db, string key, CancellationToken ct = default) =>
        (await db.OperatorSettings.FirstOrDefaultAsync(o => o.Key == key, ct))?.Value;

    public static async Task SetSettingAsync(DigiPayDbContext db, string key, string? value, CancellationToken ct = default)
    {
        var row = await db.OperatorSettings.FirstOrDefaultAsync(o => o.Key == key, ct);
        if (row is null) db.OperatorSettings.Add(new OperatorSetting { Key = key, Value = value });
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }

    public static string Iso(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O");

    public static DateTime? ParseIso(string? value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : null;

    /// <summary>
    /// Plain-language text for a failure code, shown to the merchant and sent in <c>payout.failed</c> (FR-027).
    /// </summary>
    public static string FailureText(string? code, string? detail) => code switch
    {
        DdPayout.FailureAddressInvalid => "The payout address is no longer valid for this store. Check it, or set a new one.",
        DdPayout.FailureNotFound => "DigiPay couldn't confirm the payout reached the network, so it was not counted as sent. It will be tried again.",
        DdPayout.FailureRejected => $"The DigiDollar network refused the payout{(string.IsNullOrWhiteSpace(detail) ? "" : $" ({detail})")}.",
        DdPayout.FailureSuperseded => "The payout address changed before this payout was sent. A new payout goes to the current address.",
        _ => "The payout didn't go through.",
    };
}
