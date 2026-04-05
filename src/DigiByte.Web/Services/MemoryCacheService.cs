namespace DigiByte.Web.Services;

/// <summary>
/// Lightweight in-memory cache with TTL and request deduplication.
/// Scoped per-session — avoids redundant API calls across page navigations.
/// </summary>
public sealed class MemoryCacheService
{
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly Dictionary<string, object> _inFlight = new();

    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            return (T)entry.Value;
        if (entry is { IsExpired: true })
            _cache.Remove(key);
        return default;
    }

    public bool TryGet<T>(string key, out T value)
    {
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            value = (T)entry.Value;
            return true;
        }
        if (entry is { IsExpired: true })
            _cache.Remove(key);
        value = default!;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null) where T : notnull
    {
        _cache[key] = new CacheEntry(value, ttl.HasValue ? DateTime.UtcNow + ttl.Value : null);
    }

    public void Remove(string key) => _cache.Remove(key);

    /// <summary>Remove all keys matching a prefix (e.g. "balance" clears "balance:mainnet").</summary>
    public void RemoveByPrefix(string prefix)
    {
        var keys = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keys) _cache.Remove(key);
    }

    public void Clear() => _cache.Clear();

    /// <summary>
    /// Get from cache or fetch. Deduplicates concurrent in-flight requests for the same key.
    /// </summary>
    public async Task<T> GetOrFetchAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) where T : notnull
    {
        if (TryGet<T>(key, out var cached))
            return cached;

        // Dedup: return existing in-flight task instead of starting a new one
        if (_inFlight.TryGetValue(key, out var existing))
            return await (Task<T>)existing;

        var task = factory();
        _inFlight[key] = task;
        try
        {
            var result = await task;
            Set(key, result, ttl);
            return result;
        }
        finally
        {
            _inFlight.Remove(key);
        }
    }

    private sealed record CacheEntry(object Value, DateTime? ExpiresAt)
    {
        public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }
}
