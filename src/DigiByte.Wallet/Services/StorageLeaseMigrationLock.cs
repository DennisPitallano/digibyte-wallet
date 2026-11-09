using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Fallback migration lock for browsers without the Web Locks API: a short lease stored in secure storage.
/// It is best effort (storage has no compare-and-set), so it re-reads after writing and backs off if another
/// holder won; migration itself stays safe without it because it only ever adds a verified copy.
/// </summary>
public sealed class StorageLeaseMigrationLock : IMigrationLock
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private readonly ISecureStorage _storage;
    private readonly TimeProvider _time;

    public StorageLeaseMigrationLock(ISecureStorage storage, TimeProvider? time = null)
    {
        _storage = storage;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string name)
    {
        var key = "lock_" + name;
        var now = _time.GetUtcNow();

        var existing = await _storage.GetAsync(key);
        if (existing is not null && TryParse(existing, out var expires) && expires > now)
            return null; // someone else holds a live lease

        var token = Guid.NewGuid().ToString("N");
        await _storage.SetAsync(key, $"{token}|{(now + LeaseDuration).ToUnixTimeMilliseconds()}");

        var confirmed = await _storage.GetAsync(key);
        if (confirmed is null || !confirmed.StartsWith(token, StringComparison.Ordinal))
            return null; // lost the race

        return new Lease(_storage, key, token);
    }

    private static bool TryParse(string value, out DateTimeOffset expires)
    {
        expires = default;
        var parts = value.Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[1], out var ms)) return false;
        expires = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return true;
    }

    private sealed class Lease(ISecureStorage storage, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            var current = await storage.GetAsync(key);
            if (current is not null && current.StartsWith(token, StringComparison.Ordinal))
                await storage.RemoveAsync(key);
        }
    }
}
