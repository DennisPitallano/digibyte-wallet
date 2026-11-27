using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
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
    private DotNetObjectReference<AppState>? _dotNetRef;
    private readonly Func<IWalletService>? _wallet;

    /// <summary>Auto-lock after this duration of inactivity.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Immediately lock when the tab/app becomes hidden (minimized, switched away).</summary>
    public bool LockOnHidden { get; set; }

    public bool IsWalletCreated { get; set; }
    public bool IsWalletUnlocked { get; set; }
    public bool IsSimpleMode { get; set; } = true;
    public string FiatCurrency { get; set; } = "USD";
    public List<WalletInfo> Wallets { get; set; } = [];
    public string? ActiveWalletName { get; set; }
    public string FiatSymbol => DigiByte.Wallet.Models.FiatCurrencies.SymbolFor(FiatCurrency);

    /// <summary>Digits after the decimal point for fiat amounts of one unit or more (0 for yen, rupiah and similar).</summary>
    public int FiatDecimals => DigiByte.Wallet.Models.FiatCurrencies.DecimalsFor(FiatCurrency);
    public bool IsTestnet { get; set; }
    public string NetworkMode { get; set; } = "mainnet";

    /// <summary>Route changes since the app loaded. Above zero means history.back() stays inside the app.</summary>
    public int InAppNavigations { get; set; }

    public event Action? OnChange;
    public event Action? OnAutoLocked;

    public AppState(IJSRuntime js, IConfiguration config, Func<IWalletService>? wallet = null)
    {
        _js = js;
        _wallet = wallet;
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

            var lockHidden = await _js.InvokeAsync<string?>("localStorage.getItem", "dgb-lock-on-hidden");
            LockOnHidden = lockHidden == "true";

            // Set up visibility change listener
            _dotNetRef = DotNetObjectReference.Create(this);
            await _js.InvokeVoidAsync("visibilityLock.init", _dotNetRef, LockOnHidden);
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
        _lockTimer = new Timer(_ => Lock(), null, LockTimeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stop the lock timer (e.g. when wallet is manually locked).</summary>
    public void StopLockTimer()
    {
        _lockTimer?.Dispose();
        _lockTimer = null;
    }

    /// <summary>
    /// Locks the wallet because the user asked to (the snap on the balance card). Does exactly what the auto-lock does:
    /// the app goes to the unlock screen and the PIN or passphrase is needed again.
    /// </summary>
    public void LockNow()
    {
        if (!IsWalletUnlocked) return;
        Lock();
    }

    /// <summary>Called from JS when the tab/app becomes hidden.</summary>
    [JSInvokable]
    public void OnTabHidden()
    {
        if (!IsWalletUnlocked || !LockOnHidden) return;
        Lock();
    }

    /// <summary>
    /// The one lock path (timeout, lock when hidden, Lock now). The wallet's decrypted sessions, with their keys, are dropped
    /// first, so nothing that reacts to <see cref="OnAutoLocked"/> can still find them in memory. Whatever must keep running
    /// while locked (the payment-request watcher) works from stored addresses only.
    /// </summary>
    private void Lock()
    {
        IsWalletUnlocked = false;
        StopLockTimer();
        LockWallet();
        OnAutoLocked?.Invoke();
        NotifyStateChanged();
    }

    private void LockWallet()
    {
        if (_wallet is null) return;
        try
        {
            var locking = _wallet().LockWalletAsync();
            // WalletService clears its sessions synchronously; anything slower is still observed, so a failure is logged.
            if (!locking.IsCompleted)
                _ = locking.ContinueWith(t => Console.WriteLine($"[Lock] Locking the wallet failed: {t.Exception?.GetBaseException().GetType().Name}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            else if (locking.IsFaulted)
                Console.WriteLine($"[Lock] Locking the wallet failed: {locking.Exception?.GetBaseException().GetType().Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lock] Locking the wallet failed: {ex.GetType().Name}");
        }
    }

    /// <summary>Update the lock-on-hidden setting and sync to JS.</summary>
    public async Task SetLockOnHiddenAsync(bool value)
    {
        LockOnHidden = value;
        await SavePreferenceAsync("dgb-lock-on-hidden", value ? "true" : "false");
        try { await _js.InvokeVoidAsync("visibilityLock.setLockOnHidden", value); }
        catch { }
    }

    public void Dispose()
    {
        _lockTimer?.Dispose();
        _dotNetRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
