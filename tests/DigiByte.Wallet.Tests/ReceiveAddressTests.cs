using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Every receive address the wallet shows must be scanned, or money paid to it is invisible and unspendable.
/// </summary>
public class ReceiveAddressTests
{
    private static async Task<WalletService> CreateAsync(Env env)
    {
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        return service;
    }

    /// <summary>Hands out and pays a new address repeatedly, as a merchant would.</summary>
    private static async Task PayNewAddressesAsync(Env env, WalletService service, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var (_, address) = await service.GenerateNewReceivingAddressAsync();
            env.Chain.Credit(address, OneDgb);
        }
    }

    // ─── A shown address past the old range must be visible and spendable ───

    [Fact]
    public async Task Balance_IncludesPaymentToShownAddressPastIndex19()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        service.ActiveWallet!.NextReceivingIndex = 20;
        env.Chain.Credit(await service.GetReceivingAddressAsync(), 5 * OneDgb);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5 * OneDgb, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Send_CanSpendPaymentToShownAddressPastIndex19()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        service.ActiveWallet!.NextReceivingIndex = 20;
        var shown = await service.GetReceivingAddressAsync();
        env.Chain.Credit(shown, 5 * OneDgb);

        await service.SendAsync(Stranger(), 1m);

        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(shown));
    }

    [Fact]
    public async Task GeneratedAddresses_PastIndex19_AreCountedEvenIfTheExplorerIsDownAfterReload()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;

        await PayNewAddressesAsync(env, service, 30);
        Assert.Equal(30 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        env.Chain.HistoryDown = true;
        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(30 * OneDgb, (await reloaded.GetBalanceAsync()).ConfirmedSatoshis);
    }

    // ─── The index must survive leaving the page and reloading ───

    [Fact]
    public async Task Generate_IsSaved_SoAReloadShowsTheSameAddress()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;

        await service.GenerateNewReceivingAddressAsync();
        await service.GenerateNewReceivingAddressAsync();
        var (index, shown) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(3, index);
        var saved = JsonSerializer.Deserialize<WalletInfo>((await new WalletKeyStore(env.Storage).GetWalletInfoAsync(walletId))!)!;
        Assert.Equal(3, saved.NextReceivingIndex);

        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(shown, await reloaded.GetReceivingAddressAsync());
    }

    [Fact]
    public async Task Generate_AfterReload_MovesForwardNotBack()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;
        await service.GenerateNewReceivingAddressAsync();
        await service.GenerateNewReceivingAddressAsync();

        var reloaded = await env.ReloadAsync(walletId);
        var (index, address) = await reloaded.GenerateNewReceivingAddressAsync();

        Assert.Equal(3, index);
        Assert.Equal(env.ReceiveAddress(3), address);
    }

    [Fact]
    public async Task Generate_WhenItCannotBeSaved_FailsAndKeepsTheCurrentAddress()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        await service.GetBalanceAsync();
        var before = await service.GetReceivingAddressAsync();
        env.Storage.FailWalletInfoWrites = true;

        await Assert.ThrowsAsync<InvalidOperationException>(service.GenerateNewReceivingAddressAsync);

        Assert.Equal(before, await service.GetReceivingAddressAsync());
    }

    // ─── Funds already stranded by older versions, whose saved index is still 0 ───

    [Fact]
    public async Task Discovery_FindsFundsStrandedPastTheOldRange_AndSavesThem()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(25), 5 * OneDgb);

        Assert.Equal(5 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        env.Chain.HistoryDown = true;
        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(5 * OneDgb, (await reloaded.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Send_CanSpendFundsStrandedPastTheOldRange()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.Credit(env.ReceiveAddress(25), 5 * OneDgb);

        await service.SendAsync(Stranger(), 1m);

        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(env.ReceiveAddress(25)));
    }

    [Fact]
    public async Task Discovery_FollowsUsedAddressesAcrossGaps()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.Credit(env.ReceiveAddress(35), OneDgb);
        env.Chain.Credit(env.ReceiveAddress(50), 2 * OneDgb);

        Assert.Equal(3 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Discovery_DoesNotChangeTheAddressOnShow()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var before = await service.GetReceivingAddressAsync();
        env.Chain.Credit(before, OneDgb);
        env.Chain.Credit(env.ReceiveAddress(25), OneDgb);

        await service.GetBalanceAsync();

        Assert.Equal(before, await service.GetReceivingAddressAsync());
    }

    [Fact]
    public async Task Generate_SkipsAddressesTheChainShowsAsUsed()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        for (var i = 0; i < 3; i++) env.Chain.Credit(env.ReceiveAddress(i), OneDgb);

        var (index, _) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(3, index);
    }

    [Fact]
    public async Task DeepScan_FindsFundsBeyondTheAutomaticSearch_AndTheyCanBeSpentAfterReload()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(200), 5 * OneDgb);
        Assert.Equal(0, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        var result = await service.ScanForMissingReceiveFundsAsync();

        Assert.Equal(5 * OneDgb, result.Satoshis);
        Assert.Equal(5 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        var reloaded = await env.ReloadAsync(walletId);
        await reloaded.SendAsync(Stranger(), 1m);
        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(env.ReceiveAddress(200)));
    }

    [Fact]
    public async Task DeepScan_WithNothingToFind_ReportsNothing()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.Credit(env.ReceiveAddress(0), OneDgb);

        var result = await service.ScanForMissingReceiveFundsAsync(gapLimit: 30);

        Assert.Equal(new ReceiveRecoveryResult(0, 0), result);
    }

    [Fact]
    public async Task DeepScan_WhenExplorerIsDown_ThrowsInsteadOfReportingNothing()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.HistoryDown = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => service.ScanForMissingReceiveFundsAsync(gapLimit: 30));
    }

    // ─── Never hand out more than a restored wallet could find ───

    [Fact]
    public async Task Generate_StopsAtTheGapLimit_AndKeepsTheAddressOnShow()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        for (var i = 0; i < WalletService.ReceiveGapLimit - 1; i++)
            await service.GenerateNewReceivingAddressAsync();
        var last = await service.GetReceivingAddressAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(service.GenerateNewReceivingAddressAsync);

        Assert.Equal(last, await service.GetReceivingAddressAsync());
    }

    [Fact]
    public async Task Generate_AtTheGapLimit_ContinuesOnceAnAddressIsPaid()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        for (var i = 0; i < WalletService.ReceiveGapLimit - 1; i++)
            await service.GenerateNewReceivingAddressAsync();
        env.Chain.Credit(await service.GetReceivingAddressAsync(), OneDgb);

        var (index, _) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(WalletService.ReceiveGapLimit, index);
    }

    [Fact]
    public async Task RestoringFromSeed_FindsEveryPaidGeneratedAddress()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        await PayNewAddressesAsync(env, service, 45);
        for (var i = 0; i < WalletService.ReceiveGapLimit - 1; i++)
            await service.GenerateNewReceivingAddressAsync();
        env.Chain.Credit(await service.GetReceivingAddressAsync(), OneDgb);

        var restored = new WalletService(
            new WalletKeyStore(new InMemoryStorage()), new PassThroughCrypto(), env.Chain, new TransactionTracker(new InMemoryStorage()));
        restored.SetNetworkMode("mainnet");
        await restored.CreateWalletAsync("Restored", env.Words, Pin);

        Assert.Equal(46 * OneDgb, (await restored.GetBalanceAsync()).ConfirmedSatoshis);
    }

    // ─── Watch-only wallets derive the same addresses ───

    [Fact]
    public async Task WatchOnlyWallet_CanGenerate_AndSeesThePayment()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWatchOnlyWalletAsync("Watch", env.Hd.GetAccountExtPubKey().ToString(Network), Pin);

        await PayNewAddressesAsync(env, service, 25);

        Assert.Equal(25 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }
}
