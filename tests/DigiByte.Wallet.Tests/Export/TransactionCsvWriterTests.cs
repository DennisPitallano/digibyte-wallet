using System.Text;
using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests.Export;

/// <summary>The whole file: BOM, line endings, header row, order (spec 031, contracts/csv-file-format.md).</summary>
public class TransactionCsvWriterTests
{
    private static readonly CsvFormat Dot = CsvFormat.ForDecimalMark(".");

    internal static ExportRow Row(string txId, DateTime utc, decimal amount = 1m, ExportAsset asset = ExportAsset.Dgb) => new()
    {
        TxId = txId,
        TimestampUtc = utc,
        LocalTime = utc.ToString("yyyy-MM-dd HH:mm:ss"),
        UtcOffset = "UTC+00:00",
        Asset = asset,
        Kind = amount >= 0 ? ExportKind.Received : ExportKind.Sent,
        Amount = amount,
        Status = ExportStatus.Confirmed,
        Confirmations = 6,
    };

    private static string[] Lines(string content) => content.TrimStart('﻿').Split("\r\n");

    [Fact]
    public void Output_StartsWithUtf8Bom_AndUsesCrlf()
    {
        var content = TransactionCsvWriter.Write([Row("a", DateTime.UtcNow)], Dot, CsvWords.English, "USD", PriceBasis.Native);

        Assert.Equal('﻿', content[0]);
        var bytes = Encoding.UTF8.GetBytes(content);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("\r\n", content);
        Assert.DoesNotContain("\n", content.Replace("\r\n", ""));
    }

    [Fact]
    public void HeaderRow_IsLineOne_WithFifteenColumnsInContractOrder()
    {
        var content = TransactionCsvWriter.Write([Row("a", DateTime.UtcNow)], Dot, CsvWords.English, "USD", PriceBasis.Native);
        var header = Lines(content)[0];

        Assert.Equal(
            "Date,Time zone,Asset,Type,Amount,Fee (DGB),\"Price (USD, daily)\",\"Value (USD, daily)\",Status,Confirmations,Name,"
            + "Counterparty address,Wallet address,Note,Transaction ID",
            header);
    }

    [Fact]
    public void SpanishHeaders_MatchTheContract()
    {
        var content = TransactionCsvWriter.Write([Row("a", DateTime.UtcNow)], Dot, CsvWords.Spanish, "EUR", PriceBasis.Native);
        var header = Lines(content)[0];

        Assert.Equal(
            "Fecha,Zona horaria,Activo,Tipo,Importe,Comisión (DGB),\"Precio (EUR, diario)\",\"Valor (EUR, diario)\",Estado,Confirmaciones,"
            + "Nombre,Dirección de la otra parte,Dirección de la billetera,Nota,ID de transacción",
            header);
    }

    [Fact]
    public void ConvertedCurrency_HeadersSayViaUsd()
    {
        var content = TransactionCsvWriter.Write([Row("a", DateTime.UtcNow)], Dot, CsvWords.English, "KES", PriceBasis.ConvertedFromUsd);
        Assert.Contains("\"Value (KES, daily, via USD)\"", Lines(content)[0]);
    }

    [Fact]
    public void Rows_AreNewestFirst_WithNoDuplicateTransactionId()
    {
        var t = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[] { Row("old", t.AddDays(-2)), Row("new", t), Row("mid", t.AddDays(-1)), Row("new", t) };

        var lines = Lines(TransactionCsvWriter.Write(rows, Dot, CsvWords.English, "USD", PriceBasis.Native));

        var ids = lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')[^1]).ToList();
        Assert.Equal(["new", "mid", "old"], ids);
    }

    [Fact]
    public void ZeroRows_IsRefused_NeverAHeaderOnlyFile() =>
        Assert.Throws<ArgumentException>(() =>
            TransactionCsvWriter.Write([], Dot, CsvWords.English, "USD", PriceBasis.Native));

    [Fact]
    public void Row_WritesEveryCellInOrder()
    {
        var row = Row("tx1", new DateTime(2026, 11, 20, 15, 30, 5, DateTimeKind.Utc), -100m) with
        {
            LocalTime = "2026-11-20 23:30:05",
            UtcOffset = "UTC+08:00",
            FeeDgb = -0.000226m,
            Price = 0.0109m,
            Value = -1.09m,
            Name = "Supplier",
            CounterpartyAddress = "DQ4x",
            WalletAddress = "dgb1qk2",
            Note = "Invoice 12, part 1",
        };

        var line = Lines(TransactionCsvWriter.Write([row], Dot, CsvWords.English, "USD", PriceBasis.Native))[1];

        Assert.Equal(
            "2026-11-20 23:30:05,UTC+08:00,DGB,Sent,-100.00000000,-0.00022600,0.01090000,-1.09,Confirmed,6,Supplier,DQ4x,dgb1qk2,"
            + "\"Invoice 12, part 1\",tx1",
            line);
    }

    [Fact]
    public void DdRow_HasTwoDecimalAmounts()
    {
        var row = Row("dd1", DateTime.UtcNow, 12.5m, ExportAsset.Dd);
        var line = Lines(TransactionCsvWriter.Write([row], Dot, CsvWords.English, "USD", PriceBasis.Peg))[1];
        Assert.Contains(",DD,Received,12.50,", line);
    }

    [Fact]
    public void SemicolonStyle_WritesCommaDecimals()
    {
        var row = Row("tx", DateTime.UtcNow, -1.5m);
        var content = TransactionCsvWriter.Write([row], CsvFormat.ForDecimalMark(","), CsvWords.English, "EUR", PriceBasis.Native);
        var line = Lines(content)[1];
        Assert.Contains(";-1,50000000;", line);
        Assert.StartsWith("Date;Time zone;", Lines(content)[0]);
    }
}
