using System.Globalization;
using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// Turns the wallet's transactions into file rows (spec 031, research R6). Pure: the caller passes the time zone and the
/// name lookups, so the same rules apply in tests and in the app.
/// </summary>
public static class ExportRowMapper
{
    private const decimal SatoshisPerDgb = 100_000_000m;

    /// <summary>A DGB transaction from Activity's history.</summary>
    /// <param name="nameOf">The contact or own-wallet name for an address, or null.</param>
    /// <param name="walletLabelOf">The label the person gave one of their own addresses, or null.</param>
    public static ExportRow FromRecord(TransactionRecord record, TimeZoneInfo zone,
        Func<string, string?>? nameOf = null, Func<string, string?>? walletLabelOf = null)
    {
        var sent = record.Direction == TransactionDirection.Sent;
        var amount = record.AmountSatoshis / SatoshisPerDgb;
        var kind = record.DigiDollar is { } dd ? KindOf(dd.Type)
            : !sent ? ExportKind.Received
            : record.AmountSatoshis == 0 ? ExportKind.OwnTransfer
            : ExportKind.Sent;
        var (local, offset, date) = LocalTime(record.Timestamp, zone);

        return new ExportRow
        {
            TxId = record.TxId,
            TimestampUtc = AsUtc(record.Timestamp),
            LocalTime = local,
            UtcOffset = offset,
            LocalDate = date,
            Asset = ExportAsset.Dgb,
            Kind = kind,
            Amount = sent ? -amount : amount,
            FeeDgb = sent ? -(record.FeeSatoshis / SatoshisPerDgb) : null,
            Status = record.Confirmations > 0 ? ExportStatus.Confirmed : ExportStatus.Unconfirmed,
            Confirmations = record.Confirmations,
            Name = NameFor(record.CounterpartyAddress, nameOf) ?? NullIfEmpty(record.CounterpartyName),
            CounterpartyAddress = NullIfEmpty(record.CounterpartyAddress),
            WalletAddress = WalletAddressText(record.WalletAddresses.FirstOrDefault(), walletLabelOf),
            Note = NullIfEmpty(record.Memo),
        };
    }

    /// <summary>A DigiDollar transaction from the DigiDollar screen's history. Amounts are in DD; the fee is in DGB.</summary>
    public static ExportRow FromDd(DigiDollarTransaction tx, TimeZoneInfo zone, Func<string, string?>? nameOf = null)
    {
        var failed = tx.Status == DdTxStatus.Failed;
        var dd = tx.AmountCents / 100m;
        var (kind, amount) = tx.Direction switch
        {
            DdDirection.Received => (ExportKind.Received, dd),
            DdDirection.SelfTransfer => (ExportKind.OwnTransfer, 0m),
            _ => (ExportKind.Sent, -dd),
        };
        // A failed send moved nothing: listed so it can be matched, never counted (spec edge cases).
        if (failed) amount = 0m;
        var (local, offset, date) = LocalTime(tx.Timestamp.UtcDateTime, zone);

        return new ExportRow
        {
            TxId = tx.Txid,
            TimestampUtc = tx.Timestamp.UtcDateTime,
            LocalTime = local,
            UtcOffset = offset,
            LocalDate = date,
            Asset = ExportAsset.Dd,
            Kind = kind,
            Amount = amount,
            FeeDgb = tx.Direction != DdDirection.Received && tx.FeeSats is { } fee && !failed ? -(fee / SatoshisPerDgb) : null,
            Status = failed ? ExportStatus.Failed : tx.Confirmations > 0 ? ExportStatus.Confirmed : ExportStatus.Unconfirmed,
            Confirmations = tx.Confirmations,
            Name = NameFor(tx.CounterpartyAddress, nameOf),
            CounterpartyAddress = NullIfEmpty(tx.CounterpartyAddress),
        };
    }

    /// <summary>
    /// The local date and time (<c>yyyy-MM-dd HH:mm:ss</c>), its UTC offset (<c>UTC±hh:mm</c>) and the local calendar day.
    /// The offset is the one in force at that instant, so rows either side of a daylight-saving change differ.
    /// </summary>
    public static (string Local, string Offset, DateOnly Date) LocalTime(DateTime timestamp, TimeZoneInfo zone)
    {
        var utc = AsUtc(timestamp);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var offset = zone.GetUtcOffset(utc);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return (local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            $"UTC{sign}{abs.Hours:00}:{abs.Minutes:00}",
            DateOnly.FromDateTime(local));
    }

    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),   // explorer and tracker timestamps are UTC
    };

    private static ExportKind KindOf(DigiDollarTxType type) => type switch
    {
        DigiDollarTxType.Mint => ExportKind.DigiDollarMinted,
        DigiDollarTxType.Redeem => ExportKind.DigiDollarRedeemed,
        _ => ExportKind.DigiDollarTransfer,
    };

    private static string? NameFor(string? address, Func<string, string?>? nameOf) =>
        string.IsNullOrEmpty(address) || nameOf is null ? null : NullIfEmpty(nameOf(address));

    /// <summary>The wallet's own address, with the label the person gave it when there is one: <c>Shop till (dgb1q…)</c>.</summary>
    private static string? WalletAddressText(string? address, Func<string, string?>? labelOf)
    {
        if (string.IsNullOrEmpty(address)) return null;
        var label = labelOf is null ? null : NullIfEmpty(labelOf(address));
        return label is null ? address : $"{label} ({address})";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
