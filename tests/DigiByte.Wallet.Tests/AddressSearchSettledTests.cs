using DigiByte.Wallet.Services;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A balance read before the wallet has finished finding its used addresses covers only the addresses it already knew. On a
/// freshly restored wallet that can be zero while the funds sit a little further along, so the read must say what it is, and
/// the wallet must say when the search has finished.
/// </summary>
public class AddressSearchSettledTests
{
    private static async Task<(Env Env, WalletService Service, TaskCompletionSource Gate)> RestoredWithSlowSearchAsync()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.Credit(env.ReceiveAddress(25), 5 * OneDgb);

        var gate = new TaskCompletionSource();
        env.Chain.HistoryGate = gate.Task;
        service.AddressDiscoveryWait = TimeSpan.FromMilliseconds(20);
        return (env, service, gate);
    }

    [Fact]
    public async Task Balance_ReadWhileTheSearchIsRunning_SaysAddressesAreNotSettled()
    {
        var (_, service, gate) = await RestoredWithSlowSearchAsync();

        var balance = await service.GetBalanceAsync();

        Assert.False(balance.AddressesSettled);
        Assert.False(service.AddressesSettled);
        Assert.Equal(0, balance.ConfirmedSatoshis);
        gate.SetResult();
    }

    [Fact]
    public async Task Search_WhenItFinishes_SaysSo_AndTheNextBalanceIsSettledAndComplete()
    {
        var (env, service, gate) = await RestoredWithSlowSearchAsync();
        var finished = new TaskCompletionSource<string>();
        service.AddressSearchFinished += id => finished.TrySetResult(id);
        await service.GetBalanceAsync();

        gate.SetResult();
        env.Chain.HistoryGate = null;
        var walletId = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(service.ActiveWallet!.Id, walletId);
        Assert.True(service.AddressesSettled);
        var balance = await service.GetBalanceAsync();
        Assert.True(balance.AddressesSettled);
        Assert.Equal(5 * OneDgb, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Balance_ReadAfterTheSearch_IsSettled()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);

        var balance = await service.GetBalanceAsync();

        Assert.True(balance.AddressesSettled);
        Assert.True(service.AddressesSettled);
    }

    [Fact]
    public async Task Search_WhenItFails_DoesNotSayItFinished_AndTheBalanceStaysUnsettled()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.HistoryDown = true;
        var finished = false;
        service.AddressSearchFinished += _ => finished = true;

        var balance = await service.GetBalanceAsync();

        Assert.False(balance.AddressesSettled);
        Assert.False(service.AddressesSettled);
        Assert.False(finished);
    }

    [Fact]
    public async Task Search_WhenAListenerThrows_StillSettles()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        service.AddressSearchFinished += _ => throw new InvalidOperationException("listener failed");

        var balance = await service.GetBalanceAsync();

        Assert.True(balance.AddressesSettled);
    }
}
