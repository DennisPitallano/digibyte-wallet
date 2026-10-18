using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>FR-016: at most 5 sends per rolling 60 seconds per unlocked session.</summary>
public class DdSendRateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AllowsFive_BlocksTheSixth()
    {
        var l = new DdSendRateLimiter();
        for (var i = 0; i < 5; i++)
        {
            Assert.False(l.IsLimited(T0.AddSeconds(i)));
            l.Record(T0.AddSeconds(i));
        }
        Assert.True(l.IsLimited(T0.AddSeconds(10)));
    }

    [Fact]
    public void WindowIsRolling_OldestSendExpiresAfter60Seconds()
    {
        var l = new DdSendRateLimiter();
        for (var i = 0; i < 5; i++) l.Record(T0.AddSeconds(i * 10)); // at 0,10,20,30,40

        Assert.True(l.IsLimited(T0.AddSeconds(59)));
        Assert.False(l.IsLimited(T0.AddSeconds(60))); // the send at t=0 has aged out of the 60-second window
        l.Record(T0.AddSeconds(60));
        Assert.True(l.IsLimited(T0.AddSeconds(61)));
    }

    [Fact]
    public void NotLimited_WithNoHistory() => Assert.False(new DdSendRateLimiter().IsLimited(T0));

    [Fact]
    public void Reset_ClearsTheWindow()
    {
        var l = new DdSendRateLimiter();
        for (var i = 0; i < 5; i++) l.Record(T0);
        Assert.True(l.IsLimited(T0));

        l.Reset();

        Assert.False(l.IsLimited(T0));
    }

    [Fact]
    public void CustomLimitAndWindow_AreHonored()
    {
        var l = new DdSendRateLimiter(limit: 2, window: TimeSpan.FromSeconds(10));
        l.Record(T0);
        l.Record(T0.AddSeconds(1));
        Assert.True(l.IsLimited(T0.AddSeconds(5)));
        Assert.False(l.IsLimited(T0.AddSeconds(11)));
    }

    [Fact]
    public void Constructor_RejectsNonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DdSendRateLimiter(limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DdSendRateLimiter(window: TimeSpan.Zero));
    }
}
