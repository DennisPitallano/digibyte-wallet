namespace DigiByte.Wallet.Services;

public enum ReadSourceKind
{
    Explorer,
    OwnNode,
    Gateway,
    Demo,
}

public enum ReadSourceTone
{
    Good,
    Waiting,
    Bad,
    Off,
}

/// <summary>
/// One source the wallet reads blockchain data from, as the wallet fallback service would use it right now: where it stands in the
/// order, its current state and how it has answered during this session. Nothing here is wallet data.
/// </summary>
/// <param name="Role">"Primary", "Fallback 1", "Fallback 2", ... for a source that is in the order, or "Standby" for one that is not (yet).</param>
/// <param name="InUse">True when the source takes part in the order at this moment.</param>
/// <param name="Status">A short word for its state: ready, active, cooling down, stabilizing, off, ...</param>
/// <param name="Note">A plain sentence on what the position means, or why the source is not in use.</param>
/// <param name="AnsweredLast">True for the source that answered the most recent read.</param>
public sealed record ReadSourceEntry(
    string Key,
    string Name,
    ReadSourceKind Kind,
    string Role,
    bool InUse,
    string Status,
    ReadSourceTone Tone,
    string? Note,
    bool AnsweredLast,
    long Successes,
    long Failures,
    DateTime? LastSuccessUtc,
    DateTime? LastFailureUtc,
    DateTime? CooldownUntilUtc);

/// <summary>How many reads a source answered or failed in this session, and when.</summary>
internal sealed class SourceCounters
{
    private long _successes;
    private long _failures;
    private long _lastSuccessTicks;
    private long _lastFailureTicks;

    public long Successes => Interlocked.Read(ref _successes);
    public long Failures => Interlocked.Read(ref _failures);
    public DateTime? LastSuccessUtc => Ticks(Interlocked.Read(ref _lastSuccessTicks));
    public DateTime? LastFailureUtc => Ticks(Interlocked.Read(ref _lastFailureTicks));

    public void Success()
    {
        Interlocked.Increment(ref _successes);
        Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);
    }

    public void Failure()
    {
        Interlocked.Increment(ref _failures);
        Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);
    }

    private static DateTime? Ticks(long t) => t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
}
