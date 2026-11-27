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
    private readonly AppState _app;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private IReadOnlyList<PaymentRequest> _last = [];
    private bool _firstCheckSinceStart = true;
    private bool _wasUnlocked;
    private readonly HashSet<string> _pendingAddresses = new(StringComparer.Ordinal);

    private Timer? _timer;
    private string? _walletId;
    private string? _focusId;
    private DateTime _lastFocusCheck = DateTime.MinValue;
    private DateTime _lastSweep = DateTime.MinValue;
    private TimeSpan _backoff = TimeSpan.Zero;
    private DateTime _retryAfter = DateTime.MinValue;

    public RequestWatcher(PaymentRequestService requests, WalletService wallet, IBlockchainService chain, IJSRuntime js, AppState app)
    {
        _requests = requests;
        _wallet = wallet;
        _chain = chain;
        _js = js;
        _app = app;
        _wallet.TransactionsChanged += OnTransactionsChanged;
        _requests.Changed += OnRequestsSaved;
    }

    // Any saved change (a check, an open, a new request) keeps the paid-unopened list and its readers in step.
    private async void OnRequestsSaved(string walletId)
    {
        if (walletId != _walletId) return;
        try
        {
            _last = await _requests.GetAllAsync(walletId);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Requests] Could not reload requests: {ex.GetType().Name}");
        }
    }

    /// <summary>Statuses or the watched wallet changed: lists, the tab count and the lock screen re-render.</summary>
    public event Action? Changed;

    /// <summary>The wallet whose requests are watched; null until one is known.</summary>
    public string? WalletId => _walletId;

    /// <summary>
    /// The money seen at the request on screen first reached the amount asked, unconfirmed (FR-010): show
    /// "Payment arriving", without vibrating or notifying.
    /// </summary>
    public event Action<PaymentRequest>? PaymentArriving;

    /// <summary>
    /// A request was found paid and is to be announced the way given (spec 036, contracts/request-watcher.md). Raised
    /// once per request for the unlocked ways, after <see cref="PaymentRequest.PaidAnnouncedAt"/> is saved; for
    /// <see cref="RequestAnnouncement.LockedNotification"/>, once, after <see cref="PaymentRequest.LockNotifiedAt"/>
    /// is saved. Never raised for the lock-screen notice, which reads <see cref="PaidUnopened"/>.
    /// </summary>
    public event Action<PaymentRequest, RequestAnnouncement>? RequestPaid;

    /// <summary>The first check after an app start, unlock or wallet switch found this many requests paid meanwhile (FR-014a).</summary>
    public event Action<int>? PaidWhileAway;

    /// <summary>
    /// Requests paid or overpaid that the person has not opened since, announced or not: the Request tab count and the
    /// lock-screen notice (FR-014a, FR-014b).
    /// </summary>
    public IReadOnlyList<PaymentRequest> PaidUnopened => _last
        .Where(r => r.Tracked && r.Status is PaymentRequestStatus.Paid or PaymentRequestStatus.Overpaid && !r.OpenedSincePaid)
        .ToList();

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
            var unlocked = _app.IsWalletUnlocked;
            if (unlocked && !_wasUnlocked)
            {
                // Just unlocked: the next check gives the "paid since you were last here" summary (FR-014a).
                _firstCheckSinceStart = true;
                _lastSweep = DateTime.MinValue;
            }
            _wasUnlocked = unlocked;
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
        _firstCheckSinceStart = true;
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
        if (await CheckAsync(walletId, [request], sweep: false)) Changed?.Invoke();
    }

    private async Task SweepAsync()
    {
        if (_walletId is not { } walletId) return;
        _lastSweep = DateTime.UtcNow;
        var now = DateTime.UtcNow;
        var all = await _requests.GetAllAsync(walletId);
        var unopenedBefore = PaidUnopened.Count;
        _last = all;
        // Readers of the paid-unopened list (the tab count, the lock screen) learn of it even when no status changes,
        // as on the first sweep after the app loads.
        if (PaidUnopened.Count != unopenedBefore) Changed?.Invoke();

        string[] named;
        lock (_pendingAddresses)
        {
            named = [.. _pendingAddresses];
            _pendingAddresses.Clear();
        }
        // Paid but not yet announced (seen while locked, say) stays in the sweep until it is, so the summary follows an unlock.
        var toCheck = all.Where(r => r.Tracked && (IsLive(r, now) || named.Contains(r.Address) || IsPaidUnannounced(r))).ToList();

        _wallet.SetExtraHotAddresses(all.Where(IsOpen).Select(r => r.Address).ToArray());
        if (toCheck.Count == 0)
        {
            if (_app.IsWalletUnlocked) _firstCheckSinceStart = false;
            return;
        }
        if (await CheckAsync(walletId, toCheck, sweep: true)) Changed?.Invoke();
    }

    private static bool IsPaidUnannounced(PaymentRequest r) =>
        r.Status is PaymentRequestStatus.Paid or PaymentRequestStatus.Overpaid && r.PaidAnnouncedAt is null;

    /// <summary>
    /// Checks the requests and stores what it finds. A failed lookup keeps the stored status and leaves
    /// <see cref="PaymentRequest.LastCheckedAt"/> alone, so the screen shows it as not up to date (SC-007).
    /// </summary>
    private async Task<bool> CheckAsync(string walletId, IReadOnlyList<PaymentRequest> requests, bool sweep)
    {
        await _checking.WaitAsync();
        var announce = new List<(PaymentRequest Request, RequestAnnouncement Way)>();
        var arriving = new List<PaymentRequest>();
        var away = 0;
        var completed = false;
        var view = new RequestViewState(false, !await IsHiddenAsync(), !_app.IsWalletUnlocked);
        var first = _firstCheckSinceStart;
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

                var now = DateTime.UtcNow;
                var check = RequestStatusCalculator.Compute(request, txs, now);
                if (check.Status != request.Status || !SamePayments(check.Payments, request.Payments)) changed = true;

                // Decide on the request as it now stands; the rule is the only place that routes (T025).
                var computed = Copy(request, check);
                var way = RequestAnnouncementRule.Decide(computed, view with { RequestOnScreen = request.Id == _focusId }, first);
                if (way != RequestAnnouncement.None)
                {
                    // Another tab may have announced it already: re-read storage first (spec 036 C2).
                    var fresh = await _requests.ReadFreshAsync(walletId, request.Id);
                    if ((RequestAnnouncementRule.MarksAnnounced(way) && fresh?.PaidAnnouncedAt is not null)
                        || (way == RequestAnnouncement.LockedNotification && fresh?.LockNotifiedAt is not null))
                        way = RequestAnnouncement.None;
                }

                await _requests.ApplyAsync(walletId, request.Id, new RequestUpdate(check.Status, check.Payments, now,
                    PaidAnnouncedAt: RequestAnnouncementRule.MarksAnnounced(way) ? now : null,
                    LockNotifiedAt: way == RequestAnnouncement.LockedNotification ? now : null));

                if (check.BecameArriving && request.Id == _focusId) arriving.Add(computed);
                if (way == RequestAnnouncement.AwaySummary) away++;
                else if (way is not (RequestAnnouncement.None or RequestAnnouncement.LockNotice)) announce.Add((computed, way));
            }
            completed = true;
            if (sweep) _last = await _requests.GetAllAsync(walletId);
            return changed || announce.Count > 0 || away > 0;
        }
        finally
        {
            // The summary is due once per start, unlock or switch, and only a check made while unlocked uses it up.
            if (completed && first && !view.Locked) _firstCheckSinceStart = false;
            _checking.Release();
            foreach (var r in arriving) Raise(() => PaymentArriving?.Invoke(r));
            foreach (var (r, way) in announce) Raise(() => RequestPaid?.Invoke(r, way));
            if (away > 0) Raise(() => PaidWhileAway?.Invoke(away));
        }
    }

    private static void Raise(Action raise)
    {
        try { raise(); }
        catch (Exception ex) { Console.WriteLine($"[Requests] A request event handler failed: {ex.GetType().Name}"); }
    }

    // The request with this check's result, for deciding and for handlers; the stored copy is updated by ApplyAsync.
    private static PaymentRequest Copy(PaymentRequest r, RequestCheck check) => new()
    {
        Id = r.Id,
        Address = r.Address,
        AddressIndex = r.AddressIndex,
        Tracked = r.Tracked,
        AmountDgb = r.AmountDgb,
        FiatAmount = r.FiatAmount,
        FiatCurrency = r.FiatCurrency,
        FiatPrice = r.FiatPrice,
        Label = r.Label,
        Message = r.Message,
        CreatedAt = r.CreatedAt,
        ExpiresAt = r.ExpiresAt,
        CancelledAt = r.CancelledAt,
        RenewedFromId = r.RenewedFromId,
        Payments = check.Payments.ToList(),
        Status = check.Status,
        PaidAnnouncedAt = r.PaidAnnouncedAt,
        LockNotifiedAt = r.LockNotifiedAt,
        OpenedSincePaid = r.OpenedSincePaid,
        LastCheckedAt = r.LastCheckedAt,
    };

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
        _requests.Changed -= OnRequestsSaved;
        _timer?.Dispose();
        _timer = null;
        return ValueTask.CompletedTask;
    }
}
