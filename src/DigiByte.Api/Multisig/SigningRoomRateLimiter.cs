using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Multisig;

/// <summary>
/// Per-connection limits for the signing-room hub (FR-027): failed join attempts per minute and rooms opened per hour.
/// ASP.NET's rate limiter covers HTTP requests, not hub calls, so the hub keeps its own windows (research R10).
/// </summary>
public sealed class SigningRoomRateLimiter
{
    private static readonly TimeSpan JoinWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OpenWindow = TimeSpan.FromHours(1);

    private readonly TimeProvider _time;
    private readonly SigningRoomOptions _options;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _failedJoins = new();
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _opens = new();

    public SigningRoomRateLimiter(TimeProvider time, IOptions<SigningRoomOptions> options)
    {
        _time = time;
        _options = options.Value;
    }

    /// <summary>True when the connection has used up its failed-join allowance for the last minute.</summary>
    public bool IsJoinLimited(string connectionId) =>
        _failedJoins.TryGetValue(connectionId, out var q) && Count(q, JoinWindow) >= Math.Max(1, _options.FailedJoinsPerMinute);

    public void RecordJoinFailure(string connectionId) =>
        Add(_failedJoins.GetOrAdd(connectionId, _ => new Queue<DateTimeOffset>()), JoinWindow);

    /// <summary>Counts an open attempt; false (and not counted) when the connection is over its hourly allowance.</summary>
    public bool TryRecordOpen(string connectionId)
    {
        var q = _opens.GetOrAdd(connectionId, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            if (Count(q, OpenWindow) >= Math.Max(1, _options.RoomsOpenedPerHour)) return false;
            q.Enqueue(_time.GetUtcNow());
            return true;
        }
    }

    public void Forget(string connectionId)
    {
        _failedJoins.TryRemove(connectionId, out _);
        _opens.TryRemove(connectionId, out _);
    }

    private int Count(Queue<DateTimeOffset> q, TimeSpan window)
    {
        lock (q)
        {
            var cutoff = _time.GetUtcNow() - window;
            while (q.Count > 0 && q.Peek() <= cutoff) q.Dequeue();
            return q.Count;
        }
    }

    private void Add(Queue<DateTimeOffset> q, TimeSpan window)
    {
        lock (q)
        {
            Count(q, window);
            q.Enqueue(_time.GetUtcNow());
        }
    }
}
