using DigiByte.Wallet.Services;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Owns Baku's device-local preferences and decides which single message is shown at a time.
/// Persists only a presence mode and dismissed tip ids to localStorage (no wallet data, nothing secret).
/// </summary>
public class BakuService
{
    private const string ModeKey = "dgb-baku-mode";
    private const string DismissedKey = "dgb-baku-dismissed";

    private readonly IJSRuntime _js;
    private readonly Dictionary<string, long> _requested = new();
    private readonly HashSet<string> _dismissed = new();
    private long _sequence;
    private TaskCompletionSource<bool>? _confirmation;

    public BakuService(IJSRuntime js) => _js = js;

    public event Action? OnChanged;

    public BakuMode Mode { get; private set; } = BakuMode.Full;

    /// <summary>False when the user turned Baku off: messages render as text only, without the mascot image.</summary>
    public bool ShowsImage => Mode != BakuMode.Off;

    /// <summary>The guardian message awaiting the user's decision, if any.</summary>
    public BakuMessage? PendingConfirmation { get; private set; }

    /// <summary>The single message to show now: highest severity wins, ties go to the most recent request.</summary>
    public BakuMessage? Current =>
        _requested
            .Select(kv => (Message: BakuCatalog.Find(kv.Key), Seq: kv.Value))
            .Where(x => x.Message != null && IsVisible(x.Message))
            .OrderByDescending(x => x.Message!.Severity)
            .ThenByDescending(x => x.Seq)
            .Select(x => x.Message)
            .FirstOrDefault();

    public bool IsDismissed(string id) => _dismissed.Contains(id);

    public async Task InitializeAsync()
    {
        try
        {
            var mode = await _js.InvokeAsync<string?>("localStorage.getItem", ModeKey);
            Mode = mode switch
            {
                "guardian" => BakuMode.GuardianOnly,
                "off" => BakuMode.Off,
                _ => BakuMode.Full,
            };

            var dismissed = await _js.InvokeAsync<string?>("localStorage.getItem", DismissedKey);
            _dismissed.Clear();
            if (!string.IsNullOrWhiteSpace(dismissed))
            {
                foreach (var id in dismissed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    _dismissed.Add(id);
            }
        }
        catch
        {
            // Storage can be blocked (private window, prerender): fall back to defaults.
            Mode = BakuMode.Full;
            _dismissed.Clear();
        }
        OnChanged?.Invoke();
    }

    public async Task SetModeAsync(BakuMode mode)
    {
        Mode = mode;
        await SaveAsync(ModeKey, mode switch
        {
            BakuMode.GuardianOnly => "guardian",
            BakuMode.Off => "off",
            _ => "full",
        });
        OnChanged?.Invoke();
    }

    /// <summary>Offer a message for the current screen. Unknown ids are ignored.</summary>
    public void Request(string id)
    {
        if (BakuCatalog.Find(id) == null) return;
        _requested[id] = ++_sequence;
        OnChanged?.Invoke();
    }

    public void Clear(string id)
    {
        if (_requested.Remove(id)) OnChanged?.Invoke();
    }

    /// <summary>Permanently dismiss a message. A no-op for Warning and Critical.</summary>
    public async Task DismissAsync(string id)
    {
        var message = BakuCatalog.Find(id);
        if (message is not { Dismissible: true }) return;
        _dismissed.Add(id);
        _requested.Remove(id);
        await SaveAsync(DismissedKey, string.Join(',', _dismissed.Order()));
        OnChanged?.Invoke();
    }

    public async Task ResetTipsAsync()
    {
        _dismissed.Clear();
        await SaveAsync(DismissedKey, "");
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Show a guardian message and wait for the user. True means "I understand, continue", false means go back.
    /// Always shown, regardless of mode. An unknown id never blocks the action.
    /// </summary>
    public Task<bool> ConfirmAsync(string id)
    {
        var message = BakuCatalog.Find(id);
        if (message == null) return Task.FromResult(true);

        _confirmation?.TrySetResult(false); // a newer warning replaces an unanswered one
        _confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingConfirmation = message;
        OnChanged?.Invoke();
        return _confirmation.Task;
    }

    /// <summary>
    /// Guardian check for a send. Picks one message (a single combined one when both risks apply) and waits for the
    /// user. No risk means no dialog and the send continues. Returns false only if the user chose to go back.
    /// </summary>
    public Task<bool> ConfirmSendRiskAsync(SendRiskAssessment risk)
    {
        var id = (risk.NewAddress, risk.LargeAmount) switch
        {
            (true, true) => "send.new-and-large",
            (true, false) => "send.new-address",
            (false, true) => "send.large-amount",
            _ => null,
        };
        return id is null ? Task.FromResult(true) : ConfirmAsync(id);
    }

    public void ResolveConfirmation(bool proceed)
    {
        var pending = _confirmation;
        _confirmation = null;
        PendingConfirmation = null;
        pending?.TrySetResult(proceed);
        OnChanged?.Invoke();
    }

    private bool IsVisible(BakuMessage message)
    {
        if (message.Severity >= BakuSeverity.Warning) return true; // never filtered by mode
        if (Mode != BakuMode.Full) return false;
        return !_dismissed.Contains(message.Id);
    }

    private async Task SaveAsync(string key, string value)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { /* storage unavailable: the preference lasts for this session only */ }
    }
}
