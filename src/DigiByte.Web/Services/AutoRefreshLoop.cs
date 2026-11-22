using DigiByte.Wallet.Services;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// The background check an open page runs so a payment shows up without a manual refresh. One per page that checks (Home,
/// Receive). It ticks every 15 seconds (DigiByte's block time) and avoids needless explorer load in two ways: it skips
/// ticks while the tab is hidden, and it stretches the interval while nothing changes (see <see cref="RefreshPacer"/>).
/// </summary>
public sealed class AutoRefreshLoop(IJSRuntime js) : IAsyncDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly RefreshPacer _pacer = new();
    private Timer? _timer;
    private Func<Task>? _check;
    private Func<bool>? _isBusy;
    private bool _checking;

    public bool IsRunning => _timer is not null;

    /// <summary>
    /// Starts ticking. <paramref name="check"/> does the page's refresh and must call <see cref="Report"/> with whether
    /// anything changed. <paramref name="isBusy"/> lets the page skip a tick while it is already refreshing.
    /// </summary>
    public void Start(Func<Task> check, Func<bool>? isBusy = null)
    {
        if (_timer is not null) return;
        _check = check;
        _isBusy = isBusy;
        _pacer.Reset();
        _timer = new Timer(_ => _ = OnTickAsync(), null, TickInterval, TickInterval);
    }

    /// <summary>After a refresh, from the loop or started by the user: whether it surfaced anything new.</summary>
    public void Report(bool changed) => _pacer.Report(changed);

    /// <summary>The user asked for fresh data: go back to the full pace.</summary>
    public void NotifyManualRefresh() => _pacer.Reset();

    private async Task OnTickAsync()
    {
        if (_checking || _check is null || _isBusy?.Invoke() == true) return;

        // Pause while the tab is hidden (minimised, screen off, switched away).
        try
        {
            if (await js.InvokeAsync<bool>("domHelpers.isHidden")) return;
        }
        catch { /* interop unavailable: assume visible and continue */ }

        // Idle backoff: only check every Nth tick once the wallet looks quiet.
        if (!_pacer.ShouldCheck()) return;

        _checking = true;
        try { await _check(); }
        catch { /* a failed check is retried at the next interval */ }
        finally { _checking = false; }
    }

    public async Task StopAsync()
    {
        if (_timer is null) return;
        var timer = _timer;
        _timer = null;
        _check = null;
        await timer.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
