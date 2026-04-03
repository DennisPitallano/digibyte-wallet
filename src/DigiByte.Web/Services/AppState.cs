namespace DigiByte.Web.Services;

/// <summary>
/// Global application state for the wallet PWA.
/// </summary>
public class AppState
{
    public bool IsWalletCreated { get; set; }
    public bool IsWalletUnlocked { get; set; }
    public bool IsSimpleMode { get; set; } = true;
    public string FiatCurrency { get; set; } = "USD";
    public bool IsTestnet { get; set; } = true; // Default to testnet for development

    public event Action? OnChange;

    public void NotifyStateChanged() => OnChange?.Invoke();
}
