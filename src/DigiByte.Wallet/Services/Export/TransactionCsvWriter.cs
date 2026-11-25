using System.Text;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// Writes the export file (contracts/csv-file-format.md): a UTF-8 byte-order mark, the 15-column header on line 1, one row per
/// transaction newest first, CRLF line endings, no preamble and no totals. The same columns in the same order for DGB and DD,
/// so the two files can be pasted together (FR-014).
/// </summary>
public static class TransactionCsvWriter
{
    /// <summary>Decimals of the Price column: enough for a DGB price of a fraction of a cent.</summary>
    public const int PriceDecimals = 8;

    /// <summary>The whole file text, starting with the byte-order mark. Refuses zero rows: there is never a header-only file (FR-017).</summary>
    public static string Write(IReadOnlyList<ExportRow> rows, CsvFormat format, CsvWords words, string fiatCurrency, PriceBasis basis)
    {
        if (rows.Count == 0)
            throw new ArgumentException("There is nothing to export; a header-only file is never written.", nameof(rows));

        var fiatDecimals = FiatCurrencies.DecimalsFor(fiatCurrency);
        var sb = new StringBuilder("﻿");
        sb.Append(format.Line(
        [
            format.Text(words.Date), format.Text(words.TimeZone), format.Text(words.Asset), format.Text(words.Type),
            format.Text(words.Amount), format.Text(words.Fee),
            format.Text(words.PriceHeader(fiatCurrency, basis)), format.Text(words.ValueHeader(fiatCurrency, basis)),
            format.Text(words.Status), format.Text(words.Confirmations), format.Text(words.Name), format.Text(words.Counterparty),
            format.Text(words.WalletAddress), format.Text(words.Note), format.Text(words.TransactionId),
        ])).Append("\r\n");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.OrderByDescending(r => r.TimestampUtc))
        {
            if (!seen.Add(row.TxId)) continue;
            sb.Append(format.Line(
            [
                format.Text(row.LocalTime),
                format.Text(row.UtcOffset),
                format.Text(row.Asset == ExportAsset.Dd ? "DD" : "DGB"),
                format.Text(words.Kinds[row.Kind]),
                format.Number(row.Amount, row.AmountDecimals),
                format.Number(row.FeeDgb, 8),
                format.Number(row.Price, PriceDecimals),
                format.Number(row.Value, fiatDecimals),
                format.Text(words.Statuses[row.Status]),
                format.Integer(row.Confirmations),
                format.Text(row.Name),
                format.Text(row.CounterpartyAddress),
                format.Text(row.WalletAddress),
                format.Text(row.Note),
                format.Text(row.TxId),
            ])).Append("\r\n");
        }
        return sb.ToString();
    }
}
