namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// The complete history could not be read: a source failed, failed partway, or no source supports a complete read. The export
/// then stops and offers Try again; it never saves a partial file (FR-006).
/// </summary>
public sealed class HistoryIncompleteException : Exception
{
    public HistoryIncompleteException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>How far a complete history read is: addresses (or DigiDollar address groups) done of the total.</summary>
public readonly record struct HistoryExportProgress(int Done, int Total);
