using DigiByte.Wallet.Services;
using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests.Export;

/// <summary>
/// The whole export: read → map → filter → prices → write (spec 031, data-model.md "State flow"), against a real wallet on the
/// test chain, so the history rules are the app's own.
/// </summary>
public class TransactionExportServiceTests
{
    internal static readonly TimeZoneInfo Utc8 = TimeZoneInfo.CreateCustomTimeZone("Test+8", TimeSpan.FromHours(8), "Test+8", "Test+8");
    internal static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    internal static ExportEnvironment Env(string fiat = "USD") => new()
    {
        DecimalMark = ".",
        Language = "en",
        TimeZone = Utc8,
        Now = Now,
        FiatCurrency = fiat,
    };

    internal static async Task<(ChangeAddressTests.Env Env, WalletService Wallet)> WalletWithAsync(int credits, string name = "Test")
    {
        var env = new ChangeAddressTests.Env();
        env.Chain.DetailedHistory = true;
        var service = env.NewService();
        await service.CreateWalletAsync(name, env.Words, ChangeAddressTests.Pin);
        for (var i = 0; i < credits; i++)
            env.Chain.Credit(env.ReceiveAddress(i % 3), (i + 1) * ChangeAddressTests.OneDgb);
        return (env, service);
    }

    private static string[] DataLines(string content) =>
        content.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();

    [Fact]
    public async Task Everything_ExportsEveryTransaction()
    {
        var (env, wallet) = await WalletWithAsync(7);
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(ExportOutcome.Ready, result.Outcome);
        Assert.Equal(7, result.RowCount);
        Assert.Equal(7, DataLines(result.Content!).Length);
    }

