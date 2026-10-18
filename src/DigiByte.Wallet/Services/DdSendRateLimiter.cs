namespace DigiByte.Wallet.Services;

/// <summary>
/// Client-side guard for FR-016: no more than <see cref="Limit"/> sends in any rolling <see cref="Window"/> per unlocked
/// wallet session (default 5 per 60 seconds). Resets when the wallet locks or changes. Only successful broadcasts are recorded.
/// </summary>
public sealed class DdSendRateLimiter
{
    private readonly List<DateTimeOffset> _sends = [];
    private readonly object _gate = new();

    public DdSendRateLimiter(int limit = 5, TimeSpan? window = null)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (window is { } w && w <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        Limit = limit;
        Window = window ?? TimeSpan.FromSeconds(60);
    }

    public int Limit { get; }

    public TimeSpan Window { get; }

    public bool IsLimited(DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            return _sends.Count >= Limit;
        }
    }

    public void Record(DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            _sends.Add(now);
        }
    }

    public void Reset()
    {
        lock (_gate) { _sends.Clear(); }
    }

    private void Prune(DateTimeOffset now) => _sends.RemoveAll(t => now - t >= Window);
}
