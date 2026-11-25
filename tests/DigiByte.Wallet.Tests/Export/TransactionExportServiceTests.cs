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
