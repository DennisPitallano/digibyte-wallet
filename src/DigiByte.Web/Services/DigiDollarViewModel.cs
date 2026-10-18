using System.Globalization;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Shared, per-session view state for the DigiDollar UI (Home tile, notice line, pages). All rules live in the
/// tested Wallet project (<see cref="DigiDollarVisibility"/>, <see cref="IDigiDollarService"/>,
/// <see cref="DigiDollarFormat"/>); this class only holds the latest result, schedules refreshes, and tells the UI
/// to re-render. It never touches DGB code and a DigiDollar failure never throws into a page (Principle V).
/// </summary>
public sealed class DigiDollarViewModel : IDisposable
{
    /// <summary>Foreground refresh interval (FR-010, research R6): one block.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly IDigiDollarService _service;
    private readonly IDdSessionProvider _sessions;
    private readonly DigiDollarStore _store;
    private readonly DigiDollarFeatureFlag _flag;
    private readonly AppState _app;
    private readonly LocalizationService _loc;
    private readonly IJSRuntime _js;
    private readonly IConfiguration _config;
    private readonly NotificationService _notifications;

    private string? _loadedFor;
    private CancellationTokenSource? _pollCts;
    private bool _refreshing;
    private bool _used;
    private string? _usedFor;
    private int _pollOwners;

    public DigiDollarViewModel(
        IDigiDollarService service, IDdSessionProvider sessions, DigiDollarStore store, DigiDollarFeatureFlag flag,
        AppState app, LocalizationService loc, IJSRuntime js, IConfiguration config, NotificationService notifications)
    {
        _config = config;
        _notifications = notifications;
        _service = service;
        _sessions = sessions;
        _store = store;
        _flag = flag;
        _app = app;
        _loc = loc;
        _js = js;
        _app.OnChange += OnAppChanged;
    }

    /// <summary>The wallet locked: forget the dashboard and the send rate limit (FR-016).</summary>
    private void OnAppChanged()
    {
        if (_app.IsWalletUnlocked) return;
        if (Dashboard is null && _loadedFor is null) return;
        Reset();
    }

    /// <summary>A different wallet is active than the one the dashboard belongs to: start over.</summary>
    private void SyncSession()
    {
        var id = _sessions.GetDigiDollarSession()?.WalletId;
        if (id == _loadedFor) return;
        Dashboard = null;
        Scan = null;
        _used = false;
        _usedFor = null;
        _service.ResetSession();
        _loadedFor = id;
    }

    public event Action? Changed;

    /// <summary>The latest result; null until the first refresh completes.</summary>
    public DdDashboard? Dashboard { get; private set; }

    public bool IsRefreshing => _refreshing;

    /// <summary>Restore-scan progress while one is running (FR-007), otherwise null.</summary>
    public DdScanProgress? Scan { get; private set; }

    public CultureInfo Culture => DigiDollarFormat.CultureFor(_loc.CurrentLocale);

    public string Usd(long cents) => DigiDollarFormat.Usd(cents, Culture);

    /// <summary>
    /// Block-explorer link for a transaction (FR-009), from <c>DigiDollar:ExplorerTxUrlMainnet</c> /
    /// <c>DigiDollar:ExplorerTxUrlTestnet</c>. Null when no explorer is configured for the network, so no dead link is shown.
    /// </summary>
    public string? ExplorerTxUrl(string txid)
    {
        var key = _app.NetworkMode == "mainnet" ? "DigiDollar:ExplorerTxUrlMainnet" : "DigiDollar:ExplorerTxUrlTestnet";
        var baseUrl = _config[key];
        return string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(txid)}";
    }

    /// <summary>Effective visibility for the active wallet right now (contracts/wallet-ui-contract.md).</summary>
    public DdVisibility Visibility
    {
        get
        {
            var session = _sessions.GetDigiDollarSession();
            var health = Dashboard?.Health;
            return DigiDollarVisibility.Evaluate(
                _flag.Flags,
                _app.NetworkMode,
                session?.Kind ?? DdWalletKind.None,
                backendReachable: health?.Available ?? true,
                deploymentActive: health?.DeploymentActive ?? true,
                usedDigiDollarBefore: _used && _usedFor == session?.WalletId);
        }
    }

    /// <summary>Read the plaintext "has used DigiDollar" marker for the active wallet (drives the neutral home line).</summary>
    public async Task RefreshUsedMarkerAsync()
    {
        var id = _sessions.GetDigiDollarSession()?.WalletId;
        if (id is null) { _used = false; _usedFor = null; return; }
        try { _used = await _store.HasUsedAsync(id); }
        catch { _used = false; }
        _usedFor = id;
    }

    /// <summary>Load once when visible; safe to call from every page and component.</summary>
    public async Task EnsureLoadedAsync()
    {
        SyncSession();
        await RefreshUsedMarkerAsync();
        if (Dashboard is null && Visibility.Visible) await RefreshAsync();
        else Changed?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        SyncSession();
        if (_refreshing || !Visibility.Visible) return;
        _refreshing = true;
        Changed?.Invoke();
        try
        {
            Dashboard = await _service.RefreshAsync(ct, new Progress<DdScanProgress>(p =>
            {
                Scan = p;
                Changed?.Invoke();
            }));
            _used = true;
            _usedFor = _sessions.GetDigiDollarSession()?.WalletId;
            await AnnounceFailedSendsAsync(Dashboard);
        }
        catch (OperationCanceledException) { /* page left */ }
        catch
        {
            // A DigiDollar failure must never break the page or DGB flows: keep the last dashboard.
        }
        finally
        {
            Scan = null;
            _refreshing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Tell the user once about each send that did not go through (FR-017), then drop it from local state.</summary>
    private async Task AnnounceFailedSendsAsync(DdDashboard? dashboard)
    {
        if (dashboard is not { NewlyFailedSends.Count: > 0 }) return;
        foreach (var failed in dashboard.NewlyFailedSends)
        {
            var to = failed.Recipient.Length <= 18 ? failed.Recipient : $"{failed.Recipient[..8]}…{failed.Recipient[^6..]}";
            _notifications.Show(_loc.T("dd.send.failed", Usd(failed.AmountCents), to), ToastType.Error, 8000);
            try { await _service.AcknowledgeFailedSendAsync(failed.Txid); }
            catch { /* it will be announced again next time rather than lost */ }
        }
    }

    /// <summary>Forget everything (wallet switched or locked).</summary>
    public void Reset()
    {
        StopPollingCore();
        _service.ResetSession();
        _loadedFor = null;
        Dashboard = null;
        Scan = null;
        _used = false;
        _usedFor = null;
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ polling (FR-010)

    /// <summary>Refresh every 15 s while a DigiDollar page is in the foreground; paused while the tab is hidden.</summary>
    public void StartPolling()
    {
        _pollOwners++;
        if (_pollCts is not null) return;
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollCts.Token);
    }

    public void StopPolling()
    {
        _pollOwners = Math.Max(0, _pollOwners - 1);
        if (_pollOwners == 0) StopPollingCore();
    }

    private void StopPollingCore()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _pollOwners = 0;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { if (await _js.InvokeAsync<bool>("domHelpers.isHidden", ct)) continue; }
                catch (JSException) { /* helper missing: just poll */ }
                await RefreshAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _app.OnChange -= OnAppChanged;
        StopPollingCore();
    }
}
