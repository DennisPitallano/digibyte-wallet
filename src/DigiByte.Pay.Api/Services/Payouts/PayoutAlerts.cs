using System.Collections.Concurrent;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Operator alerts for DD payouts (specs/034 contracts/operator-api.md). Each is a log entry with EventId name
/// <c>DdPayoutAlert</c> and a structured <c>alert</c> property that Railway alert rules match on. Repeats of the same
/// alert for the same owner are limited to once an hour.
/// </summary>
public sealed class PayoutAlerts
{
    public const string FeeReserveLow = "fee_reserve_low";
    public const string CoverageShort = "coverage_short";
    public const string NodeUnavailable6h = "node_unavailable_6h";
    public const string ReversalAbsorbed = "reversal_absorbed";
    public const string UnattributedReceive = "unattributed_receive";
    public const string StoreAutoPaused = "store_auto_paused";
    public const string CheckingStuck = "checking_stuck";

    private static readonly EventId AlertEvent = new(3401, "DdPayoutAlert");
    private readonly ConcurrentDictionary<string, DateTime> _lastRaised = new();
    private readonly ConcurrentQueue<(string Alert, string? Subject)> _raised = new();
    private readonly ILogger<PayoutAlerts> _log;

    public PayoutAlerts(ILogger<PayoutAlerts> log) => _log = log;

    /// <summary>Alerts raised since start, for the operator health endpoint and tests.</summary>
    public IReadOnlyCollection<(string Alert, string? Subject)> Raised => _raised.ToArray();

    public void Raise(string alert, string? subject, string message, params object?[] args)
    {
        var key = $"{alert}|{subject}";
        var now = DateTime.UtcNow;
        if (_lastRaised.TryGetValue(key, out var last) && now - last < TimeSpan.FromHours(1)) return;
        _lastRaised[key] = now;
        _raised.Enqueue((alert, subject));

        var level = alert switch
        {
            CoverageShort or ReversalAbsorbed => LogLevel.Critical,
            FeeReserveLow or NodeUnavailable6h or CheckingStuck => LogLevel.Error,
            _ => LogLevel.Warning,
        };
        using (_log.BeginScope(new Dictionary<string, object?> { ["alert"] = alert, ["subject"] = subject }))
        {
#pragma warning disable CA2254 // message templates are fixed per call site
            _log.Log(level, AlertEvent, "[DdPayoutAlert:" + alert + "] " + message, args);
#pragma warning restore CA2254
        }
    }

    /// <summary>Forget the throttle for an alert so the next occurrence logs again (used once a condition clears).</summary>
    public void Clear(string alert, string? subject) => _lastRaised.TryRemove($"{alert}|{subject}", out _);
}
