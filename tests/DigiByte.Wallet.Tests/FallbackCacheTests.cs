using System.Net;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The Fallback read cache (spec 023): shared in-flight reads, failed reads not cached, invalidation while a read is in flight,
/// history cached per address whatever the page size, and the routing of address summaries.
/// </summary>
public class FallbackCacheTests
{
    private sealed class CountingNodeHandler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("node down") });
        }
    }

    /// <summary>An explorer whose reads can be held open, made to fail, and counted.</summary>
    private class ProbeExplorer : IBlockchainService
    {
        public int BalanceCalls, HistoryCalls;
        public long Balance { get; set; } = 1000;
        public int FailNext { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public List<TransactionInfo> History { get; } = [];

        private async Task Wait()
        {
            if (Gate is { } gate) await gate.Task;
            else await Task.Yield();
            if (FailNext > 0) { FailNext--; throw new HttpRequestException("429"); }
        }

        public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
        {
            Interlocked.Increment(ref BalanceCalls);
            await Wait();
            return Balance;
        }

        public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
        {
            Interlocked.Increment(ref HistoryCalls);
            await Wait();
            return History.Skip(skip).Take(take).ToList();
        }

        public Task<long> GetBalanceAsync(string address) => GetBalanceAsync([address]);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("tx");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(1m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }

    private sealed class SummaryProbe : ProbeExplorer, IAddressSummarySource
    {
        public int SummaryCalls;
        public bool FailSummaries { get; set; }

        public async Task<AddressSummary> GetAddressSummaryAsync(string address, bool recordFailure = true)
        {
            Interlocked.Increment(ref SummaryCalls);
            await Task.Yield();
            if (FailSummaries) throw new HttpRequestException("429");
            return new AddressSummary { Address = address, ConfirmedTxCount = 1, ConfirmedFunded = 700, ReadAtUtc = DateTime.UtcNow };
        }

        public Task<AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId) =>
            Task.FromResult(new AddressHistoryPage([], null));
    }

    private static (FallbackBlockchainService Sut, CountingNodeHandler Node) Make(params IBlockchainService[] explorers)
    {
        var node = new CountingNodeHandler();
        var nodeApi = new NodeApiBlockchainService(new HttpClient(node), "http://fake-node:5260", true);
        return (new FallbackBlockchainService(nodeApi, explorers, new HttpClient(), isDevelopment: false), node);
    }

    private static TransactionInfo Tx(int i) => new()
    {
        TxId = $"t{i:000}", Confirmations = 1, Timestamp = DateTime.UtcNow, FeeSatoshis = 0, Inputs = [], Outputs = [],
    };

    // ── In-flight sharing and invalidation (T006) ──

    [Fact]
    public async Task ConcurrentIdenticalBalanceReads_ShareOneRequest()
    {
        var explorer = new ProbeExplorer { Gate = new TaskCompletionSource() };
        var (sut, _) = Make(explorer);
        string[] addresses = ["dgb1qa", "dgb1qb"];

        var first = sut.GetBalanceAsync(addresses);
        var second = sut.GetBalanceAsync(addresses.Reverse());
        explorer.Gate.SetResult();

        Assert.Equal(1000, await first);
        Assert.Equal(1000, await second);
        Assert.Equal(1, explorer.BalanceCalls);
    }

    [Fact]
    public async Task FailedRead_IsNotCached_AndTheNextCallAsksAgain()
    {
        var explorer = new ProbeExplorer { FailNext = 1 };
        var (sut, node) = Make(explorer);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.GetBalanceAsync(["dgb1qa"]));
        var nodeRequestsAfterFirst = node.Requests;

        // The explorer is now on cooldown, so the retry goes to the node: it reached a source instead of a cached failure.
        await Assert.ThrowsAnyAsync<Exception>(() => sut.GetBalanceAsync(["dgb1qa"]));
        Assert.True(node.Requests > nodeRequestsAfterFirst);
    }

    [Fact]
    public async Task InvalidateWhileInFlight_ThatReadIsNotStored_AndTheNextReadGoesToTheNetwork()
    {
        var explorer = new ProbeExplorer { Gate = new TaskCompletionSource(), Balance = 1000 };
        var (sut, _) = Make(explorer);

        var before = sut.GetBalanceAsync(["dgb1qa"]);
        sut.InvalidateCache();                 // a send happens while the read is still out
        explorer.Gate.SetResult();
        Assert.Equal(1000, await before);

        explorer.Gate = null;
        explorer.Balance = 400;
        Assert.Equal(400, await sut.GetBalanceAsync(["dgb1qa"]));
        Assert.Equal(2, explorer.BalanceCalls);
    }

    [Fact]
    public async Task ReadStartedAfterInvalidate_DoesNotJoinTheOlderInFlightRead()
    {
        var explorer = new ProbeExplorer { Gate = new TaskCompletionSource() };
        var (sut, _) = Make(explorer);

        var old = sut.GetBalanceAsync(["dgb1qa"]);
        sut.InvalidateCache();
        var fresh = sut.GetBalanceAsync(["dgb1qa"]);
        explorer.Gate.SetResult();
        await Task.WhenAll(old, fresh);

        Assert.Equal(2, explorer.BalanceCalls);
    }

    // ── History cached per address (T016, FR-003) ──

    [Fact]
    public async Task HistoryReadsWithDifferentPageSizes_FetchOnce_AndSliceCorrectly()
    {
        var explorer = new ProbeExplorer();
        for (var i = 0; i < 60; i++) explorer.History.Add(Tx(i));
        var (sut, _) = Make(explorer);

        var home = await sut.GetAddressTransactionsAsync("dgb1qa", 0, 50);
        var send = await sut.GetAddressTransactionsAsync("dgb1qa", 0, 500);
        var receive = await sut.GetAddressTransactionsAsync("dgb1qa", 0, 1000);
        var middle = await sut.GetAddressTransactionsAsync("dgb1qa", 10, 5);

        Assert.Equal(1, explorer.HistoryCalls);
        Assert.Equal(50, home.Count);
        Assert.Equal(60, send.Count);
        Assert.Equal(60, receive.Count);
        Assert.Equal(["t010", "t011", "t012", "t013", "t014"], middle.Select(t => t.TxId));
    }

    // ── Address summary routing (T013) ──

    [Fact]
    public async Task Summary_RoutesToTheFirstExplorerThatImplementsIt()
    {
        var plain = new ProbeExplorer();
        var summaries = new SummaryProbe();
        var (sut, _) = Make(plain, summaries);

        var summary = await sut.GetAddressSummaryAsync("dgb1qa");

        Assert.Equal(700, summary.Confirmed);
        Assert.Equal(1, summaries.SummaryCalls);
    }

    [Fact]
    public async Task Summary_NoExplorerImplementsIt_IsUnavailable()
    {
        var (sut, _) = Make(new ProbeExplorer());
        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressSummaryAsync("dgb1qa"));
    }

    [Fact]
    public async Task Summary_OnRegtest_IsUnavailable()
    {
        var summaries = new SummaryProbe();
        var (sut, _) = Make(summaries);
        sut.SetNetworkMode("regtest");

        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressSummaryAsync("dgb1qa"));
        Assert.Equal(0, summaries.SummaryCalls);
    }

    [Fact]
    public async Task Summary_AllFail_IsUnavailable_AndTheFailedExplorerCoolsDown()
    {
        var summaries = new SummaryProbe { FailSummaries = true };
        var (sut, _) = Make(summaries);

        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressSummaryAsync("dgb1qa"));
        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressSummaryAsync("dgb1qa"));
        Assert.Equal(1, summaries.SummaryCalls);   // on cooldown after the first failure
    }

    [Fact]
    public async Task Summary_WithoutRecordingFailures_LeavesNoCooldown()
    {
        var summaries = new SummaryProbe { FailSummaries = true };
        var (sut, _) = Make(summaries);

        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressSummaryAsync("dgb1qa", recordFailure: false));
        summaries.FailSummaries = false;
        var summary = await sut.GetAddressSummaryAsync("dgb1qa");

        Assert.Equal(700, summary.Confirmed);
        Assert.Equal(2, summaries.SummaryCalls);
        Assert.False(sut.AllSourcesFailed);
    }

    [Fact]
    public async Task Summary_FirstExplorerFails_TheNextOneAnswers()
    {
        var failing = new SummaryProbe { FailSummaries = true };
        var good = new SummaryProbe();
        var (sut, _) = Make(failing, good);

        Assert.Equal(700, (await sut.GetAddressSummaryAsync("dgb1qa")).Confirmed);
        Assert.Equal(1, good.SummaryCalls);
    }

    [Fact]
    public async Task Summary_WhileTheGatewayIsActiveAndFirst_IsUnavailable_SoTheExplorerIsNotAsked()
    {
        var summaries = new SummaryProbe();
        var node = new CountingNodeHandler();
        var nodeApi = new NodeApiBlockchainService(new HttpClient(node), "http://fake-node:5260", true);
        var gatewayState = new FakeNodeState { State = NodeSourceState.Active };
        var sut = new FallbackBlockchainService(nodeApi, [summaries], new HttpClient(), isDevelopment: false,
            gatewaySource: new ProbeExplorer(), gatewayState: gatewayState, gatewayPosition: GatewayPosition.First);

        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressSummaryAsync("dgb1qa"));
        await Assert.ThrowsAsync<SummaryUnavailableException>(() => sut.GetAddressHistoryPageAsync("dgb1qa", null));
        Assert.Equal(0, summaries.SummaryCalls);
    }

    [Fact]
    public async Task HistoryPage_RoutesToTheSummaryExplorer()
    {
        var summaries = new SummaryProbe();
        var (sut, _) = Make(summaries);

        var page = await sut.GetAddressHistoryPageAsync("dgb1qa", null);

        Assert.Empty(page.Transactions);
    }
}
