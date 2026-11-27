using DigiByte.Wallet.Services;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Each payment request gets a receive address of its own, without moving the address Receive shows (spec 036,
/// FR-001, FR-003). Requests and Receive share the budget of 20 unpaid addresses (FR-005), and every request address
/// stays checked after a reload (FR-002, SC-005).
/// </summary>
public class RequestAddressHandOutTests
{
    private static async Task<WalletService> CreateAsync(Env env)
    {
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        await service.GetBalanceAsync();
        return service;
    }

    [Fact]
    public async Task EachRequest_GetsANewAddress_AndReceiveDoesNotMove()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var shown = await service.GetReceivingAddressAsync();

        var (i1, a1) = await service.HandOutRequestAddressAsync();
        var (i2, a2) = await service.HandOutRequestAddressAsync();

        Assert.NotEqual(a1, a2);
        Assert.NotEqual(shown, a1);
        Assert.Equal(i1 + 1, i2);
        Assert.Equal(env.ReceiveAddress(i1), a1);
        Assert.Equal(env.ReceiveAddress(i2), a2);
        Assert.Equal(shown, await service.GetReceivingAddressAsync());
        Assert.Equal(0, service.ActiveWallet!.NextReceivingIndex);
        Assert.Equal(i2, service.ActiveWallet.HandedOutTop);
    }

    [Fact]
    public async Task ReceivesNewAddress_SkipsTheIndexesGivenToRequests()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var (i1, _) = await service.HandOutRequestAddressAsync();
        var (i2, _) = await service.HandOutRequestAddressAsync();

        var (shownIndex, _) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(i2 + 1, shownIndex);
        Assert.NotEqual(i1, shownIndex);
        Assert.Equal(shownIndex, service.ActiveWallet!.HandedOutTop);
        var (i3, _) = await service.HandOutRequestAddressAsync();
        Assert.Equal(shownIndex + 1, i3);
    }

    [Fact]
    public async Task AContiguousRequestIndex_ExtendsTheBaseRun_AndIsChecked()
    {
        var env = new Env();
        var service = await CreateAsync(env);

        // 20 hand-outs would hit the limit; pay as we go so the budget never runs out, and reach past index 20.
        var last = 0;
        for (var n = 0; n < 25; n++)
        {
            var (index, address) = await service.HandOutRequestAddressAsync();
            env.Chain.Credit(address, OneDgb);
            await service.GetBalanceAsync();
            last = index;
        }

        Assert.True(last >= 20);
        Assert.Empty(service.ActiveWallet!.WatchedReceiveIndexes);
        Assert.Equal(25 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task ANonContiguousRequestIndex_BecomesAnUnpaidWatchedEntry()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        // A wider search found a payment far along (spec 027): new hand-outs come after it, not after the base run.
        service.ActiveWallet!.WatchedReceiveIndexes = [new WatchedReceiveIndex(3000, true)];

        var (index, _) = await service.HandOutRequestAddressAsync();

        Assert.Equal(3001, index);
        Assert.Contains(new WatchedReceiveIndex(3001, false), service.ActiveWallet.WatchedReceiveIndexes);
        Assert.Equal(0, service.ActiveWallet.NextReceivingIndex);
    }

    [Fact]
    public async Task AfterTwentyUnpaidHandOuts_RequestsAndReceiveTogether_TheNextIsRefused()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        // Index 0 is on show. 9 more to Receive and 10 to requests: indexes 0..19 are out, none paid.
        for (var n = 0; n < 9; n++) await service.GenerateNewReceivingAddressAsync();
        for (var n = 0; n < 10; n++) await service.HandOutRequestAddressAsync();

        var ex = await Assert.ThrowsAsync<ReceiveAddressException>(service.HandOutRequestAddressAsync);
        Assert.Equal(ReceiveAddressFailure.TooManyUnusedPastLastUsed, ex.Reason);
        var ex2 = await Assert.ThrowsAsync<ReceiveAddressException>(service.GenerateNewReceivingAddressAsync);
        Assert.Equal(ReceiveAddressFailure.TooManyUnusedPastLastUsed, ex2.Reason);
    }

    [Fact]
    public async Task PayingARequest_FreesRoomForMore()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        string? last = null;
        for (var n = 0; n < 19; n++) last = (await service.HandOutRequestAddressAsync()).Address;
        await Assert.ThrowsAsync<ReceiveAddressException>(service.HandOutRequestAddressAsync);

        env.Chain.Credit(last!, OneDgb);

        var (index, _) = await service.HandOutRequestAddressAsync();
        Assert.Equal(20, index);
    }

    [Fact]
    public async Task WhenItCannotBeSaved_ItFails_AndChangesNothing()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        service.ActiveWallet!.WatchedReceiveIndexes = [new WatchedReceiveIndex(3000, true)];
        var shown = await service.GetReceivingAddressAsync();
        env.Storage.FailWalletInfoWrites = true;

        var ex = await Assert.ThrowsAsync<ReceiveAddressException>(service.HandOutRequestAddressAsync);

        Assert.Equal(ReceiveAddressFailure.SaveFailed, ex.Reason);
        Assert.Null(service.ActiveWallet.HandedOutTop);
        Assert.Equal([new WatchedReceiveIndex(3000, true)], service.ActiveWallet.WatchedReceiveIndexes);
        Assert.Equal(shown, await service.GetReceivingAddressAsync());
        env.Storage.FailWalletInfoWrites = false;
        Assert.Equal(3001, (await service.HandOutRequestAddressAsync()).Index);
    }

    [Fact]
    public async Task OnASingleAddressWallet_SaysSo()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletFromWifAsync("Paper", new NBitcoin.Key().GetWif(Network).ToString(), Pin);

        var ex = await Assert.ThrowsAsync<ReceiveAddressException>(service.HandOutRequestAddressAsync);

        Assert.Equal(ReceiveAddressFailure.SingleAddress, ex.Reason);
    }

    [Fact]
    public async Task AfterAReload_RequestAddressesAreStillChecked_AndTheIndexIsKept()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var (_, a1) = await service.HandOutRequestAddressAsync();
        var (i2, a2) = await service.HandOutRequestAddressAsync();

        var reloaded = await env.ReloadAsync(walletId);
        env.Chain.Credit(a1, OneDgb);
        env.Chain.Credit(a2, 2 * OneDgb);

        Assert.Equal(3 * OneDgb, (await reloaded.GetBalanceAsync()).ConfirmedSatoshis);
        Assert.Equal(i2, reloaded.ActiveWallet!.HandedOutTop);
        Assert.Equal(0, reloaded.ActiveWallet.NextReceivingIndex);
        Assert.Equal(i2 + 1, (await reloaded.HandOutRequestAddressAsync()).Index);
    }
}
