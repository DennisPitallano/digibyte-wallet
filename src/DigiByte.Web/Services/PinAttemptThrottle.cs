using DigiByte.Wallet.Storage;

namespace DigiByte.Web.Services;

/// <summary>
/// Slows repeated wrong PINs or passphrases (the same rule for both): after 3 failures, 15 seconds, doubling with each further failure, at most 5 doublings.
/// Shared by the unlock and PSBT signing pages, so the rule exists in one place. Each page keeps its own instance, so the
/// count resets when the page is left: it slows guessing rather than guaranteeing a limit.
/// </summary>
public sealed class PinAttemptThrottle
{
    private const int FreeAttempts = 3;
    private const int BaseDelaySeconds = 15;
    private const int MaxDoublings = 5;

    // English wording, used when no localiser is set. The locale files carry the same keys.
    private static readonly Dictionary<string, string> English = new()
    {
        ["credential.incorrect_pin"] = "Incorrect PIN.",
        ["credential.incorrect_passphrase"] = "Incorrect passphrase.",
        ["throttle.remaining_one"] = "{0} 1 attempt remaining before lockout.",
        ["throttle.remaining_many"] = "{0} {1} attempts remaining before lockout.",
        ["throttle.locked"] = "{0} Locked for {1}s after {2} failed attempts.",
        ["throttle.lockout_wait"] = "Too many attempts. Try again in {0}s.",
    };

    private readonly Func<DateTime> _utcNow;
    private DateTime? _lockedUntil;

    public PinAttemptThrottle(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Which credential this wallet uses, so messages say "PIN" or "passphrase". Thresholds do not depend on it.</summary>
    public CredentialKind Kind { get; set; } = CredentialKind.Pin;

    /// <summary>Reads a message by key with format arguments, for example <c>LocalizationService.T</c>. Null means English.</summary>
    public Func<string, object[], string>? Localize { get; set; }

    public int FailedAttempts { get; private set; }

    public bool IsLockedOut => _lockedUntil.HasValue && _utcNow() < _lockedUntil.Value;

    private string Text(string key, params object[] args) =>
        Localize is not null ? Localize(key, args) : string.Format(English[key], args);

    private string Incorrect() =>
        Text(Kind == CredentialKind.Passphrase ? "credential.incorrect_passphrase" : "credential.incorrect_pin");

    /// <summary>What to show when a credential is entered during the lockout.</summary>
    public string LockoutMessage
    {
        get
        {
            var remaining = (_lockedUntil ?? _utcNow()) - _utcNow();
            return Text("throttle.lockout_wait", (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds)));
        }
    }

    /// <summary>Records a wrong credential and returns the message to show.</summary>
    public string RecordFailure()
    {
        FailedAttempts++;
        if (FailedAttempts < FreeAttempts)
        {
            var left = FreeAttempts - FailedAttempts;
            return left == 1
                ? Text("throttle.remaining_one", Incorrect())
                : Text("throttle.remaining_many", Incorrect(), left);
        }

        var delaySeconds = BaseDelaySeconds * (int)Math.Pow(2, Math.Min(FailedAttempts - FreeAttempts, MaxDoublings));
        _lockedUntil = _utcNow().AddSeconds(delaySeconds);
        return Text("throttle.locked", Incorrect(), delaySeconds, FailedAttempts);
    }

    public void RecordSuccess()
    {
        FailedAttempts = 0;
        _lockedUntil = null;
    }
}
