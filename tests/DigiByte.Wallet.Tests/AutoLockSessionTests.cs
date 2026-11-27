using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Locking the app must lock the wallet: the decrypted session (seed-derived keys) is dropped from memory, not just hidden
/// behind the unlock screen. Covers every way the app locks: "Lock now", lock when hidden and the inactivity timer.
/// </summary>
public class AutoLockSessionTests
{
    private static AppState NewAppState(WalletService wallet) =>
        new(new NullJs(), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DefaultNetwork"] = "mainnet" })
            .Build(), () => wallet);

    private static async Task<(ChangeAddressTests.Env Env, WalletService Wallet, AppState App)> UnlockedAsync()
    {
        var env = new ChangeAddressTests.Env();
        var wallet = await env.CreateFundedAsync();
        Assert.True(wallet.IsUnlocked);
        var app = NewAppState(wallet);
        app.IsWalletUnlocked = true;
        return (env, wallet, app);
    }

    private static async Task AssertKeysClearedAsync(WalletService wallet)
    {
        Assert.False(wallet.IsUnlocked);
        Assert.Null(wallet.ActiveWallet);
        Assert.False(wallet.CanSign);
        Assert.Null(wallet.GetPrivateKeyForMultisig());
        await Assert.ThrowsAsync<InvalidOperationException>(() => wallet.GetReceivingAddressAsync());
    }

    [Fact]
    public async Task LockNow_ClearsTheWalletSessionAndKeys()
    {
        var (_, wallet, app) = await UnlockedAsync();

        app.LockNow();

        Assert.False(app.IsWalletUnlocked);
        await AssertKeysClearedAsync(wallet);
    }

    [Fact]
    public async Task OnTabHidden_WithLockOnHidden_ClearsTheWalletSessionAndKeys()
    {
        var (_, wallet, app) = await UnlockedAsync();
        app.LockOnHidden = true;

        app.OnTabHidden();

        await AssertKeysClearedAsync(wallet);
    }

    [Fact]
    public async Task OnTabHidden_WithoutLockOnHidden_KeepsTheSession()
    {
        var (_, wallet, app) = await UnlockedAsync();
        app.LockOnHidden = false;

        app.OnTabHidden();

        Assert.True(wallet.IsUnlocked);
    }

    [Fact]
    public async Task InactivityTimeout_ClearsTheWalletSessionAndKeys()
    {
        var (_, wallet, app) = await UnlockedAsync();
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.OnAutoLocked += () => locked.TrySetResult();
        app.LockTimeout = TimeSpan.FromMilliseconds(20);

        app.ResetLockTimer();
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(app.IsWalletUnlocked);
        await AssertKeysClearedAsync(wallet);
    }

    [Fact]
    public async Task OnAutoLocked_HandlersSeeTheWalletAlreadyLocked()
    {
        // The unlock screen and pages that react to the lock must never find keys still in memory.
        var (_, wallet, app) = await UnlockedAsync();
        bool? unlockedWhenRaised = null;
        app.OnAutoLocked += () => unlockedWhenRaised = wallet.IsUnlocked;

        app.LockNow();

        Assert.False(unlockedWhenRaised);
    }

    [Fact]
    public async Task Lock_ClearsEveryWalletUnlockedInTheSession()
    {
        // Wallets unlocked earlier in the session (for instant switching) are locked too, not only the active one.
        var (env, wallet, app) = await UnlockedAsync();
        var first = wallet.ActiveWallet!.Id;
        var second = new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, NBitcoin.WordCount.Twelve).ToString();
        await wallet.CreateWalletAsync("Second", second, ChangeAddressTests.Pin);
        Assert.True(wallet.IsWalletUnlockedInSession(first));

        app.LockNow();

