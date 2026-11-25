namespace DigiByte.Wallet.Services.Export;

/// <summary>Which currency a file holds: one currency per file (FR-014).</summary>
public enum ExportAsset
{
    Dgb,
    Dd,
}

/// <summary>The Type column (contracts/csv-file-format.md).</summary>
public enum ExportKind
{
    Received,
    Sent,
    OwnTransfer,
    DigiDollarTransfer,
    DigiDollarMinted,
    DigiDollarRedeemed,
}

/// <summary>The Status column. Failed is for DigiDollar sends only.</summary>
public enum ExportStatus
{
    Confirmed,
    Unconfirmed,
    Failed,
}

/// <summary>
/// One transaction as a bookkeeper needs it (data-model.md "ExportRow"). Every row has a <see cref="TxId"/>; within a file the
/// writer keeps each <see cref="TxId"/> once. Amounts are signed (FR-005): positive received, negative sent, 0 for an own transfer
/// or a failed send; the fee is negative, in DGB, and only on sent and own-transfer rows.
/// </summary>
public sealed record ExportRow
{
    public required string TxId { get; init; }

    /// <summary>When it happened, in UTC: the writer sorts on this (newest first).</summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary><c>yyyy-MM-dd HH:mm:ss</c> in the device's time zone.</summary>
    public required string LocalTime { get; init; }

    /// <summary><c>UTC+hh:mm</c> or <c>UTC-hh:mm</c> for this row's instant (daylight-saving aware).</summary>
    public required string UtcOffset { get; init; }

    /// <summary>The local calendar day: date ranges and daily prices use it (clarification Q4).</summary>
    public DateOnly LocalDate { get; init; }

    public required ExportAsset Asset { get; init; }
    public required ExportKind Kind { get; init; }
    public required decimal Amount { get; init; }
    public decimal? FeeDgb { get; init; }
    public decimal? Price { get; init; }
    public decimal? Value { get; init; }
    public required ExportStatus Status { get; init; }
    public required int Confirmations { get; init; }
    public string? Name { get; init; }
    public string? CounterpartyAddress { get; init; }
    public string? WalletAddress { get; init; }
    public string? Note { get; init; }

    /// <summary>Decimals of <see cref="Amount"/>: 8 for DGB, 2 for DD.</summary>
    public int AmountDecimals => Asset == ExportAsset.Dd ? 2 : 8;
}
