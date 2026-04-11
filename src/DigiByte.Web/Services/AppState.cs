using DigiByte.Wallet.Models;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Global application state for the wallet PWA.
/// Persists user preferences to localStorage.
/// </summary>
public class AppState : IDisposable
{
    private readonly IJSRuntime _js;
    private readonly string _defaultNetwork;
    private Timer? _lockTimer;

    /// <summary>Auto-lock after this duration of inactivity.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsWalletCreated { get; set; }
    public bool IsWalletUnlocked { get; set; }
    public bool IsSimpleMode { get; set; } = true;
    public string FiatCurrency { get; set; } = "USD";
    public List<WalletInfo> Wallets { get; set; } = [];
    public string? ActiveWalletName { get; set; }
    public string FiatSymbol => FiatCurrency switch
    {
        "EUR" => "\u20ac",
        "GBP" => "\u00a3",
        "PHP" => "\u20b1",
        "JPY" => "\u00a5",
        _ => "$",
    };
    public bool IsTestnet { get; set; }
    public string NetworkMode { get; set; } = "mainnet";

    public event Action? OnChange;
    public event Action? OnAutoLocked;

    public AppState(IJSRuntime js, IConfiguration config)
    {
        _js = js;
        _defaultNetwork = config["DefaultNetwork"] ?? "mainnet";
        NetworkMode = _defaultNetwork;
        IsTestnet = _defaultNetwork != "mainnet";
    }

    /// <summary>
    /// Load persisted preferences from localStorage on app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var mode = await _js.InvokeAsync<string?>("localStorage.getItem", "dgb-display-mode");
            if (mode == "advanced") IsSimpleMode = false;

            var network = await _js.InvokeAsync<string?>("localStorage.getItem", "dgb-network");
            if (network != null) { NetworkMode = network; IsTestnet = network != "mainnet"; }

            var currency = await _js.InvokeAsync<string?>("localStorage.getItem", "dgb-currency");
            if (currency != null) FiatCurrency = currency;

            var lockMins = await _js.InvokeAsync<string?>("localStorage.getItem", "dgb-lock-timeout");
            if (lockMins != null && int.TryParse(lockMins, out var mins) && mins > 0)
                LockTimeout = TimeSpan.FromMinutes(mins);
        }
        catch { /* JS interop may fail during prerender */ }
    }

    public void NotifyStateChanged() => OnChange?.Invoke();

    /// <summary>
    /// Persist a setting change to localStorage.
    /// </summary>
    public async Task SavePreferenceAsync(string key, string value)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { }
    }

    /// <summary>
    /// Call on any user interaction to reset the auto-lock countdown.
    /// </summary>
    public void ResetLockTimer()
    {
        if (!IsWalletUnlocked) return;
        _lockTimer?.Dispose();
        _lockTimer = new Timer(_ =>
        {
            IsWalletUnlocked = false;
            OnAutoLocked?.Invoke();
            NotifyStateChanged();
        }, null, LockTimeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stop the lock timer (e.g. when wallet is manually locked).</summary>
    public void StopLockTimer()
    {
        _lockTimer?.Dispose();
        _lockTimer = null;
    }

    public void Dispose()
    {
        _lockTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
