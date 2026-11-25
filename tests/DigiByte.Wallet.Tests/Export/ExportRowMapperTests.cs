using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests.Export;

/// <summary>Wallet transactions become file rows (spec 031, research R6, FR-004/FR-005).</summary>
public class ExportRowMapperTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.CreateCustomTimeZone("Test+8", TimeSpan.FromHours(8), "Test+8", "Test+8");

    internal static TimeZoneInfo NewYork() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var tz) ? tz
        : TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static TransactionRecord Record(TransactionDirection direction, long amount, long fee = 22_600, int confirmations = 6,
        DateTime? when = null) => new()
    {
        TxId = "tx" + amount,
        Direction = direction,
        AmountSatoshis = amount,
        FeeSatoshis = fee,
        Timestamp = when ?? new DateTime(2026, 11, 18, 2, 2, 44, DateTimeKind.Utc),
        Confirmations = confirmations,
        CounterpartyAddress = "DQ4counterparty",
        WalletAddresses = ["dgb1qmine"],
    };

    // ── Signs, fee and type (FR-005) ──

    [Fact]
    public void Received_IsPositive_WithNoFee()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 250 * 100_000_000L), Manila);
        Assert.Equal(ExportKind.Received, row.Kind);
        Assert.Equal(250m, row.Amount);
        Assert.Null(row.FeeDgb);
        Assert.Equal(ExportAsset.Dgb, row.Asset);
    }

    [Fact]
    public void Sent_IsNegative_ExcludesTheFee_AndTheFeeIsNegative()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Sent, 100 * 100_000_000L), Manila);
        Assert.Equal(ExportKind.Sent, row.Kind);
        Assert.Equal(-100m, row.Amount);
        Assert.Equal(-0.000226m, row.FeeDgb);
    }

    [Fact]
    public void SentWithNothingToOthers_IsAnOwnTransfer_WithZeroAmountAndTheFee()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Sent, 0, fee: 11_300), Manila);
        Assert.Equal(ExportKind.OwnTransfer, row.Kind);
        Assert.Equal(0m, row.Amount);
        Assert.Equal(-0.000113m, row.FeeDgb);
    }

    [Theory]
    [InlineData(DigiDollarTxType.Transfer, ExportKind.DigiDollarTransfer)]
    [InlineData(DigiDollarTxType.Mint, ExportKind.DigiDollarMinted)]
    [InlineData(DigiDollarTxType.Redeem, ExportKind.DigiDollarRedeemed)]
    public void DigiDollarTransaction_InTheDgbFile_TakesItsTypeFromTheSummary(DigiDollarTxType type, ExportKind expected)
    {
        var record = Record(TransactionDirection.Sent, 0);
        record.DigiDollar = new DigiDollarTxSummary { Type = type, OutputCents = [142] };
        var row = ExportRowMapper.FromRecord(record, Manila);
        Assert.Equal(expected, row.Kind);
        Assert.Equal(ExportAsset.Dgb, row.Asset);
    }

    [Fact]
    public void NoConfirmations_IsUnconfirmed()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1, confirmations: 0), Manila);
        Assert.Equal(ExportStatus.Unconfirmed, row.Status);
        Assert.Equal(0, row.Confirmations);
    }

    // ── Local time and offset (FR-004, clarification Q4) ──

    [Fact]
    public void Date_IsLocalTime_WithItsOffset()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1), Manila);
        Assert.Equal("2026-11-18 10:02:44", row.LocalTime);
        Assert.Equal("UTC+08:00", row.UtcOffset);
    }

    [Fact]
    public void LateEveningLocal_KeepsItsLocalDate_ThoughItIsTheNextDayInUtc()
    {
        // 23:30 local on the 20th in UTC-5 is 04:30 UTC on the 21st.
        var utc = new DateTime(2026, 11, 21, 4, 30, 0, DateTimeKind.Utc);
        var minus5 = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1, when: utc), minus5);
        Assert.Equal("2026-11-20 23:30:00", row.LocalTime);
        Assert.Equal("UTC-05:00", row.UtcOffset);
        Assert.Equal(new DateOnly(2026, 11, 20), row.LocalDate);
    }

    [Fact]
    public void RowsEitherSideOfADaylightSavingChange_CarryDifferentOffsets()
    {
        var ny = NewYork();
        var summer = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1, when: new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)), ny);
        var winter = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 2, when: new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc)), ny);
        Assert.Equal("UTC-04:00", summer.UtcOffset);
        Assert.Equal("UTC-05:00", winter.UtcOffset);
    }

    // ── DigiDollar rows (US4) ──

    private static DigiDollarTransaction Dd(DdDirection direction, long cents, long? feeSats = 22_600, DdTxStatus status = DdTxStatus.Confirmed,
        int confirmations = 6) =>
        new("dd" + cents, direction, "TDcounterparty", cents, feeSats, confirmations,
            new DateTimeOffset(2026, 11, 18, 2, 2, 44, TimeSpan.Zero), status);

    [Fact]
    public void DdReceived_IsPositiveDd_WithNoFee()
    {
        var row = ExportRowMapper.FromDd(Dd(DdDirection.Received, 1250, feeSats: null), Manila);
        Assert.Equal(ExportAsset.Dd, row.Asset);
        Assert.Equal(ExportKind.Received, row.Kind);
        Assert.Equal(12.50m, row.Amount);
        Assert.Null(row.FeeDgb);
        Assert.Equal(2, row.AmountDecimals);
    }

    [Fact]
    public void DdSent_IsNegativeDd_AndTheFeeIsNegativeDgb()
    {
        var row = ExportRowMapper.FromDd(Dd(DdDirection.Sent, 1250), Manila);
        Assert.Equal(ExportKind.Sent, row.Kind);
        Assert.Equal(-12.50m, row.Amount);
        Assert.Equal(-0.000226m, row.FeeDgb);
    }

    [Fact]
    public void DdSelfTransfer_IsAnOwnTransfer_WithZeroAndTheFee()
    {
        var row = ExportRowMapper.FromDd(Dd(DdDirection.SelfTransfer, 1250), Manila);
        Assert.Equal(ExportKind.OwnTransfer, row.Kind);
        Assert.Equal(0m, row.Amount);
        Assert.Equal(-0.000226m, row.FeeDgb);
    }

    [Fact]
    public void DdFailedSend_IsFailed_WithZeroAmount_SoItIsNeverCounted()
    {
        var row = ExportRowMapper.FromDd(Dd(DdDirection.Sent, 1250, status: DdTxStatus.Failed, confirmations: 0), Manila);
        Assert.Equal(ExportStatus.Failed, row.Status);
        Assert.Equal(0m, row.Amount);
        Assert.Null(row.FeeDgb);
    }

    [Fact]
    public void DdPending_IsUnconfirmed_InLocalTime()
    {
        var row = ExportRowMapper.FromDd(Dd(DdDirection.Received, 100, feeSats: null, status: DdTxStatus.Pending, confirmations: 0), Manila);
        Assert.Equal(ExportStatus.Unconfirmed, row.Status);
        Assert.Equal("2026-11-18 10:02:44", row.LocalTime);
        Assert.Equal("TDcounterparty", row.CounterpartyAddress);
    }

    // ── Names and labels (US5, FR-015) ──

    [Fact]
    public void Name_IsTheContactsName_ForTheCounterparty()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1), Manila,
            nameOf: a => string.Equals(a, "dq4COUNTERPARTY", StringComparison.OrdinalIgnoreCase) ? "Customer – Alice" : null);
        Assert.Equal("Customer – Alice", row.Name);
    }

    [Fact]
    public void Name_FallsBackToTheNameRecordedWithTheTransaction()
    {
        var record = Record(TransactionDirection.Received, 1);
        record.CounterpartyName = "Savings wallet";
        Assert.Equal("Savings wallet", ExportRowMapper.FromRecord(record, Manila, nameOf: _ => null).Name);
    }

    [Fact]
    public void NoName_IsAnEmptyCell_NeverAPlaceholder()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1), Manila, nameOf: _ => null);
        Assert.Null(row.Name);
        var line = TransactionCsvWriter.Write([row], CsvFormat.ForDecimalMark("."), CsvWords.English, "USD", PriceBasis.Native)
            .TrimStart('﻿').Split("\r\n")[1];
        Assert.Contains(",6,,DQ4counterparty,", line);   // Confirmations, empty Name, Counterparty
    }

    [Fact]
    public void WalletAddress_CarriesItsLabel()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1), Manila,
            walletLabelOf: a => a == "dgb1qmine" ? "Shop till" : null);
        Assert.Equal("Shop till (dgb1qmine)", row.WalletAddress);
    }

    [Fact]
    public void DdName_IsTheContactsName()
    {
        var row = ExportRowMapper.FromDd(Dd(DdDirection.Received, 100, feeSats: null), Manila,
            nameOf: a => a == "TDcounterparty" ? "Supplier" : null);
        Assert.Equal("Supplier", row.Name);
    }

    [Fact]
    public void AFormulaLikeName_IsWrittenAsText()
    {
        var row = ExportRowMapper.FromRecord(Record(TransactionDirection.Received, 1), Manila, nameOf: _ => "@SUM(1)");
        var line = TransactionCsvWriter.Write([row], CsvFormat.ForDecimalMark("."), CsvWords.English, "USD", PriceBasis.Native)
            .TrimStart('﻿').Split("\r\n")[1];
        Assert.Contains(",'@SUM(1),", line);
    }

    // ── Addresses and note ──

    [Fact]
    public void Addresses_AndNote_AreCopied()
    {
        var record = Record(TransactionDirection.Sent, 5);
        record.Memo = "Invoice 12";
        var row = ExportRowMapper.FromRecord(record, Manila);
        Assert.Equal("DQ4counterparty", row.CounterpartyAddress);
        Assert.Equal("dgb1qmine", row.WalletAddress);
        Assert.Equal("Invoice 12", row.Note);
        Assert.Equal("tx5", row.TxId);
    }
}
