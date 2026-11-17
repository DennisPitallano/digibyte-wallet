using DigiByte.Wallet.Services;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.Wallet.Tests;

/// <summary>Offline shows after about 10 seconds; coming back shows at once (spec 018, FR-002).</summary>
public class MultisigUxPresenceTests
{
    private static (PresenceDebouncer Sut, FakeTimeProvider Clock) Make()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        return (new PresenceDebouncer(clock), clock);
    }

    [Fact]
    public void Unknown_key_defaults_to_offline()
    {
        var (sut, _) = Make();
        Assert.False(sut.IsOnline("aa"));
    }

    [Fact]
    public void Online_is_shown_immediately()
    {
        var (sut, _) = Make();
        sut.Observe("aa", true);
        Assert.True(sut.IsOnline("aa"));
    }

    [Fact]
    public void Going_offline_is_held_for_ten_seconds()
    {
        var (sut, clock) = Make();
        sut.Observe("aa", true);
        sut.Observe("aa", false);
        Assert.True(sut.IsOnline("aa"));
        Assert.True(sut.HasPendingChange);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.True(sut.IsOnline("aa"));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(sut.IsOnline("aa"));
        Assert.False(sut.HasPendingChange);
    }

    [Fact]
    public void A_brief_blip_never_shows_offline()
    {
        var (sut, clock) = Make();
        sut.Observe("aa", true);
        sut.Observe("aa", false);
        clock.Advance(TimeSpan.FromSeconds(4));
        sut.Observe("aa", true);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(sut.IsOnline("aa"));
    }

    [Fact]
    public void Repeated_offline_reports_do_not_restart_the_hold()
    {
        var (sut, clock) = Make();
        sut.Observe("aa", true);
        sut.Observe("aa", false);
        clock.Advance(TimeSpan.FromSeconds(6));
        sut.Observe("aa", false);
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(sut.IsOnline("aa"));
    }

    [Fact]
    public void Keys_are_case_insensitive()
    {
        var (sut, _) = Make();
        sut.Observe("AbC", true);
        Assert.True(sut.IsOnline("abc"));
    }
}
