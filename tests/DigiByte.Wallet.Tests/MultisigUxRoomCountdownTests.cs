using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The time left in a signing room, and when it starts to stand out (spec 026, FR-031).</summary>
public class MultisigUxRoomCountdownTests
{
    [Theory]
    [InlineData(27 * 60 + 40, "27:40")]
    [InlineData(59 * 60 + 59, "59:59")]
    [InlineData(10 * 60, "10:00")]
    [InlineData(65, "1:05")]
    [InlineData(9, "0:09")]
    public void Under_an_hour_reads_minutes_and_seconds(int seconds, string expected)
    {
        Assert.Equal(expected, RoomCountdown.For(TimeSpan.FromSeconds(seconds)).Text);
    }

    [Theory]
    [InlineData(3600, "1:00:00")]
    [InlineData(3600 + 5 * 60 + 7, "1:05:07")]
    [InlineData(2 * 3600 - 1, "1:59:59")]
    [InlineData(26 * 3600, "26:00:00")]
    public void An_hour_or_more_reads_hours_minutes_and_seconds(int seconds, string expected)
    {
        Assert.Equal(expected, RoomCountdown.For(TimeSpan.FromSeconds(seconds)).Text);
    }

    [Theory]
    [InlineData(5 * 60, true)]
    [InlineData(5 * 60 - 1, true)]
    [InlineData(1, true)]
    [InlineData(5 * 60 + 1, false)]
    [InlineData(30 * 60, false)]
    public void It_stands_out_at_five_minutes_or_less(int seconds, bool urgent)
    {
        Assert.Equal(urgent, RoomCountdown.For(TimeSpan.FromSeconds(seconds)).IsUrgent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Time_that_has_run_out_reads_zero_and_stands_out(int seconds)
    {
        var countdown = RoomCountdown.For(TimeSpan.FromSeconds(seconds));

        Assert.Equal("0:00", countdown.Text);
        Assert.True(countdown.IsUrgent);
    }

    [Fact]
    public void Part_seconds_round_down_so_the_clock_never_shows_time_it_does_not_have()
    {
        Assert.Equal("4:59", RoomCountdown.For(TimeSpan.FromSeconds(299.9)).Text);
    }

    [Theory]
    [InlineData(27 * 60 + 40, "27 minutes 40 seconds left")]
    [InlineData(5 * 60, "5 minutes left")]
    [InlineData(61, "1 minute 1 second left")]
    [InlineData(45, "45 seconds left")]
    [InlineData(3600 + 120, "1 hour 2 minutes left")]
    [InlineData(0, "No time left")]
    public void Spoken_form_for_assistive_technology(int seconds, string expected)
    {
        Assert.Equal(expected, RoomCountdown.Spoken(TimeSpan.FromSeconds(seconds)));
    }
}
