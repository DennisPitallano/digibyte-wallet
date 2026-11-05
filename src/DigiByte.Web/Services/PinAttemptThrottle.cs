namespace DigiByte.Web.Services;

/// <summary>
/// Slows repeated wrong PINs: after 3 failures, 15 seconds, doubling with each further failure, at most 5 doublings.
/// Shared by the unlock and PSBT signing pages, so the rule exists in one place. Each page keeps its own instance, so the
/// count resets when the page is left: it slows guessing rather than guaranteeing a limit.
/// </summary>
public sealed class PinAttemptThrottle
{
    private const int FreeAttempts = 3;
    private const int BaseDelaySeconds = 15;
    private const int MaxDoublings = 5;

    private readonly Func<DateTime> _utcNow;
    private DateTime? _lockedUntil;

    public PinAttemptThrottle(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public int FailedAttempts { get; private set; }

    public bool IsLockedOut => _lockedUntil.HasValue && _utcNow() < _lockedUntil.Value;

    /// <summary>What to show when a PIN is entered during the lockout.</summary>
    public string LockoutMessage
    {
        get
        {
            var remaining = (_lockedUntil ?? _utcNow()) - _utcNow();
            return $"Too many attempts. Try again in {(int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds))}s.";
        }
    }

    /// <summary>Records a wrong PIN and returns the message to show.</summary>
    public string RecordFailure()
    {
        FailedAttempts++;
        if (FailedAttempts < FreeAttempts)
            return $"Incorrect PIN. {FreeAttempts - FailedAttempts} attempts remaining before lockout.";

        var delaySeconds = BaseDelaySeconds * (int)Math.Pow(2, Math.Min(FailedAttempts - FreeAttempts, MaxDoublings));
        _lockedUntil = _utcNow().AddSeconds(delaySeconds);
        return $"Incorrect PIN. Locked for {delaySeconds}s after {FailedAttempts} failed attempts.";
    }

    public void RecordSuccess()
    {
        FailedAttempts = 0;
        _lockedUntil = null;
    }
}
