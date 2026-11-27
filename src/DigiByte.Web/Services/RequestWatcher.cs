using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Keeps payment requests' statuses up to date for the whole app, not one page (spec 036, research R3,
/// contracts/request-watcher.md). It checks each request by its stored address through the explorer the wallet already
/// uses, and never calls anything that needs the wallet unlocked, so it keeps working while the wallet is locked or in
/// the background (FR-014b). Only the address is sent; never a label, message or amount (FR-030).
/// </summary>
public sealed class RequestWatcher : IAsyncDisposable
{
    /// <summary>How often the request on screen is checked (SC-002).</summary>
    public static readonly TimeSpan FocusInterval = TimeSpan.FromSeconds(3);
    /// <summary>How often the other live requests are checked while the wallet is visible, and while it is hidden.</summary>
    public static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(60);
    /// <summary>Expired and cancelled requests stay live this long, so late money shows without a refresh elsewhere.</summary>
    public static readonly TimeSpan RecentlyClosed = TimeSpan.FromHours(24);
    /// <summary>A status older than this, while online, reads "Not up to date" (SC-007).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private const string WatchedWalletKey = "dgb-request-watch-wallet";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    private readonly PaymentRequestService _requests;
    private readonly WalletService _wallet;
    private readonly IBlockchainService _chain;
    private readonly IJSRuntime _js;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly HashSet<string> _pendingAddresses = new(StringComparer.Ordinal);

    private Timer? _timer;
    private string? _walletId;
    private string? _focusId;
    private DateTime _lastFocusCheck = DateTime.MinValue;
    private DateTime _lastSweep = DateTime.MinValue;
    private TimeSpan _backoff = TimeSpan.Zero;
    private DateTime _retryAfter = DateTime.MinValue;

    public RequestWatcher(PaymentRequestService requests, WalletService wallet, IBlockchainService chain, IJSRuntime js)
    {
        _requests = requests;
        _wallet = wallet;
        _chain = chain;
        _js = js;
        _wallet.TransactionsChanged += OnTransactionsChanged;
    }

    /// <summary>Statuses or the watched wallet changed: lists, the tab count and the lock screen re-render.</summary>
    public event Action? Changed;

    /// <summary>The wallet whose requests are watched; null until one is known.</summary>
    public string? WalletId => _walletId;

    /// <summary>
    /// Starts watching, at app load: for the wallet last watched on this device until a wallet is unlocked, so a request
    /// paid while the app is still locked is noticed too.
    /// </summary>
    public async Task StartAsync()
    {
        if (_timer is not null) return;
        try { _walletId = await _js.InvokeAsync<string?>("localStorage.getItem", WatchedWalletKey); }
        catch { /* storage unavailable: wait for an unlock */ }
        _timer = new Timer(_ => _ = OnTickAsync(), null, TimeSpan.Zero, Tick);
    }

    /// <summary>A page shows this request (or none): check it now, then every <see cref="FocusInterval"/>.</summary>
    public void Focus(string? requestId)
    {
        _focusId = requestId;
        _lastFocusCheck = DateTime.MinValue;
        if (requestId is not null) _ = CheckFocusAsync();
    }

    /// <summary>The person asked for fresh statuses (pull to refresh): check every live request now.</summary>
    public Task CheckNowAsync()
    {
        _retryAfter = DateTime.MinValue;
        _lastSweep = DateTime.MinValue;
        return SweepAsync();
    }

