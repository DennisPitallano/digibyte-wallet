namespace DigiByte.Wallet.Services;

/// <summary>
/// Developer switch for change-only history sync (spec 023, FR-010a). On by default. When off, every refresh reads every
/// watched address's full history, as before. Read on each refresh, so a change takes effect on the next one.
/// </summary>
public sealed class HistorySyncOptions
{
    /// <summary>Storage key the web app keeps the switch under (per device).</summary>
    public const string StorageKey = "dev:history-change-only";

    public bool ChangeOnly { get; set; } = true;
}
