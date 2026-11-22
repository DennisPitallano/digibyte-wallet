using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// How often an open page re-checks the wallet (spec 025, FR-024, T021). This is Home's poller pacing, moved unchanged so
/// Receive checks at exactly the same pace: one tick is 15 seconds; a check every tick while things change, every 2 ticks
/// after one or two unchanged checks, every 4 ticks (60 s) after three or more.
/// </summary>
public class RefreshPacerTests
{
    /// <summary>Runs ticks until the pacer allows a check, and returns how many ticks that took.</summary>
    private static int TicksUntilCheck(RefreshPacer pacer)
    {
        for (var ticks = 1; ticks <= 10; ticks++)
            if (pacer.ShouldCheck()) return ticks;
        throw new InvalidOperationException("the pacer never allowed a check");
    }

    [Fact]
    public void TheFirstTick_Checks()
    {
        Assert.True(new RefreshPacer().ShouldCheck());
    }

    [Fact]
    public void WhileThingsChange_EveryTickChecks()
    {
        var pacer = new RefreshPacer();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(1, TicksUntilCheck(pacer));
            pacer.Report(changed: true);
        }
    }

    [Fact]
    public void UnchangedChecks_StretchTheInterval_ToTwoTicks_ThenFour_AndStayThere()
    {
        var pacer = new RefreshPacer();
        Assert.Equal(1, TicksUntilCheck(pacer));

        var gaps = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            pacer.Report(changed: false);
            gaps.Add(TicksUntilCheck(pacer));
        }

        // 30 s, 30 s, then 60 s for as long as nothing changes.
        Assert.Equal([2, 2, 4, 4, 4, 4], gaps);
    }

    [Fact]
    public void AChange_ReturnsToEveryTick()
    {
        var pacer = new RefreshPacer();
        TicksUntilCheck(pacer);
        for (var i = 0; i < 4; i++) { pacer.Report(changed: false); TicksUntilCheck(pacer); }

        pacer.Report(changed: true);

        Assert.Equal(1, TicksUntilCheck(pacer));
    }

    [Fact]
    public void Reset_MakesTheNextTickCheck_AndForgetsTheQuietStreak()
    {
        var pacer = new RefreshPacer();
        TicksUntilCheck(pacer);
        for (var i = 0; i < 4; i++) { pacer.Report(changed: false); TicksUntilCheck(pacer); }
        pacer.Report(changed: false);

        pacer.Reset();

        Assert.True(pacer.ShouldCheck());
        pacer.Report(changed: false);
        Assert.Equal(2, TicksUntilCheck(pacer)); // one unchanged check after the reset, not five
    }

    [Fact]
    public void ATickThatDoesNotCheck_StillCountsDown()
    {
        var pacer = new RefreshPacer();
        TicksUntilCheck(pacer);
        pacer.Report(changed: false);

        Assert.False(pacer.ShouldCheck());
        Assert.True(pacer.ShouldCheck());
    }
}
