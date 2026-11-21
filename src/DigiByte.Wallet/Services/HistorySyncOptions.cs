namespace DigiByte.Wallet.Services;

/// <summary>
/// Developer switches for the read path. Both on by default.
/// <list type="bullet">
/// <item><see cref="ChangeOnly"/> (spec 023, FR-010a): when off, every refresh reads every watched address's full history, as before.</item>
/// <item><see cref="Tiered"/> (spec 024, FR-013): when off, every refresh reads every address's summary and every send reads every
/// address's coins, as in 0.7.0. Tiering applies only while <see cref="ChangeOnly"/> is also on.</item>
/// </list>
/// Read on each refresh, so a change takes effect on the next one. The switches are for developers, testing and benchmarks only:
/// a saved value counts only while the developer view is on (<see cref="FromStorage"/>, FR-013a).
/// </summary>
public sealed class HistorySyncOptions
{
    /// <summary>Storage key the web app keeps the change-only switch under (per device).</summary>
    public const string StorageKey = "dev:history-change-only";

    /// <summary>Storage key the web app keeps the tiered-refresh switch under (per device).</summary>
    public const string TieredStorageKey = "dev:tiered-refresh";

    public bool ChangeOnly { get; set; } = true;

    public bool Tiered { get; set; } = true;

    /// <summary>True when tiered refresh applies: both switches on.</summary>
    public bool TieringActive => Tiered && ChangeOnly;

    /// <summary>
    /// The options at startup. With the developer view off the defaults are used whatever a device saved earlier, so a value left
    /// behind can never change a regular user's wallet (FR-013a). With it on, a saved "false" turns that switch off; a missing,
    /// "true" or unreadable value leaves it on.
    /// </summary>
    public static HistorySyncOptions FromStorage(bool devView, string? changeOnlyStored, string? tieredStored) => new()
    {
        ChangeOnly = !(devView && IsOff(changeOnlyStored)),
        Tiered = !(devView && IsOff(tieredStored)),
    };

    private static bool IsOff(string? stored) => string.Equals(stored?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
}
