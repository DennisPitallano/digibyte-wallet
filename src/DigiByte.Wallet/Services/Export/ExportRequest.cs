namespace DigiByte.Wallet.Services.Export;

/// <summary>What to export (FR-002).</summary>
public enum ExportScope
{
    Everything,

    /// <summary>What Activity's list shows: its filter, search and wallet address (DGB only).</summary>
    CurrentView,

    DateRange,
}

/// <summary>The date-range choices. <see cref="Custom"/> uses <see cref="ExportRequest.From"/> and <see cref="ExportRequest.To"/>.</summary>
public enum ExportPreset
{
    ThisMonth,
    LastMonth,
    ThisYear,
    Custom,
}

/// <summary>What the person chose in the export sheet (data-model.md "ExportRequest"). Fiat values are always included.</summary>
public sealed record ExportRequest
{
    public required ExportAsset Asset { get; init; }
    public ExportScope Scope { get; init; } = ExportScope.Everything;
    public ExportPreset Preset { get; init; } = ExportPreset.ThisMonth;

    /// <summary>Custom range, local dates, both days included.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Activity's current view, for <see cref="ExportScope.CurrentView"/>.</summary>
    public ActivityView? View { get; init; }

    /// <summary>The local date range for <see cref="ExportScope.DateRange"/>, or null for any other scope or an invalid custom range.</summary>
    public (DateOnly From, DateOnly To)? ResolveRange(DateOnly today)
    {
        if (Scope != ExportScope.DateRange) return null;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return Preset switch
        {
            ExportPreset.ThisMonth => (monthStart, today),
            ExportPreset.LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            ExportPreset.ThisYear => (new DateOnly(today.Year, 1, 1), today),
            _ => From is { } f && To is { } t && f <= t && t <= today ? (f, t) : null,
        };
    }

    /// <summary>A custom range needs From ≤ To and To not after today; a current-view export needs the view.</summary>
    public bool IsValid(DateOnly today) => Scope switch
    {
        ExportScope.DateRange => ResolveRange(today) is not null,
        ExportScope.CurrentView => View is not null,
        _ => true,
    };

    /// <summary>
    /// Whether a row belongs in the file. A date range compares the row's <b>local</b> date, both days included. The current view
    /// applies Activity's own rules to <paramref name="record"/> (the DGB transaction the row came from).
    /// </summary>
    public bool Matches(ExportRow row, Models.TransactionRecord? record, DateOnly today) => Scope switch
    {
        ExportScope.DateRange => ResolveRange(today) is { } r && row.LocalDate >= r.From && row.LocalDate <= r.To,
        ExportScope.CurrentView => View is not null && record is not null && ActivityListFilter.Matches(record, View),
        _ => true,
    };
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
