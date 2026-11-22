using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A history load reports its own completeness (spec 025, FR-011, FR-015, T007). The older
/// <see cref="WalletService.LastHistoryIncomplete"/> is one property shared by every call, so with two overlapping loads a
/// failed one could be read as complete; and a load made before address discovery finishes can be empty without the wallet
/// being empty.
/// </summary>
public class HistoryLoadTests
{
    private static readonly Network Network = ChangeAddressTests.Network;
    private const string Pin = ChangeAddressTests.Pin;

    /// <summary>
    /// The scripted explorer behind switches for the two kinds of read a history load makes when change-only sync is off:
    /// "has this address been used" (address discovery) and "this address's transactions" (the history itself).
    /// </summary>
    private sealed class GatedChain(SummaryExplorer inner) : IBlockchainService
    {
        public Task? HoldAddressTxs { get; set; }
        public bool FailAddressTxs { get; set; }
        public Task? HoldUsedLookups { get; set; }
        public bool FailUsedLookups { get; set; }
        private int _addressTxCalls;
        public int AddressTxCalls => _addressTxCalls;

        public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
        {
            Interlocked.Increment(ref _addressTxCalls);
            if (HoldAddressTxs is { } hold) await hold;
            if (FailAddressTxs) throw new HttpRequestException("503 Service Unavailable");
            return await inner.GetAddressTransactionsAsync(address, skip, take);
        }

        public async Task<bool> HasAddressHistoryAsync(string address)
        {
            if (HoldUsedLookups is { } hold) await hold;
            if (FailUsedLookups) throw new HttpRequestException("explorer is down");
            return await inner.HasAddressHistoryAsync(address);
        }

        public Task<long> GetBalanceAsync(string address) => inner.GetBalanceAsync(address);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => inner.GetBalanceAsync(addresses);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => inner.GetUtxosAsync(address);
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => inner.GetUtxosAsync(addresses);
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => inner.BroadcastTransactionAsync(rawTransaction);
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => inner.GetTransactionAsync(txId);
        public Task<decimal> GetFeeRateAsync() => inner.GetFeeRateAsync();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => inner.GetDgbPriceAsync(fiatCurrency);
        public Task<int> GetBlockHeightAsync() => inner.GetBlockHeightAsync();
    }

    private sealed class Env
    {
        public HistorySyncEnv Inner { get; } = new();
        public GatedChain Chain { get; }
        public WalletService Service { get; }

        public Env()
        {
            // Change-only sync off: discovery and history then go through the two gated calls, with nothing shared between loads.
            Inner.Options.ChangeOnly = false;
            Chain = new GatedChain(Inner.Explorer);
            Service = Inner.NewService(Chain);
        }

