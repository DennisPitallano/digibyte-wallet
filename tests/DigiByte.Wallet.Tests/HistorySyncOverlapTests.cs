namespace DigiByte.Wallet.Tests;

/// <summary>
/// Callers that overlap share one refresh, but only when that refresh covers the addresses they asked about. A wallet that
/// has just found more used addresses must not be handed the result of a run that started before it knew them: that result
/// leaves the new addresses out, and on a restored wallet that is a balance of zero.
/// </summary>
public class HistorySyncOverlapTests
{
    private const string W = "w1";
    private const string N = "mainnet";

    [Fact]
    public async Task Refresh_ForMoreAddressesWhileOneIsRunning_CoversTheNewAddresses()
    {
        var env = new HistorySyncEnv();
        var known = env.Receive(0);
        var found = env.Receive(25);
        env.Explorer.Pay(found, 5000);
        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;

        var first = env.Sync.RefreshAsync(W, N, [known]);
        var second = env.Sync.RefreshAsync(W, N, [known, found]);
        env.Explorer.Hold = null;
        hold.SetResult();

        Assert.Equal(0, (await first).ConfirmedSatoshis);
        Assert.Equal(5000, (await second).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Refresh_ForTheSameAddressesWhileOneIsRunning_SharesThatRun()
    {
        var env = new HistorySyncEnv();
        var addresses = new[] { env.Receive(0), env.Receive(1) };
        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;

        var first = env.Sync.RefreshAsync(W, N, addresses);
        var second = env.Sync.RefreshAsync(W, N, addresses);
        hold.SetResult();

        Assert.Same(first, second);
        await first;
    }

    [Fact]
    public async Task Refresh_ForFewerAddressesWhileOneIsRunning_SharesThatRun()
    {
        var env = new HistorySyncEnv();
        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;

        var first = env.Sync.RefreshAsync(W, N, [env.Receive(0), env.Receive(1)]);
        var second = env.Sync.RefreshAsync(W, N, [env.Receive(1)]);
        hold.SetResult();

        Assert.Same(first, second);
        await first;
    }

    [Fact]
    public async Task Refresh_ForMoreAddresses_StillRunsWhenTheOneInFlightFails()
    {
        var env = new HistorySyncEnv();
        var known = env.Receive(0);
        var found = env.Receive(25);
        env.Explorer.Pay(found, 5000);
        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;

        var first = env.Sync.RefreshAsync(W, N, [known]);
        var second = env.Sync.RefreshAsync(W, N, [known, found]);
        // Only the read already in flight fails: wait until the first run is held on it before letting later reads through.
        for (var i = 0; i < 1000 && env.Explorer.SummaryAddresses.Count == 0; i++) await Task.Delay(5);
        env.Explorer.Hold = null;
        hold.SetException(new HttpRequestException("explorer is down"));

        await Assert.ThrowsAnyAsync<Exception>(() => first);
        Assert.Equal(5000, (await second).ConfirmedSatoshis);
    }

    [Fact]
    public async Task TwoRefreshes_ForMoreAddressesWhileOneIsRunning_ShareOneFollowUpRun()
    {
        var env = new HistorySyncEnv();
        var known = env.Receive(0);
        var found = env.Receive(25);
        env.Explorer.Pay(found, 5000);
        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;

        var first = env.Sync.RefreshAsync(W, N, [known]);
        var second = env.Sync.RefreshAsync(W, N, [known, found]);
        var third = env.Sync.RefreshAsync(W, N, [known, found]);
        env.Explorer.Hold = null;
        hold.SetResult();
        await first;
        env.Explorer.ResetCounts();

        Assert.Equal(5000, (await second).ConfirmedSatoshis);
        Assert.Equal(5000, (await third).ConfirmedSatoshis);
    }
}
