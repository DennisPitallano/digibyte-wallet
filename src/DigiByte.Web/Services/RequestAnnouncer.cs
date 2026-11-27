using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Tells the person a request was paid when they are not looking at it (spec 036, FR-012 to FR-014b): a tappable toast
/// elsewhere in the wallet, a system notification when the wallet is hidden, and one "paid since you were last here"
/// message. Which of these is decided by <see cref="RequestAnnouncementRule"/> in the watcher; this only carries it out.
/// The overlay belongs to the Request page and the lock-screen notice to Unlock.
/// </summary>
public sealed class RequestAnnouncer : IDisposable
{
    /// <summary>localStorage switch: show the amount and label in notifications (FR-013). Off unless exactly "true".</summary>
    public const string DetailsKey = "dgb-request-notify-details";
    private const string AskedKey = "dgb-request-notify-asked";

    private readonly RequestWatcher _watcher;
    private readonly NotificationService _toasts;
    private readonly LocalizationService _l;
    private readonly NavigationManager _nav;
    private readonly IJSRuntime _js;
    private readonly ISecureStorage _storage;
    private bool _started;

    public RequestAnnouncer(RequestWatcher watcher, NotificationService toasts, LocalizationService l, NavigationManager nav,
        IJSRuntime js, ISecureStorage storage)
    {
        _watcher = watcher;
        _toasts = toasts;
        _l = l;
        _nav = nav;
        _js = js;
        _storage = storage;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _watcher.RequestPaid += OnRequestPaid;
        _watcher.PaidWhileAway += OnPaidWhileAway;
    }

    /// <summary>
    /// After the person makes a request: ask once for permission to show notifications (FR-013), never on page load.
    /// </summary>
    public async Task AfterRequestCreatedAsync()
    {
        try
        {
            if (await _js.InvokeAsync<string?>("localStorage.getItem", AskedKey) == "1") return;
            await _js.InvokeVoidAsync("localStorage.setItem", AskedKey, "1");
            await _js.InvokeAsync<string>("requestNotify.ask");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Requests] Could not ask for notifications: {ex.GetType().Name}");
        }
    }

    private void OnRequestPaid(PaymentRequest request, RequestAnnouncement way) => _ = AnnounceAsync(request, way);

    private async Task AnnounceAsync(PaymentRequest request, RequestAnnouncement way)
    {
        try
        {
            var hidden = await HideAmountsAsync();
            var amount = hidden ? "••••" : Dgb(Confirmed(request));
            var label = request.Label ?? _l.T("payments.default_label");
            switch (way)
            {
                case RequestAnnouncement.Toast:
                    _toasts.Show(_l.T("request.paid_toast", label, amount), ToastType.Success, 8000,
                        onTap: () => _nav.NavigateTo($"payments?request={Uri.EscapeDataString(request.Id)}"));
                    break;
                case RequestAnnouncement.SystemNotification:
                case RequestAnnouncement.LockedNotification:
                    // Never the address. The label and amount only when the person turned that on, and not in hide-amounts mode.
                    var details = !hidden && await ShowDetailsAsync();
                    var body = details ? _l.T("request.notify.body_details", label, amount) : _l.T("request.notify.body_plain");
                    await _js.InvokeAsync<bool>("requestNotify.show", _l.T("request.notify.title"), body);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Requests] Could not announce a paid request: {ex.GetType().Name}");
        }
    }

    private void OnPaidWhileAway(int count)
    {
        var text = count == 1 ? _l.T("request.paid_away.one") : _l.T("request.paid_away.other", count);
        _toasts.Show(text, ToastType.Success, 0, onTap: () => _nav.NavigateTo("payments"));
    }

    private async Task<bool> ShowDetailsAsync()
    {
        try { return await _js.InvokeAsync<string?>("localStorage.getItem", DetailsKey) == "true"; }
        catch { return false; }
    }

    private async Task<bool> HideAmountsAsync()
    {
        try { return await _storage.GetAsync("pref:hideBalance") == "1"; }
        catch { return false; }
    }

    private static long Confirmed(PaymentRequest r) => r.Payments.Where(p => p.Confirmed).Sum(p => p.AmountSatoshis);

    private static string Dgb(long satoshis) =>
        (satoshis / 100_000_000m).ToString("#,0.########", System.Globalization.CultureInfo.CurrentCulture);

    public void Dispose()
    {
        _watcher.RequestPaid -= OnRequestPaid;
        _watcher.PaidWhileAway -= OnPaidWhileAway;
    }
}
