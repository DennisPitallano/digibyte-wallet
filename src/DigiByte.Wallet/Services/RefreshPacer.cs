namespace DigiByte.Wallet.Services;

/// <summary>
/// Paces an open page's automatic wallet check. The page ticks every 15 seconds (DigiByte's block time) and asks
/// <see cref="ShouldCheck"/>; after each check it reports whether anything changed. While the wallet is quiet the interval
/// stretches, so a page left open does not keep asking the explorers at full pace:
/// no unchanged check yet → every tick (15 s); one or two → every 2 ticks (30 s); three or more → every 4 ticks (60 s).
/// Any change, or a refresh the user starts, returns to every tick.
/// </summary>
public sealed class RefreshPacer
{
    private int _idleStreak;          // consecutive unchanged checks
    private int _ticksUntilNextCheck = 1;

    /// <summary>Call once per tick. True when this tick should check; false while counting down.</summary>
    public bool ShouldCheck() => --_ticksUntilNextCheck <= 0;

    /// <summary>Call after a check, with whether it surfaced anything new.</summary>
    public void Report(bool changed)
    {
        _idleStreak = changed ? 0 : _idleStreak + 1;
        _ticksUntilNextCheck = _idleStreak switch
        {
            0 => 1,
            <= 2 => 2,
            _ => 4,
        };
    }

    /// <summary>The user asked for fresh data: the next tick checks, at full pace again.</summary>
    public void Reset()
    {
        _idleStreak = 0;
        _ticksUntilNextCheck = 1;
    }
}