    private async Task OnTickAsync()
    {
        try
        {
            SyncWallet();
            if (_walletId is null) return;
            var now = DateTime.UtcNow;
            if (now < _retryAfter) return;

            if (_focusId is not null && now - _lastFocusCheck >= FocusInterval)
                await CheckFocusAsync();

            var hidden = await IsHiddenAsync();
            if (now - _lastSweep >= (hidden ? HiddenInterval : VisibleInterval) || HasPendingAddresses())
                await SweepAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Requests] Watch tick failed: {ex.GetType().Name}");
        }
    }

    // The active wallet once unlocked; switching wallets moves the watch to it, and it is remembered for next app load.
    private void SyncWallet()
    {
        var active = _wallet.ActiveWallet?.Id;
        if (active is null || active == _walletId) return;
        _walletId = active;
        _lastSweep = DateTime.MinValue;
        _ = RememberWalletAsync(active);
        Changed?.Invoke();
    }

    private async Task RememberWalletAsync(string walletId)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", WatchedWalletKey, walletId); }
        catch { /* not remembered: the next app load waits for an unlock */ }
    }

    private async Task CheckFocusAsync()
    {
        if (_walletId is not { } walletId || _focusId is not { } id) return;
        _lastFocusCheck = DateTime.UtcNow;
        var request = (await _requests.GetAllAsync(walletId)).FirstOrDefault(r => r.Id == id);
        if (request is null) return;
        if (await CheckAsync(walletId, [request])) Changed?.Invoke();
    }

    private async Task SweepAsync()
    {
        if (_walletId is not { } walletId) return;
        _lastSweep = DateTime.UtcNow;
        var now = DateTime.UtcNow;
        var all = await _requests.GetAllAsync(walletId);

        string[] named;
        lock (_pendingAddresses)
        {
            named = [.. _pendingAddresses];
            _pendingAddresses.Clear();
        }
        var toCheck = all.Where(r => r.Tracked && (IsLive(r, now) || named.Contains(r.Address))).ToList();

        _wallet.SetExtraHotAddresses(all.Where(IsOpen).Select(r => r.Address).ToArray());
        if (toCheck.Count == 0) return;
        if (await CheckAsync(walletId, toCheck)) Changed?.Invoke();
    }

    /// <summary>
    /// Checks the requests and stores what it finds. A failed lookup keeps the stored status and leaves
    /// <see cref="PaymentRequest.LastCheckedAt"/> alone, so the screen shows it as not up to date (SC-007).
    /// </summary>
    private async Task<bool> CheckAsync(string walletId, IReadOnlyList<PaymentRequest> requests)
    {
        await _checking.WaitAsync();
        try
        {
            var changed = false;
            foreach (var request in requests)
            {
                List<TransactionInfo> txs;
                try
                {
                    txs = await _chain.GetAddressTransactionsAsync(request.Address);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Requests] Could not check a request: {ex.GetType().Name}");
                    _backoff = _backoff == TimeSpan.Zero ? TimeSpan.FromSeconds(5) : TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, _backoff.Ticks * 2));
                    _retryAfter = DateTime.UtcNow + _backoff;
                    return changed;
                }
                _backoff = TimeSpan.Zero;

                var check = RequestStatusCalculator.Compute(request, txs, DateTime.UtcNow);
                if (check.Status != request.Status || !SamePayments(check.Payments, request.Payments)) changed = true;
                await _requests.ApplyAsync(walletId, request.Id,
                    new RequestUpdate(check.Status, check.Payments, DateTime.UtcNow, PaidAnnouncedAt: null, LockNotifiedAt: null));
            }
            return changed;
        }
        finally { _checking.Release(); }
    }

    private static bool SamePayments(IReadOnlyList<RequestPayment> a, IReadOnlyList<RequestPayment> b) =>
        a.Count == b.Count && a.All(p => b.Any(q => q.TxId == p.TxId && q.Confirmed == p.Confirmed && q.AmountSatoshis == p.AmountSatoshis));

    /// <summary>Waiting, payment arriving or partly paid: the wallet treats these addresses as hot (T017).</summary>
    public static bool IsOpen(PaymentRequest r) =>
        r.Tracked && r.Status is PaymentRequestStatus.Waiting or PaymentRequestStatus.PaymentArriving or PaymentRequestStatus.PartlyPaid;

    /// <summary>Open, or expired or cancelled in the last 24 hours (research R3): checked on every sweep.</summary>
    public static bool IsLive(PaymentRequest r, DateTime nowUtc)
    {
        if (!r.Tracked) return false;
        if (IsOpen(r)) return true;
        if (r.CancelledAt is { } c && nowUtc - c < RecentlyClosed) return true;
        if (r.ExpiresAt is { } e && nowUtc >= e && nowUtc - e < RecentlyClosed) return true;
        // A currency request still inside its price lock can expire at any moment.
        return r.ExpiresAt is { } lockEnd && nowUtc < lockEnd && r.Status is not (PaymentRequestStatus.Paid or PaymentRequestStatus.Overpaid);
    }

    /// <summary>Whether a request's stored status is older than <see cref="StaleAfter"/>.</summary>
    public static bool IsStale(PaymentRequest r, DateTime nowUtc) =>
        r.Tracked && (r.LastCheckedAt is not { } at || nowUtc - at > StaleAfter);

    // A refresh found new or newly confirmed money at some of the wallet's addresses: check the requests paid there,
    // whatever their status, on the next tick (T016, FR-020).
    private void OnTransactionsChanged(IReadOnlyCollection<string> addresses)
    {
        lock (_pendingAddresses)
            foreach (var a in addresses) _pendingAddresses.Add(a);
    }

    private bool HasPendingAddresses()
    {
        lock (_pendingAddresses) return _pendingAddresses.Count > 0;
    }

    private async Task<bool> IsHiddenAsync()
    {
        try { return await _js.InvokeAsync<bool>("domHelpers.isHidden"); }
        catch { return false; }
    }

    public ValueTask DisposeAsync()
    {
        _wallet.TransactionsChanged -= OnTransactionsChanged;
        _timer?.Dispose();
        _timer = null;
        return ValueTask.CompletedTask;
    }
}
