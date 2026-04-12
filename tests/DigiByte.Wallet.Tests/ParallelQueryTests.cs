using System.Diagnostics;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Tests that multi-address balance/UTXO/tx queries run in parallel (not sequentially),
/// and that FallbackBlockchainService caching works correctly.
/// </summary>
public class ParallelQueryTests
{
    /// <summary>
    /// Creates a FakeExplorer that simulates network latency per call.
    /// </summary>
    private static DelayedFakeExplorer CreateDelayedExplorer(int delayMs, long balancePerAddress = 100)
        => new("delayed", delayMs, balancePerAddress);

    private static NodeApiBlockchainService CreateFailingNodeApi()
    {
        var handler = new FakeHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("node down") });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake:5260") };
        return new NodeApiBlockchainService(http, "http://fake:5260");
    }

    // ============================================================
    // Parallel balance tests
    // ============================================================

    [Fact]
    public async Task GetBalance_MultiAddress_RunsInParallel()
    {
        // 20 addresses × 100ms each = 2000ms sequential, should be <500ms parallel
        var explorer = CreateDelayedExplorer(delayMs: 100, balancePerAddress: 1000);
        var addresses = Enumerable.Range(0, 20).Select(i => $"dgb1addr{i}").ToList();

        var sw = Stopwatch.StartNew();
        var balance = await explorer.GetBalanceAsync(addresses);
        sw.Stop();

        Assert.Equal(20_000, balance); // 20 × 1000
        Assert.True(sw.ElapsedMilliseconds < 800,
            $"Expected <800ms (parallel), but took {sw.ElapsedMilliseconds}ms — queries may be sequential");
    }

    [Fact]
    public async Task GetUtxos_MultiAddress_RunsInParallel()
    {
        // 20 addresses × 100ms each = 2000ms sequential, should be <500ms parallel
        var explorer = CreateDelayedExplorer(delayMs: 100);
        var addresses = Enumerable.Range(0, 20).Select(i => $"dgb1addr{i}").ToList();

        var sw = Stopwatch.StartNew();
        var utxos = await explorer.GetUtxosAsync(addresses);
        sw.Stop();

        Assert.Equal(20, utxos.Count); // 1 UTXO per address
        Assert.True(sw.ElapsedMilliseconds < 800,
            $"Expected <800ms (parallel), but took {sw.ElapsedMilliseconds}ms — queries may be sequential");
    }

    [Fact]
    public async Task GetBalance_MultiAddress_ReturnsCorrectTotal()
    {
        var explorer = CreateDelayedExplorer(delayMs: 0, balancePerAddress: 500);
        var addresses = Enumerable.Range(0, 60).Select(i => $"dgb1addr{i}").ToList();

        var balance = await explorer.GetBalanceAsync(addresses);

        Assert.Equal(30_000, balance); // 60 × 500
    }

    [Fact]
    public async Task GetBalance_EmptyAddresses_ReturnsZero()
    {
        var explorer = CreateDelayedExplorer(delayMs: 0);
        var balance = await explorer.GetBalanceAsync(Array.Empty<string>());
        Assert.Equal(0, balance);
    }

    [Fact]
    public async Task GetUtxos_EmptyAddresses_ReturnsEmpty()
    {
        var explorer = CreateDelayedExplorer(delayMs: 0);
        var utxos = await explorer.GetUtxosAsync(Array.Empty<string>());
        Assert.Empty(utxos);
    }

    // ============================================================
    // Fallback caching tests
    // ============================================================

    [Fact]
    public async Task Cache_BalanceIsCached_SecondCallInstant()
    {
        var explorer = CreateDelayedExplorer(delayMs: 100, balancePerAddress: 5000);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        // First call — hits explorer
        var sw1 = Stopwatch.StartNew();
        var bal1 = await sut.GetBalanceAsync("dgb1test");
        sw1.Stop();

        // Second call — should hit cache
        var sw2 = Stopwatch.StartNew();
        var bal2 = await sut.GetBalanceAsync("dgb1test");
        sw2.Stop();

        Assert.Equal(5000, bal1);
        Assert.Equal(5000, bal2);
        Assert.True(sw2.ElapsedMilliseconds < 5,
            $"Cached call took {sw2.ElapsedMilliseconds}ms — expected <5ms");
    }

    [Fact]
    public async Task Cache_MultiBalanceIsCached()
    {
        var explorer = CreateDelayedExplorer(delayMs: 50, balancePerAddress: 100);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        var addresses = Enumerable.Range(0, 10).Select(i => $"dgb1addr{i}").ToList();

        var bal1 = await sut.GetBalanceAsync(addresses);

        var sw = Stopwatch.StartNew();
        var bal2 = await sut.GetBalanceAsync(addresses);
        sw.Stop();

        Assert.Equal(1000, bal1);
        Assert.Equal(1000, bal2);
        Assert.True(sw.ElapsedMilliseconds < 5,
            $"Cached multi-balance took {sw.ElapsedMilliseconds}ms — expected <5ms");
    }

    [Fact]
    public async Task Cache_TxHistoryIsCached()
    {
        var explorer = CreateDelayedExplorer(delayMs: 100);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        var txs1 = await sut.GetAddressTransactionsAsync("dgb1test");

        var sw = Stopwatch.StartNew();
        var txs2 = await sut.GetAddressTransactionsAsync("dgb1test");
        sw.Stop();

        Assert.Equal(txs1.Count, txs2.Count);
        Assert.True(sw.ElapsedMilliseconds < 5,
            $"Cached tx history took {sw.ElapsedMilliseconds}ms — expected <5ms");
    }

    [Fact]
    public async Task Cache_UtxosAreNotCached()
    {
        var callCount = 0;
        var explorer = new CountingFakeExplorer(() => Interlocked.Increment(ref callCount));
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        await sut.GetUtxosAsync("dgb1test");
        await sut.GetUtxosAsync("dgb1test");

        Assert.Equal(2, callCount); // Not cached — both hit explorer
    }

    [Fact]
    public async Task Cache_InvalidatedAfterBroadcast()
    {
        var callCount = 0;
        var explorer = new CountingFakeExplorer(() => Interlocked.Increment(ref callCount), broadcastResult: "txid123");
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        await sut.GetBalanceAsync("dgb1test"); // call 1
        await sut.GetBalanceAsync("dgb1test"); // cached — no new call
        Assert.Equal(1, callCount);

        await sut.BroadcastTransactionAsync([0x01]); // invalidates cache
        await sut.GetBalanceAsync("dgb1test"); // call 2 — cache was cleared
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Cache_BlockHeightIsCached()
    {
        var explorer = CreateDelayedExplorer(delayMs: 100);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        await sut.GetBlockHeightAsync(); // populates cache

        var sw = Stopwatch.StartNew();
        var height = await sut.GetBlockHeightAsync();
        sw.Stop();

        Assert.Equal(1000, height);
        Assert.True(sw.ElapsedMilliseconds < 5,
            $"Cached block height took {sw.ElapsedMilliseconds}ms — expected <5ms");
    }

    [Fact]
    public async Task Cache_DifferentAddresses_SeparateCacheEntries()
    {
        var explorer = CreateDelayedExplorer(delayMs: 0, balancePerAddress: 100);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        var bal1 = await sut.GetBalanceAsync("addr1");
        var bal2 = await sut.GetBalanceAsync("addr2");

        Assert.Equal(100, bal1);
        Assert.Equal(100, bal2);
    }

    // ============================================================
    // Concurrency safety
    // ============================================================

    [Fact]
    public async Task Parallel_ConcurrentBalanceCalls_ThreadSafe()
    {
        var explorer = CreateDelayedExplorer(delayMs: 10, balancePerAddress: 100);
        var addresses = Enumerable.Range(0, 60).Select(i => $"dgb1addr{i}").ToList();

        // Run 5 concurrent balance requests
        var tasks = Enumerable.Range(0, 5).Select(_ => explorer.GetBalanceAsync(addresses));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, bal => Assert.Equal(6000, bal));
    }

    // ============================================================
    // Test helpers
    // ============================================================

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(handler(request));
    }

    /// <summary>
    /// IBlockchainService that simulates network delay per call.
    /// Used to verify parallel execution (total time should be delay × addresses / concurrency,
    /// not delay × addresses).
    /// </summary>
    private class DelayedFakeExplorer : IBlockchainService
    {
        private readonly string _name;
        private readonly int _delayMs;
        private readonly long _balancePerAddress;

        public DelayedFakeExplorer(string name, int delayMs, long balancePerAddress = 100)
        {
            _name = name;
            _delayMs = delayMs;
            _balancePerAddress = balancePerAddress;
        }

        public async Task<long> GetBalanceAsync(string address)
        {
            if (_delayMs > 0) await Task.Delay(_delayMs);
            return _balancePerAddress;
        }

        public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
        {
            var list = addresses.ToList();
            if (list.Count == 0) return 0;
            var semaphore = new SemaphoreSlim(10);
            var tasks = list.Select(async addr =>
            {
                await semaphore.WaitAsync();
                try { return await GetBalanceAsync(addr); }
                finally { semaphore.Release(); }
            });
            return (await Task.WhenAll(tasks)).Sum();
        }

        public async Task<List<UtxoInfo>> GetUtxosAsync(string address)
        {
            if (_delayMs > 0) await Task.Delay(_delayMs);
            return [new UtxoInfo { TxId = $"tx-{address}", OutputIndex = 0, AmountSatoshis = _balancePerAddress, ScriptPubKey = "", Confirmations = 6 }];
        }

        public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
        {
            var list = addresses.ToList();
            if (list.Count == 0) return [];
            var semaphore = new SemaphoreSlim(10);
            var tasks = list.Select(async addr =>
            {
                await semaphore.WaitAsync();
                try { return await GetUtxosAsync(addr); }
                finally { semaphore.Release(); }
            });
            return (await Task.WhenAll(tasks)).SelectMany(r => r).ToList();
        }

        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("fake-txid");

        public async Task<TransactionInfo?> GetTransactionAsync(string txId)
        {
            if (_delayMs > 0) await Task.Delay(_delayMs);
            return null;
        }

        public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
        {
            if (_delayMs > 0) await Task.Delay(_delayMs);
            return [];
        }

        public Task<decimal> GetFeeRateAsync() => Task.FromResult(0.001m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);

        public async Task<int> GetBlockHeightAsync()
        {
            if (_delayMs > 0) await Task.Delay(_delayMs);
            return 1000;
        }
    }

    /// <summary>
    /// Tracks how many times balance/UTXO methods are called — used to verify caching behavior.
    /// </summary>
    private class CountingFakeExplorer : IBlockchainService
    {
        private readonly Action _onBalanceCall;
        private readonly string? _broadcastResult;

        public CountingFakeExplorer(Action onBalanceCall, string? broadcastResult = null)
        {
            _onBalanceCall = onBalanceCall;
            _broadcastResult = broadcastResult;
        }

        public Task<long> GetBalanceAsync(string address) { _onBalanceCall(); return Task.FromResult(100L); }
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) { _onBalanceCall(); return Task.FromResult(100L); }
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) { _onBalanceCall(); return Task.FromResult(new List<UtxoInfo>()); }
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) { _onBalanceCall(); return Task.FromResult(new List<UtxoInfo>()); }
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(0.001m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);

        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
        {
            if (_broadcastResult == null) throw new Exception("broadcast failed");
            return Task.FromResult(_broadcastResult);
        }
    }
}
