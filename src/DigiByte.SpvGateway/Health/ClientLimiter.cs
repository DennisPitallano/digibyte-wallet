namespace DigiByte.SpvGateway.Health;

/// <summary>
/// A small per-client token bucket: each client may make <c>perMinute</c> requests in a burst and the allowance refills evenly over a
/// minute. Lives inside the gateway project so the service depends on <c>DigiByte.Crypto</c> only. Thread safe.
/// </summary>
public sealed class ClientLimiter
{
    private const int EvictAbove = 10_000;

    private readonly int _perMinute;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, (double Tokens, DateTimeOffset Updated)> _clients = new();
    private readonly object _gate = new();

    public ClientLimiter(int perMinute, Func<DateTimeOffset>? clock = null)
    {
        if (perMinute < 1)
            throw new ArgumentOutOfRangeException(nameof(perMinute));
        _perMinute = perMinute;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Takes one request's worth from the client's allowance. When none is left, says how many seconds until one is.</summary>
    public bool TryAcquire(string client, out int retryAfterSeconds)
    {
        var now = _clock();
        var refillPerSecond = _perMinute / 60.0;
        lock (_gate)
        {
            var (tokens, updated) = _clients.TryGetValue(client, out var s) ? s : (_perMinute, now);
            tokens = Math.Min(_perMinute, tokens + Math.Max(0, (now - updated).TotalSeconds) * refillPerSecond);

            if (tokens >= 1)
            {
                _clients[client] = (tokens - 1, now);
                retryAfterSeconds = 0;
                Evict(now);
                return true;
            }

            _clients[client] = (tokens, now);
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((1 - tokens) / refillPerSecond));
            return false;
        }
    }

    // Forget clients that have been idle long enough to have a full allowance again, so the table cannot grow without bound.
    private void Evict(DateTimeOffset now)
    {
        if (_clients.Count <= EvictAbove)
            return;
        foreach (var key in _clients.Where(c => (now - c.Value.Updated).TotalMinutes >= 1).Select(c => c.Key).ToList())
            _clients.Remove(key);
    }
}
