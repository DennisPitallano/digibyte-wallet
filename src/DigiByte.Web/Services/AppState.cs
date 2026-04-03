using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Global application state for the wallet PWA.
/// Persists user preferences to localStorage.
/// </summary>
public class AppState
{
    private readonly IJSRuntime _js;

    public bool IsWalletCreated { get; set; }
    public bool IsWalletUnlocked { get; set; }
    public bool IsSimpleMode { get; set; } = true;
    public string FiatCurrency { get; set; } = "USD";
    public bool IsTestnet { get; set; } = true;
    public string NetworkMode { get; set; } = "testnet";

    public event Action? OnChange;

    public AppState(IJSRuntime js)
    {
        _js = js;
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
}
