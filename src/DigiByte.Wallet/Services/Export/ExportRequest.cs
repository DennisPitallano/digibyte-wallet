namespace DigiByte.Wallet.Services.Export;

/// <summary>What to export (FR-002).</summary>
public enum ExportScope
{
    Everything,
}

/// <summary>What the person chose in the export sheet (data-model.md "ExportRequest"). Fiat values are always included.</summary>
public sealed record ExportRequest
{
    public required ExportAsset Asset { get; init; }
    public ExportScope Scope { get; init; } = ExportScope.Everything;
}

/// <summary>How an export ended (data-model.md "ExportResult"). Saving is the web app's job, after <see cref="Ready"/>.</summary>
public enum ExportOutcome
{
    /// <summary>The file content is built and ready to save or share.</summary>
    Ready,
    Cancelled,
    NothingToExport,
    HistoryUnavailable,
    Locked,
}

public sealed record ExportResult
{
    public required ExportOutcome Outcome { get; init; }
    public int RowCount { get; init; }

    /// <summary>Rows with an empty Value because no price was available for their day (FR-007: the person is told).</summary>
    public int RowsWithoutPrice { get; init; }

    public string? FileName { get; init; }

    /// <summary>The whole file, starting with the byte-order mark. Null unless <see cref="Outcome"/> is <see cref="ExportOutcome.Ready"/>.</summary>
    public string? Content { get; init; }

    public static ExportResult Of(ExportOutcome outcome) => new() { Outcome = outcome };
}

/// <summary>What the export needs from the device and the app, passed in so the service stays testable.</summary>
public sealed record ExportEnvironment
{
    /// <summary>The device's decimal mark, from the browser: decides the separator style (research R4).</summary>
    public required string DecimalMark { get; init; }

    /// <summary>The app language: header and Type/Status words.</summary>
    public required string Language { get; init; }

    public required TimeZoneInfo TimeZone { get; init; }
    public required DateTimeOffset Now { get; init; }
    public required string FiatCurrency { get; init; }

    /// <summary>The contact or own-wallet name for an address (FR-015), or null.</summary>
    public Func<string, string?>? NameOf { get; init; }

    /// <summary>The label the person gave one of their own addresses, or null.</summary>
    public Func<string, string?>? WalletLabelOf { get; init; }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZone).DateTime);
}
