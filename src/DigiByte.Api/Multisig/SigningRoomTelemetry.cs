using System.Diagnostics.Metrics;

namespace DigiByte.Api.Multisig;

/// <summary>
/// Anonymous counts and events for signing rooms (FR-028, research R11). Never records room ids, invite codes, keys,
/// addresses, amounts, PSBTs, signatures or names, only event names, reasons, kinds and counts.
/// </summary>
public sealed class SigningRoomTelemetry
{
    public const string MeterName = "DigiByte.Api.SigningRooms";

    private readonly ILogger<SigningRoomTelemetry> _logger;
    private readonly Counter<long> _opened;
    private readonly Counter<long> _ended;
    private readonly Counter<long> _broadcast;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _errors;

    public SigningRoomTelemetry(ILogger<SigningRoomTelemetry> logger, IMeterFactory meters)
    {
        _logger = logger;
        var meter = meters.Create(MeterName);
        _opened = meter.CreateCounter<long>("signing_room.opened");
        _ended = meter.CreateCounter<long>("signing_room.ended");
        _broadcast = meter.CreateCounter<long>("signing_room.broadcast");
        _rateLimited = meter.CreateCounter<long>("signing_room.rate_limited");
        _errors = meter.CreateCounter<long>("signing_room.error");
    }

    public void Opened(int lifetimeMinutes)
    {
        _opened.Add(1, new KeyValuePair<string, object?>("lifetime_minutes", lifetimeMinutes));
        _logger.LogInformation("signing_room.opened lifetime={LifetimeMinutes}", lifetimeMinutes);
    }

    public void Ended(string reason, int participants, int signatures)
    {
        _ended.Add(1, new KeyValuePair<string, object?>("reason", reason));
        _logger.LogInformation("signing_room.ended reason={Reason} participants={Participants} signatures={Signatures}",
            reason, participants, signatures);
    }

    public void Broadcast(bool success)
    {
        var outcome = success ? "success" : "failure";
        _broadcast.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _logger.LogInformation("signing_room.broadcast outcome={Outcome}", outcome);
    }

    public void RateLimited(string kind)
    {
        _rateLimited.Add(1, new KeyValuePair<string, object?>("kind", kind));
        _logger.LogInformation("signing_room.rate_limited kind={Kind}", kind);
    }

    public void Error(string kind)
    {
        _errors.Add(1, new KeyValuePair<string, object?>("kind", kind));
        _logger.LogInformation("signing_room.error kind={Kind}", kind);
    }

    /// <summary>An unexpected exception: only its type is recorded, since messages can echo user input.</summary>
    public void Unexpected(Exception ex)
    {
        _errors.Add(1, new KeyValuePair<string, object?>("kind", "unexpected"));
        _logger.LogWarning("signing_room.error kind=unexpected type={Type}", ex.GetType().Name);
    }
}