        Assert.False(wallet.IsWalletUnlockedInSession(first));
        Assert.False(wallet.IsWalletUnlockedInSession(wallet.ActiveWallet?.Id ?? "none"));
        Assert.False(wallet.IsUnlocked);
    }

    [Fact]
    public async Task AfterLock_UnlockWithThePinWorksAgain()
    {
        var (_, wallet, app) = await UnlockedAsync();
        var id = wallet.ActiveWallet!.Id;

        app.LockNow();

        Assert.True(await wallet.UnlockWalletAsync(id, ChangeAddressTests.Pin));
        Assert.True(wallet.IsUnlocked);
        Assert.NotNull(wallet.GetPrivateKeyForMultisig());
    }

    [Fact]
    public async Task RequestWatcher_AfterLock_StillSeesARequestPaid()
    {
        // Spec 036: the watcher checks stored request addresses through the explorer, never the keys, so a request paid
        // while the wallet is locked still shows on the lock screen notice (PaidUnopened).
        var (env, wallet, app) = await UnlockedAsync();
        env.Chain.DetailedHistory = true;
        var walletId = wallet.ActiveWallet!.Id;
        var requests = new PaymentRequestService(env.Storage, wallet);
        var request = await requests.CreateAsync(walletId, new NewRequest(5m, "Coffee", null));
        await using var watcher = new RequestWatcher(requests, wallet, env.Chain, new NullJs(), app);
        await watcher.StartAsync();
        await WaitUntilAsync(() => watcher.WalletId == walletId);

        app.LockNow();
        Assert.False(wallet.IsUnlocked);
        env.Chain.Credit(request.Address, 5 * ChangeAddressTests.OneDgb);
        await watcher.CheckNowAsync();

        Assert.Equal(walletId, watcher.WalletId);
        var paid = Assert.Single(watcher.PaidUnopened);
        Assert.Equal(request.Id, paid.Id);
        Assert.Equal(PaymentRequestStatus.Paid, paid.Status);
    }

    [Theory]
    [InlineData("send")]
    [InlineData("batch")]
    [InlineData("consolidate")]
    public async Task LockDuringSend_BeforeBroadcast_FailsAsLockedAndSendsNothing(string kind)
    {
        // A send that is reading coins when the app locks must stop with the "locked" error, not crash, and broadcast nothing.
        var env = new ChangeAddressTests.Env();
        var funded = await env.CreateFundedAsync();
        env.Chain.Credit(env.ReceiveAddress(1), 10 * ChangeAddressTests.OneDgb); // a second coin, so there is something to consolidate
        var walletId = funded.ActiveWallet!.Id;
        var chain = new HoldingChain(env.Chain);
        var wallet = new WalletService(new WalletKeyStore(env.Storage), new ChangeAddressTests.PassThroughCrypto(), chain, new TransactionTracker(env.Storage));
        wallet.SetNetworkMode("mainnet");
        wallet.AddressDiscoverySendWait = TimeSpan.FromMinutes(1);
        Assert.True(await wallet.UnlockWalletAsync(walletId, ChangeAddressTests.Pin));
        var app = NewAppState(wallet);
        app.IsWalletUnlocked = true;

        chain.HoldUtxoReads = true;
        var to = ChangeAddressTests.Stranger();
        var send = kind switch
        {
            "batch" => wallet.SendBatchAsync([(to, 1m)]),
            "consolidate" => wallet.ConsolidateUtxosAsync(),
            _ => wallet.SendAsync(to, 1m),
        };
        await chain.UtxoReadStarted.WaitAsync(TimeSpan.FromSeconds(30));
        app.LockNow();
        chain.ReleaseUtxoReads();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => send);
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(env.Chain.LastBroadcast);
    }

    [Fact]
    public async Task LockDuringBalanceRead_FailsAsLockedInsteadOfCrashing()
    {
        var env = new ChangeAddressTests.Env();
        var funded = await env.CreateFundedAsync();
        var walletId = funded.ActiveWallet!.Id;
        var chain = new HoldingChain(env.Chain);
        var wallet = new WalletService(new WalletKeyStore(env.Storage), new ChangeAddressTests.PassThroughCrypto(), chain, new TransactionTracker(env.Storage));
        wallet.SetNetworkMode("mainnet");
        wallet.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        Assert.True(await wallet.UnlockWalletAsync(walletId, ChangeAddressTests.Pin));
        var app = NewAppState(wallet);
        app.IsWalletUnlocked = true;

        chain.HoldBalanceReads = true;
        var read = wallet.GetBalanceAsync();
        await chain.BalanceReadStarted.WaitAsync(TimeSpan.FromSeconds(30));
        app.LockNow();
        chain.ReleaseBalanceReads();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>Passes everything to <see cref="ChangeAddressTests.FakeChain"/>, and can hold coin reads until released.</summary>
    private sealed class HoldingChain(ChangeAddressTests.FakeChain inner) : IBlockchainService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldUtxoReads { get; set; }
        public Task UtxoReadStarted => _started.Task;
        public void ReleaseUtxoReads() => _release.TrySetResult();

        private async Task<T> Held<T>(Func<Task<T>> read)
        {
            if (HoldUtxoReads)
            {
                _started.TrySetResult();
                await _release.Task;
            }
            return await read();
        }

        public Task<long> GetBalanceAsync(string address) => inner.GetBalanceAsync(address);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => HeldBalance(() => inner.GetBalanceAsync(addresses));

        private readonly TaskCompletionSource _releaseBalance = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _balanceStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldBalanceReads { get; set; }
        public Task BalanceReadStarted => _balanceStarted.Task;
        public void ReleaseBalanceReads() => _releaseBalance.TrySetResult();

        private async Task<long> HeldBalance(Func<Task<long>> read)
        {
            if (HoldBalanceReads)
            {
                _balanceStarted.TrySetResult();
                await _releaseBalance.Task;
            }
            return await read();
        }
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Held(() => inner.GetUtxosAsync(address));
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Held(() => inner.GetUtxosAsync(addresses));
        public Task<List<UtxoInfo>> GetVerifiedUtxosAsync(string address) => Held(() => ((IBlockchainService)inner).GetVerifiedUtxosAsync(address));
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => inner.BroadcastTransactionAsync(rawTransaction);
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => inner.GetTransactionAsync(txId);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => inner.GetAddressTransactionsAsync(address, skip, take);
        public Task<bool> HasAddressHistoryAsync(string address) => inner.HasAddressHistoryAsync(address);
        public Task<decimal> GetFeeRateAsync() => inner.GetFeeRateAsync();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => inner.GetDgbPriceAsync(fiatCurrency);
        public Task<int> GetBlockHeightAsync() => inner.GetBlockHeightAsync();
    }

    private sealed class NullJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }
}