        public Task CreateSeedWalletAsync() => Service.CreateWalletAsync("Test", Inner.Words, Pin);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition(), "the load never reached the explorer");
    }

    [Fact]
    public async Task ALoadThatReadsTheChain_IsComplete_AndSettled()
    {
        var env = new Env();
        await env.CreateSeedWalletAsync();
        env.Inner.Explorer.Pay(env.Inner.Receive(0), 100_000_000);

        var load = await env.Service.LoadTransactionHistoryAsync();

        Assert.True(load.Complete);
        Assert.True(load.AddressesSettled);
        var tx = Assert.Single(load.Transactions);
        Assert.Equal(TransactionDirection.Received, tx.Direction);
        Assert.Equal(100_000_000, tx.AmountSatoshis);
    }

    [Fact]
    public async Task ALoadWhoseChainReadFails_IsNotComplete_AndReturnsOnlyWhatIsTrackedLocally()
    {
        var env = new Env();
        await env.CreateSeedWalletAsync();
        env.Inner.Explorer.Pay(env.Inner.Receive(0), 100_000_000);
        await env.Service.LoadTransactionHistoryAsync(); // settles address discovery
        env.Chain.FailAddressTxs = true;

        var load = await env.Service.LoadTransactionHistoryAsync();

        Assert.False(load.Complete);
        Assert.Empty(load.Transactions);
    }

    /// <summary>
    /// The failure that made this method necessary: a failed load is still in flight when a second load starts and succeeds.
    /// With one shared flag, reset by the second load, the first caller read its failed load as complete.
    /// </summary>
    [Fact]
    public async Task OverlappingLoads_FirstFails_SecondSucceeds_EachGetsItsOwnResult()
    {
        var env = new Env();
        await env.CreateSeedWalletAsync();
        env.Inner.Explorer.Pay(env.Inner.Receive(0), 100_000_000);
        await env.Service.LoadTransactionHistoryAsync();

        var release = new TaskCompletionSource();
        var before = env.Chain.AddressTxCalls;
        env.Chain.HoldAddressTxs = release.Task;
        var first = env.Service.LoadTransactionHistoryAsync();
        await WaitUntilAsync(() => env.Chain.AddressTxCalls > before);

        env.Chain.HoldAddressTxs = null;
        var second = await env.Service.LoadTransactionHistoryAsync();

        env.Chain.FailAddressTxs = true;
        release.SetResult();
        var firstResult = await first;

        Assert.True(second.Complete);
        Assert.Single(second.Transactions);
        Assert.False(firstResult.Complete);
        Assert.Empty(firstResult.Transactions);
    }

    [Fact]
    public async Task OverlappingLoads_FirstSucceeds_SecondFails_EachGetsItsOwnResult()
    {
        var env = new Env();
        await env.CreateSeedWalletAsync();
        env.Inner.Explorer.Pay(env.Inner.Receive(0), 100_000_000);
        await env.Service.LoadTransactionHistoryAsync();

        var release = new TaskCompletionSource();
        var before = env.Chain.AddressTxCalls;
        env.Chain.HoldAddressTxs = release.Task;
        var first = env.Service.LoadTransactionHistoryAsync();
        await WaitUntilAsync(() => env.Chain.AddressTxCalls > before);

        env.Chain.HoldAddressTxs = null;
        env.Chain.FailAddressTxs = true;
        var second = await env.Service.LoadTransactionHistoryAsync();

        env.Chain.FailAddressTxs = false;
        release.SetResult();
        var firstResult = await first;

        Assert.False(second.Complete);
        Assert.True(firstResult.Complete);
        Assert.Single(firstResult.Transactions);
    }

    /// <summary>An early load of a recovered wallet can be empty while its used addresses are still being found.</summary>
    [Fact]
    public async Task WhileAddressDiscoveryIsStillRunning_TheLoadIsComplete_ButNotSettled()
    {
        var env = new Env();
        var release = new TaskCompletionSource();
        env.Chain.HoldUsedLookups = release.Task;
        env.Service.AddressDiscoveryWait = TimeSpan.FromMilliseconds(20);
        await env.CreateSeedWalletAsync();

        var early = await env.Service.LoadTransactionHistoryAsync();

        Assert.True(early.Complete);
        Assert.False(early.AddressesSettled);

        env.Chain.HoldUsedLookups = null;
        release.SetResult();
        env.Service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        var later = await env.Service.LoadTransactionHistoryAsync();

        Assert.True(later.Complete);
        Assert.True(later.AddressesSettled);
    }

    [Fact]
    public async Task WhenAddressDiscoveryFailed_TheLoadIsNotSettled()
    {
        var env = new Env();
        env.Chain.FailUsedLookups = true;
        await env.CreateSeedWalletAsync();

        var load = await env.Service.LoadTransactionHistoryAsync();

        Assert.True(load.Complete); // the history itself was read
        Assert.False(load.AddressesSettled);
    }

    /// <summary>A watch-only wallet has an HD key and address discovery like a seed wallet, so it follows the same rule.</summary>
    [Fact]
    public async Task AWatchOnlyWallet_FollowsTheSameDiscoveryRule()
    {
        var env = new Env();
        var release = new TaskCompletionSource();
        env.Chain.HoldUsedLookups = release.Task;
        env.Service.AddressDiscoveryWait = TimeSpan.FromMilliseconds(20);
        var xpub = env.Inner.Hd.GetAccountExtPubKey().ToString(Network);
        await env.Service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);

        var early = await env.Service.LoadTransactionHistoryAsync();
        Assert.True(early.Complete);
        Assert.False(early.AddressesSettled);

        env.Chain.HoldUsedLookups = null;
        release.SetResult();
        env.Service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        var later = await env.Service.LoadTransactionHistoryAsync();
        Assert.True(later.AddressesSettled);
    }

    /// <summary>A private-key wallet has one fixed address and no discovery, so it is settled from the start.</summary>
    [Fact]
    public async Task APrivateKeyWallet_IsAlwaysSettled()
    {
        var env = new Env();
        env.Chain.FailUsedLookups = true; // would fail discovery, if there were any
        await env.Service.CreateWalletFromWifAsync("Wif", new Key().GetWif(Network).ToString(), Pin);

        var load = await env.Service.LoadTransactionHistoryAsync();

        Assert.True(load.Complete);
        Assert.True(load.AddressesSettled);
    }

    // ── With change-only sync on (the default): a summary read that fails ──

    /// <summary>
    /// A chain source that gives address summaries, as the real explorers do. When its summaries fail it behaves like the
    /// real stack: the summary route reports "no explorer could give this address's summary" with the cause attached, and
    /// the per-address transaction reader returns an empty list for an address it could not read.
    /// </summary>
    private sealed class SummaryChain(SummaryExplorer inner) : IBlockchainService, IAddressSummarySource
    {
        public bool SummariesFail { get; set; }
        public bool SummariesNotOnThisRoute { get; set; }

        private void Check()
        {
            if (SummariesNotOnThisRoute) throw new SummaryUnavailableException("Address summaries are not available on this network.");
            if (SummariesFail)
                throw new SummaryUnavailableException("No explorer could give this address's summary.", new HttpRequestException("Failed to fetch"));
        }

        public Task<AddressSummary> GetAddressSummaryAsync(string address, bool recordFailure = true)
        {
            Check();
            return inner.GetAddressSummaryAsync(address, recordFailure);
        }

        public Task<AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId)
        {
            Check();
            return inner.GetAddressHistoryPageAsync(address, afterTxId);
        }

        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
            SummariesFail ? Task.FromResult(new List<TransactionInfo>()) : inner.GetAddressTransactionsAsync(address, skip, take);

        public Task<bool> HasAddressHistoryAsync(string address) => inner.HasAddressHistoryAsync(address);
        public Task<long> GetBalanceAsync(string address) => inner.GetBalanceAsync(address);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => inner.GetBalanceAsync(addresses);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => inner.GetUtxosAsync(address);
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => inner.GetUtxosAsync(addresses);
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => inner.BroadcastTransactionAsync(rawTransaction);
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => inner.GetTransactionAsync(txId);
        public Task<decimal> GetFeeRateAsync() => inner.GetFeeRateAsync();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => inner.GetDgbPriceAsync(fiatCurrency);
        public Task<int> GetBlockHeightAsync() => inner.GetBlockHeightAsync();
    }

    private static (HistorySyncEnv Env, SummaryChain Chain, WalletService Service) ChangeOnlyEnv()
    {
        var env = new HistorySyncEnv();
        var chain = new SummaryChain(env.Explorer);
        var sync = new WalletHistorySync(chain, env.Store, env.Clock) { FullResyncRetryDelay = TimeSpan.FromMinutes(1) };
        var service = new WalletService(new DigiByte.Wallet.Storage.WalletKeyStore(env.Storage), new ChangeAddressTests.PassThroughCrypto(),
            chain, new TransactionTracker(env.Storage), historySync: sync, historyOptions: env.Options);
        service.SetNetworkMode("mainnet");
        service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        return (env, chain, service);
    }

    /// <summary>
    /// Found in the browser with the explorer unreachable: the summary read failed, the wallet fell back to reading each
    /// address in full, that reader answered "no transactions" for every address it could not reach, and the load was
    /// reported complete and empty. For a recovered wallet that would record "started empty" and celebrate later.
    /// </summary>
    [Fact]
    public async Task WhenTheSummaryReadFails_TheLoadIsNotComplete_EvenThoughThePerAddressFallbackAnswers()
    {
        var (env, chain, service) = ChangeOnlyEnv();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Explorer.Pay(env.Receive(0), 100_000_000);
        var first = await service.LoadTransactionHistoryAsync();
        Assert.True(first.Complete);
        Assert.Single(first.Transactions);

        chain.SummariesFail = true;
        env.Clock.Advance(TimeSpan.FromMinutes(10)); // past any reuse of the earlier summaries

        var offline = await service.LoadTransactionHistoryAsync();

        Assert.False(offline.Complete);
    }

    [Fact]
    public async Task WhenTheSummaryReadFails_OnTheFirstLoad_AnEmptyAnswerIsNotComplete()
    {
        var (env, chain, service) = ChangeOnlyEnv();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Explorer.Pay(env.Receive(0), 100_000_000);
        chain.SummariesFail = true;

        var offline = await service.LoadTransactionHistoryAsync();

        Assert.False(offline.Complete);
        Assert.Empty(offline.Transactions);
        Assert.Empty(await service.GetTransactionHistoryAsync());
        Assert.True(service.LastHistoryIncomplete);
    }

    /// <summary>Where summaries do not exist at all (regtest, the gateway answering first), reading each address in full is the normal path.</summary>
    [Fact]
    public async Task WhenSummariesDoNotExistOnThisRoute_ThePerAddressRead_IsStillComplete()
    {
        var (env, chain, service) = ChangeOnlyEnv();
        chain.SummariesNotOnThisRoute = true;
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Explorer.Pay(env.Receive(0), 100_000_000);

        var load = await service.LoadTransactionHistoryAsync();

        Assert.True(load.Complete);
        Assert.Single(load.Transactions);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_ReturnsTheSameList_AndStillSetsLastHistoryIncomplete()
    {
        var env = new Env();
        await env.CreateSeedWalletAsync();
        env.Inner.Explorer.Pay(env.Inner.Receive(0), 100_000_000);

        var list = await env.Service.GetTransactionHistoryAsync();
        Assert.False(env.Service.LastHistoryIncomplete);
        var load = await env.Service.LoadTransactionHistoryAsync();
        Assert.Equal(load.Transactions.Select(t => t.TxId), list.Select(t => t.TxId));

        env.Chain.FailAddressTxs = true;
        Assert.Empty(await env.Service.GetTransactionHistoryAsync());
        Assert.True(env.Service.LastHistoryIncomplete);

        env.Chain.FailAddressTxs = false;
        Assert.Single(await env.Service.GetTransactionHistoryAsync());
        Assert.False(env.Service.LastHistoryIncomplete);
    }
}
