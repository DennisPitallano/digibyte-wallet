using System.Collections.Concurrent;
using System.Net;

namespace DigiByte.NodeApi.Metrics;

/// <summary>
/// Per-client fixed-window limiter for the anonymous counter endpoint (default 60 events per minute). The client address is
/// used only as an in-memory key for the current window; it is never stored, logged, or written anywhere, and entries are
/// dropped when their window ends.
/// </summary>
public sealed class MetricsRateLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<IPAddress, (DateTimeOffset Start, int Count)> _clients = new();
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public MetricsRateLimiter(int limit = 60, TimeSpan? window = null)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public TimeSpan Window => _window;

    /// <summary>True when this client may record another event now.</summary>
    public bool Allow(IPAddress? client, DateTimeOffset now)
    {
        PruneIfDue(now);
        // A request with no address (should not happen) shares one bucket rather than bypassing the limit.
        var key = client ?? IPAddress.None;

        var entry = _clients.AddOrUpdate(
            key,
            _ => (now, 1),
            (_, current) => now - current.Start >= _window ? (now, 1) : (current.Start, current.Count + 1));

        return entry.Count <= _limit;
    }

    private void PruneIfDue(DateTimeOffset now)
    {
        if (now - _lastPrune < _window) return;
        _lastPrune = now;
        foreach (var (key, value) in _clients)
            if (now - value.Start >= _window)
                _clients.TryRemove(new KeyValuePair<IPAddress, (DateTimeOffset, int)>(key, value));
    }
}
