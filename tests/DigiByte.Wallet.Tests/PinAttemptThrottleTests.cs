using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The unlock page's rule, now shared: 3 failures, then 15 seconds, doubling each failure, at most 5 doublings.</summary>
public class PinAttemptThrottleTests
{
    private DateTime _now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private PinAttemptThrottle NewThrottle() => new(() => _now);

    [Fact]
    public void TwoFailures_DontLockOut_AndSayHowManyAreLeft()
    {
        var throttle = NewThrottle();

        Assert.Equal("Incorrect PIN. 2 attempts remaining before lockout.", throttle.RecordFailure());
        Assert.Equal("Incorrect PIN. 1 attempt remaining before lockout.", throttle.RecordFailure());

        Assert.False(throttle.IsLockedOut);
    }

    [Fact]
    public void ThirdFailure_LocksOutFor15Seconds()
    {
        var throttle = NewThrottle();
        throttle.RecordFailure();
        throttle.RecordFailure();

        var message = throttle.RecordFailure();

        Assert.Equal("Incorrect PIN. Locked for 15s after 3 failed attempts.", message);
        Assert.True(throttle.IsLockedOut);
        Assert.Equal("Too many attempts. Try again in 15s.", throttle.LockoutMessage);
        _now = _now.AddSeconds(15);
        Assert.False(throttle.IsLockedOut);
    }

    [Theory]
    [InlineData(4, 30)]
    [InlineData(5, 60)]
    [InlineData(8, 480)]
    [InlineData(9, 480)]
    [InlineData(12, 480)]
    public void EachLaterFailure_DoublesTheDelay_UpToFiveDoublings(int failures, int expectedSeconds)
    {
        var throttle = NewThrottle();
        string message = "";
        for (var i = 0; i < failures; i++) message = throttle.RecordFailure();

        Assert.Equal($"Incorrect PIN. Locked for {expectedSeconds}s after {failures} failed attempts.", message);
    }

    [Fact]
    public void Success_ResetsTheCount()
    {
        var throttle = NewThrottle();
        for (var i = 0; i < 4; i++) throttle.RecordFailure();

        throttle.RecordSuccess();

        Assert.False(throttle.IsLockedOut);
        Assert.Equal(0, throttle.FailedAttempts);
        Assert.Equal("Incorrect PIN. 2 attempts remaining before lockout.", throttle.RecordFailure());
    }

    [Fact]
    public void LockoutMessage_RoundsRemainingSecondsUp()
    {
        var throttle = NewThrottle();
        for (var i = 0; i < 3; i++) throttle.RecordFailure();

        _now = _now.AddSeconds(10.2);

        Assert.Equal("Too many attempts. Try again in 5s.", throttle.LockoutMessage);
    }

    [Fact]
    public void A_passphrase_wallet_is_told_about_the_passphrase_not_the_PIN()
    {
        var throttle = NewThrottle();
        throttle.Kind = CredentialKind.Passphrase;

        Assert.Equal("Incorrect passphrase. 2 attempts remaining before lockout.", throttle.RecordFailure());
        Assert.Equal("Incorrect passphrase. 1 attempt remaining before lockout.", throttle.RecordFailure());
        Assert.Equal("Incorrect passphrase. Locked for 15s after 3 failed attempts.", throttle.RecordFailure());
    }

    [Fact]
    public void Thresholds_and_delays_are_identical_for_both_credential_types()
    {
        var pin = NewThrottle();
        var phrase = NewThrottle();
        phrase.Kind = CredentialKind.Passphrase;

        for (var i = 0; i < 8; i++)
        {
            pin.RecordFailure();
            phrase.RecordFailure();
            Assert.Equal(pin.IsLockedOut, phrase.IsLockedOut);
            Assert.Equal(pin.LockoutMessage, phrase.LockoutMessage);
            _now = _now.AddSeconds(1);
        }
    }

    [Fact]
    public void Switching_the_credential_kind_changes_the_wording_from_then_on()
    {
        var throttle = NewThrottle();
        Assert.StartsWith("Incorrect PIN.", throttle.RecordFailure());

        throttle.Kind = CredentialKind.Passphrase;

        Assert.StartsWith("Incorrect passphrase.", throttle.RecordFailure());
    }
}
