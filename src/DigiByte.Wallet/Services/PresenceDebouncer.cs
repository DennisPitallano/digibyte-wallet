namespace DigiByte.Wallet.Services;

/// <summary>
/// Smooths online/offline so a brief reconnect does not flicker: a co-signer who goes offline is only shown as Offline
/// after staying offline for the hold time (about 10 seconds). Coming back online shows at once. Display-only; it never
/// changes what the room itself believes (specs/018-multisig-ux-polish, FR-002).
/// </summary>
public sealed class PresenceDebouncer
{
    public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(10);

    private sealed class Entry
    {
        public bool RawOnline;
        public DateTimeOffset OfflineSince;
    }

    private readonly TimeProvider _clock;
    private readonly TimeSpan _hold;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public PresenceDebouncer(TimeProvider? clock = null, TimeSpan? hold = null)
    {
        _clock = clock ?? TimeProvider.System;
        _hold = hold ?? DefaultHold;
    }

    /// <summary>Records what the room last said about a co-signer.</summary>
    public void Observe(string key, bool online)
    {
        var now = _clock.GetUtcNow();
        if (!_entries.TryGetValue(key, out var entry))
        {
            // First sighting: someone already offline is shown as offline straight away.
            _entries[key] = new Entry { RawOnline = online, OfflineSince = online ? default : now - _hold };
            return;
        }

        if (online)
        {
            entry.RawOnline = true;
        }
        else if (entry.RawOnline)
        {
            entry.RawOnline = false;
            entry.OfflineSince = now;
        }
        // Already offline: keep the original time so repeated reports do not restart the hold.
    }

    /// <summary>What to show: online, unless the co-signer has been offline for the whole hold time.</summary>
    public bool IsOnline(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) return false;
        return entry.RawOnline || _clock.GetUtcNow() - entry.OfflineSince < _hold;
    }

    /// <summary>True while someone is offline but not yet shown as such; the screen should re-check shortly.</summary>
    public bool HasPendingChange
    {
        get
        {
            var now = _clock.GetUtcNow();
            return _entries.Values.Any(e => !e.RawOnline && now - e.OfflineSince < _hold);
        }
    }
}