    [Fact]
    public async Task HistoryUnreadable_IsHistoryUnavailable_WithNoContent()
    {
        var (env, wallet) = await WalletWithAsync(3);
        env.Chain.HistoryDown = true;
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(ExportOutcome.HistoryUnavailable, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task LockedWallet_IsLocked_WithNoContent()
    {
        var env = new ChangeAddressTests.Env();
        var sut = new TransactionExportService(env.NewService(), env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(ExportOutcome.Locked, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task NoTransactions_IsNothingToExport_WithNoContent()
    {
        var (env, wallet) = await WalletWithAsync(0);
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(ExportOutcome.NothingToExport, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Cancelled_IsCancelled_WithNoContent()
    {
        var (env, wallet) = await WalletWithAsync(2);
        var sut = new TransactionExportService(wallet, env.Chain);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env(), null, cts.Token);

        Assert.Equal(ExportOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Content);
    }

    [Theory]
    [InlineData("Test", "digibyte-test-DGB-2026-11-26.csv")]
    [InlineData("Shop Till #2", "digibyte-shop-till-2-DGB-2026-11-26.csv")]
    [InlineData("Café Ñandú", "digibyte-cafe-nandu-DGB-2026-11-26.csv")]
    [InlineData("💰💰", "digibyte-wallet-DGB-2026-11-26.csv")]
    public void FileName_IdentifiesWalletAssetAndLocalExportDate(string walletName, string expected) =>
        Assert.Equal(expected, TransactionExportService.FileNameFor(walletName, ExportAsset.Dgb, Env()));

    // ── US2: what to export ──

    [Fact]
    public async Task DateRangeMatchingNothing_IsNothingToExport_WithNoContent()
    {
        var (env, wallet) = await WalletWithAsync(3);   // all today
        var sut = new TransactionExportService(wallet, env.Chain);
        var request = new ExportRequest
        {
            Asset = ExportAsset.Dgb, Scope = ExportScope.DateRange, Preset = ExportPreset.Custom,
            From = new DateOnly(2020, 1, 1), To = new DateOnly(2020, 1, 31),
        };

        var result = await sut.ExportAsync(request, Env());

        Assert.Equal(ExportOutcome.NothingToExport, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task CurrentView_MatchingNothing_IsNothingToExport()
    {
        var (env, wallet) = await WalletWithAsync(3);   // only received
        var sut = new TransactionExportService(wallet, env.Chain);
        var request = new ExportRequest
        {
            Asset = ExportAsset.Dgb, Scope = ExportScope.CurrentView,
            View = new ActivityView("sent", "", null, _ => null, new Dictionary<string, string>()),
        };

        var result = await sut.ExportAsync(request, Env());

        Assert.Equal(ExportOutcome.NothingToExport, result.Outcome);
    }

    [Fact]
    public async Task CurrentView_KeepsOnlyMatchingRows()
    {
        var (env, wallet) = await WalletWithAsync(3);
        var sut = new TransactionExportService(wallet, env.Chain);
        var request = new ExportRequest
        {
            Asset = ExportAsset.Dgb, Scope = ExportScope.CurrentView,
            // Credits go to receive addresses 0, 1, 2: choosing address 0 keeps one of the three.
            View = new ActivityView("received", "", env.ReceiveAddress(0), _ => null, new Dictionary<string, string>()),
        };

        var result = await sut.ExportAsync(request, Env());

        Assert.Equal(ExportOutcome.Ready, result.Outcome);
        Assert.Equal(1, result.RowCount);
    }

    // ── US3: daily fiat values ──

    private static DailyPriceSeries Series(PriceBasis basis, params (DateOnly Day, decimal Price)[] days) =>
        new("USD", basis, days.ToDictionary(d => d.Day, d => d.Price), DateOnly.MinValue, DateOnly.MaxValue);

    [Fact]
    public async Task EachRow_IsValuedAtItsLocalDaysPrice()
    {
        var (env, wallet) = await WalletWithAsync(2);   // 1 DGB and 2 DGB, both stamped "now" by the test chain
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Utc8).DateTime);
        env.Chain.DailyPrices = (_, _, _, _) => Series(PriceBasis.Native, (today, 0.5m));
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(0, result.RowsWithoutPrice);
        var values = DataLines(result.Content!).Select(l => l.Split(',')[7]).OrderBy(v => v).ToList();
        Assert.Equal(["0.50", "1.00"], values);   // 1 × 0.5 and 2 × 0.5, USD decimals
    }

    [Fact]
    public void EqualAmountsOnDaysWithDifferentPrices_GetDifferentValues()
    {
        var mapper = TransactionCsvWriterTests.Row("a", DateTime.UtcNow, 10m) with { LocalDate = new DateOnly(2026, 11, 20) };
        var other = mapper with { TxId = "b", LocalDate = new DateOnly(2026, 11, 21) };
        var series = Series(PriceBasis.Native, (new DateOnly(2026, 11, 20), 0.01m), (new DateOnly(2026, 11, 21), 0.02m));

        var (rows, missing) = TransactionExportService.ApplyPrices([mapper, other], series, "USD");

        Assert.Equal(0.10m, rows[0].Value);
        Assert.Equal(0.20m, rows[1].Value);
        Assert.Equal(0, missing);
    }

    [Fact]
    public async Task PriceRequest_IsOneRange_WidenedByADayEachSide()
    {
        var (env, wallet) = await WalletWithAsync(2);
        var sut = new TransactionExportService(wallet, env.Chain);

        await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        var (currency, asset, from, to) = Assert.Single(env.Chain.PriceRequests);
        Assert.Equal("USD", currency);
        Assert.Equal("dgb", asset);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Utc8).DateTime);
        Assert.Equal(today.AddDays(-1), from);
        Assert.Equal(today.AddDays(1), to);
    }

    [Fact]
    public async Task PriceFailure_StillGivesAFile_WithEveryRowCounted()
    {
        var (env, wallet) = await WalletWithAsync(3);   // DailyPrices not set: the price service is "down"
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(ExportOutcome.Ready, result.Outcome);
        Assert.Equal(3, result.RowsWithoutPrice);
    }

    [Fact]
    public async Task Headers_CarryTheCurrencyAndBasis()
    {
        var (env, wallet) = await WalletWithAsync(1);
        env.Chain.DailyPrices = (c, _, f, t) => new DailyPriceSeries(c, PriceBasis.ConvertedFromUsd, new Dictionary<DateOnly, decimal>(), f, t);
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env("KES"));

        var header = result.Content!.TrimStart('﻿').Split("\r\n")[0];
        Assert.Contains("\"Price (KES, daily, via USD)\"", header);
    }

    // ── US4: DigiDollar ──

    private sealed class FakeDd(IReadOnlyList<DigiByte.Wallet.Models.DigiDollarTransaction>? history, bool fail = false) : IDigiDollarService
    {
        public Task<IReadOnlyList<DigiByte.Wallet.Models.DigiDollarTransaction>> LoadCompleteHistoryAsync(
            IProgress<HistoryExportProgress>? progress = null, CancellationToken ct = default) =>
            fail ? Task.FromException<IReadOnlyList<DigiByte.Wallet.Models.DigiDollarTransaction>>(new HistoryIncompleteException("down"))
                 : Task.FromResult(history!);

        public Task<DdDashboard> RefreshAsync(CancellationToken ct = default, IProgress<DdScanProgress>? progress = null) => throw new NotImplementedException();
        public Task<DigiByte.Wallet.Models.DigiDollarAddress> NewReceiveAddressAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DigiByte.Wallet.Models.DigiDollarAddress>> GetReceiveAddressesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DdSendCheck> CheckSendAsync(string recipient, long amountCents, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DdSendResult> SendAsync(string recipient, long amountCents, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> GetSendAllAmountAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task AcknowledgeFailedSendAsync(string txid, CancellationToken ct = default) => throw new NotImplementedException();
        public void ResetSession() { }
    }

    private static DigiByte.Wallet.Models.DigiDollarTransaction DdTx(string id, DigiByte.Wallet.Models.DdDirection direction, long cents) =>
        new(id, direction, "TDcounterparty", cents, direction == DigiByte.Wallet.Models.DdDirection.Received ? null : 22_600, 6,
            DateTimeOffset.UtcNow, DigiByte.Wallet.Models.DdTxStatus.Confirmed);

    [Fact]
    public async Task Dd_ExportsDdRowsOnly_NamedDd_WithTheUsdPegAndNoPriceRequest()
    {
        var (env, wallet) = await WalletWithAsync(2);   // DGB history exists too: it must not appear
        var dd = new FakeDd([DdTx("d1", DigiByte.Wallet.Models.DdDirection.Received, 1250), DdTx("d2", DigiByte.Wallet.Models.DdDirection.Sent, 500)]);
        var sut = new TransactionExportService(wallet, env.Chain, dd);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dd }, Env());

        Assert.Equal(ExportOutcome.Ready, result.Outcome);
        Assert.Equal(2, result.RowCount);
        Assert.EndsWith("-DD-2026-11-26.csv", result.FileName);
        Assert.All(DataLines(result.Content!), l => Assert.Contains(",DD,", l));
        Assert.Contains(",DD,Received,12.50,,1.00000000,12.50,", result.Content);
        Assert.Contains(",DD,Sent,-5.00,-0.00022600,1.00000000,-5.00,", result.Content);
        Assert.Empty(env.Chain.PriceRequests);
        Assert.Equal(0, result.RowsWithoutPrice);
    }

    [Fact]
    public async Task Dd_OutsideUsd_AsksForTheDdSeries()
    {
        var (env, wallet) = await WalletWithAsync(0);
        var dd = new FakeDd([DdTx("d1", DigiByte.Wallet.Models.DdDirection.Received, 100)]);
        var sut = new TransactionExportService(wallet, env.Chain, dd);

        await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dd }, Env("PHP"));

        var request = Assert.Single(env.Chain.PriceRequests);
        Assert.Equal("dd", request.Asset);
        Assert.Equal("PHP", request.Currency);
    }

    [Fact]
    public async Task Dd_HistoryUnreadable_IsHistoryUnavailable()
    {
        var (env, wallet) = await WalletWithAsync(0);
        var sut = new TransactionExportService(wallet, env.Chain, new FakeDd(null, fail: true));

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dd }, Env());

        Assert.Equal(ExportOutcome.HistoryUnavailable, result.Outcome);
    }

    [Fact]
    public async Task WithoutPrices_PriceAndValueAreEmpty_AndCounted()
    {
        var (env, wallet) = await WalletWithAsync(2);
        var sut = new TransactionExportService(wallet, env.Chain);

        var result = await sut.ExportAsync(new ExportRequest { Asset = ExportAsset.Dgb }, Env());

        Assert.Equal(2, result.RowsWithoutPrice);
        Assert.All(DataLines(result.Content!), l => Assert.Contains(",,Confirmed,", l));
    }
}
