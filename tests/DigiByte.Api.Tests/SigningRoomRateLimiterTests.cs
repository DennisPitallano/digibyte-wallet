using DigiByte.Api.Multisig;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.Api.Tests;

/// <summary>Per-connection limits for the signing-room hub (FR-027, T007).</summary>
public class SigningRoomRateLimiterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly SigningRoomRateLimiter _limits;

    public SigningRoomRateLimiterTests() => _limits = new SigningRoomRateLimiter(_clock, Options.Create(new SigningRoomOptions()));

    [Fact]
    public void The_sixth_failed_join_within_a_minute_is_limited_and_clears_after_the_window()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.False(_limits.IsJoinLimited("c1"));
            _limits.RecordJoinFailure("c1");
        }
        Assert.True(_limits.IsJoinLimited("c1"));
        Assert.False(_limits.IsJoinLimited("c2"));

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(_limits.IsJoinLimited("c1"));
    }

    [Fact]
    public void The_eleventh_open_within_an_hour_is_limited()
    {
        for (var i = 0; i < 10; i++) Assert.True(_limits.TryRecordOpen("c1"));
        Assert.False(_limits.TryRecordOpen("c1"));
        Assert.True(_limits.TryRecordOpen("c2"));

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(_limits.TryRecordOpen("c1"));
    }

    [Fact]
    public void Forgetting_a_connection_drops_its_windows()
    {
        for (var i = 0; i < 5; i++) _limits.RecordJoinFailure("c1");
        _limits.Forget("c1");
        Assert.False(_limits.IsJoinLimited("c1"));
    }
}
