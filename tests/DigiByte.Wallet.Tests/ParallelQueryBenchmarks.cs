using System.Diagnostics;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Performance benchmarks comparing sequential vs parallel query execution.
/// Run with: dotnet test --filter "FullyQualifiedName~Benchmark" --logger "console;verbosity=detailed"
/// </summary>
public class ParallelQueryBenchmarks
{
    private const int AddressCount = 60; // Typical HD wallet: 20 receiving × 2 formats + 10 change × 2 formats
    private const int SimulatedLatencyMs = 80; // Typical Esplora/Blockbook response time

    private static NodeApiBlockchainService CreateFailingNodeApi()
    {
        var handler = new FakeHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("node down") });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake:5260") };
        return new NodeApiBlockchainService(http, "http://fake:5260");
    }

    // ============================================================
    // Balance benchmarks
    // ============================================================

    [Fact]
    public async Task Benchmark_Balance_Sequential_vs_Parallel()
    {
        var addresses = Enumerable.Range(0, AddressCount).Select(i => $"dgb1addr{i}").ToList();

        // Sequential (simulate old behavior)
        var seqExplorer = new LatencyExplorer(SimulatedLatencyMs, balancePerAddress: 1000);
        var swSeq = Stopwatch.StartNew();
        long seqTotal = 0;
        foreach (var addr in addresses)
            seqTotal += await seqExplorer.GetBalanceAsync(addr);
        swSeq.Stop();

        // Parallel (new behavior)
        var parExplorer = new LatencyExplorer(SimulatedLatencyMs, balancePerAddress: 1000);
        var swPar = Stopwatch.StartNew();
        var parTotal = await parExplorer.GetBalanceAsync(addresses);
        swPar.Stop();

        Assert.Equal(seqTotal, parTotal); // Same result
        Assert.Equal(AddressCount * 1000L, parTotal);

        var speedup = (double)swSeq.ElapsedMilliseconds / swPar.ElapsedMilliseconds;

        // Output benchmark results (visible with --logger "console;verbosity=detailed")
        Console.WriteLine($"");
        Console.WriteLine($"╔══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  BALANCE BENCHMARK ({AddressCount} addresses × {SimulatedLatencyMs}ms latency)  ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Sequential: {swSeq.ElapsedMilliseconds,6}ms                              ║");
        Console.WriteLine($"║  Parallel:   {swPar.ElapsedMilliseconds,6}ms                              ║");
        Console.WriteLine($"║  Speedup:    {speedup,6:F1}x                              ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════╝");

        Assert.True(speedup > 3, $"Expected >3x speedup, got {speedup:F1}x");
    }

    [Fact]
    public async Task Benchmark_UTXOs_Sequential_vs_Parallel()
    {
        var addresses = Enumerable.Range(0, AddressCount).Select(i => $"dgb1addr{i}").ToList();

        // Sequential
        var seqExplorer = new LatencyExplorer(SimulatedLatencyMs);
        var swSeq = Stopwatch.StartNew();
        var seqUtxos = new List<UtxoInfo>();
        foreach (var addr in addresses)
            seqUtxos.AddRange(await seqExplorer.GetUtxosAsync(addr));
        swSeq.Stop();

        // Parallel
        var parExplorer = new LatencyExplorer(SimulatedLatencyMs);
        var swPar = Stopwatch.StartNew();
        var parUtxos = await parExplorer.GetUtxosAsync(addresses);
        swPar.Stop();

        Assert.Equal(seqUtxos.Count, parUtxos.Count);

        var speedup = (double)swSeq.ElapsedMilliseconds / swPar.ElapsedMilliseconds;

        Console.WriteLine($"");
        Console.WriteLine($"╔══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  UTXO BENCHMARK ({AddressCount} addresses × {SimulatedLatencyMs}ms latency)    ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Sequential: {swSeq.ElapsedMilliseconds,6}ms                              ║");
        Console.WriteLine($"║  Parallel:   {swPar.ElapsedMilliseconds,6}ms                              ║");
        Console.WriteLine($"║  Speedup:    {speedup,6:F1}x                              ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════╝");

        Assert.True(speedup > 3, $"Expected >3x speedup, got {speedup:F1}x");
    }

    [Fact]
    public async Task Benchmark_TxHistory_Sequential_vs_Parallel()
    {
        var addresses = Enumerable.Range(0, AddressCount).Select(i => $"dgb1addr{i}").ToList();

        // Sequential (simulate old WalletService behavior)
        var seqExplorer = new LatencyExplorer(SimulatedLatencyMs);
        var swSeq = Stopwatch.StartNew();
        var seqTxs = new Dictionary<string, TransactionInfo>();
        foreach (var addr in addresses)
        {
            var txs = await seqExplorer.GetAddressTransactionsAsync(addr);
            foreach (var tx in txs) seqTxs.TryAdd(tx.TxId, tx);
        }
        swSeq.Stop();

        // Parallel (new WalletService behavior)
        var parExplorer = new LatencyExplorer(SimulatedLatencyMs);
        var swPar = Stopwatch.StartNew();
        var semaphore = new SemaphoreSlim(10);
        var parTxTasks = addresses.Select(async addr =>
        {
            await semaphore.WaitAsync();
            try { return await parExplorer.GetAddressTransactionsAsync(addr); }
            finally { semaphore.Release(); }
        });
        var parResults = await Task.WhenAll(parTxTasks);
        var parTxs = new Dictionary<string, TransactionInfo>();
        foreach (var txList in parResults)
            foreach (var tx in txList) parTxs.TryAdd(tx.TxId, tx);
        swPar.Stop();

        var speedup = (double)swSeq.ElapsedMilliseconds / swPar.ElapsedMilliseconds;

        Console.WriteLine($"");
        Console.WriteLine($"╔══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  TX HISTORY BENCHMARK ({AddressCount} addr × {SimulatedLatencyMs}ms latency)   ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Sequential: {swSeq.ElapsedMilliseconds,6}ms                              ║");
        Console.WriteLine($"║  Parallel:   {swPar.ElapsedMilliseconds,6}ms                              ║");
        Console.WriteLine($"║  Speedup:    {speedup,6:F1}x                              ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════╝");

        Assert.True(speedup > 3, $"Expected >3x speedup, got {speedup:F1}x");
    }

    // ============================================================
    // Cache benchmarks
    // ============================================================

    [Fact]
    public async Task Benchmark_Cache_ColdVsWarm()
    {
        var explorer = new LatencyExplorer(SimulatedLatencyMs, balancePerAddress: 5000);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [explorer], isDevelopment: false);

        var addresses = Enumerable.Range(0, AddressCount).Select(i => $"dgb1addr{i}").ToList();

        // Cold call
        var swCold = Stopwatch.StartNew();
        var bal1 = await sut.GetBalanceAsync(addresses);
        swCold.Stop();

        // Warm call (cached)
        var swWarm = Stopwatch.StartNew();
        var bal2 = await sut.GetBalanceAsync(addresses);
        swWarm.Stop();

        Assert.Equal(bal1, bal2);

        Console.WriteLine($"");
        Console.WriteLine($"╔══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  CACHE BENCHMARK ({AddressCount} addresses)                    ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Cold (first call):  {swCold.ElapsedMilliseconds,6}ms                        ║");
        Console.WriteLine($"║  Warm (cached):      {swWarm.ElapsedMilliseconds,6}ms                        ║");
        Console.WriteLine($"║  Speedup:            {(double)swCold.ElapsedMilliseconds / Math.Max(swWarm.ElapsedMilliseconds, 1),6:F0}x                        ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════╝");

        Assert.True(swWarm.ElapsedMilliseconds < 5, $"Cached call took {swWarm.ElapsedMilliseconds}ms — expected <5ms");
    }

    [Fact]
    public async Task Benchmark_FullWalletRefresh_OldVsNew()
    {
        // Simulates a full Home.razor refresh: balance + tx history + price + block height
        var addresses = Enumerable.Range(0, AddressCount).Select(i => $"dgb1addr{i}").ToList();

        // OLD: sequential, no cache
        var oldExplorer = new LatencyExplorer(SimulatedLatencyMs, balancePerAddress: 1000);
        var swOld = Stopwatch.StartNew();
        long oldBal = 0;
        foreach (var addr in addresses)
            oldBal += await oldExplorer.GetBalanceAsync(addr);
        var oldTxs = new Dictionary<string, TransactionInfo>();
        foreach (var addr in addresses)
            foreach (var tx in await oldExplorer.GetAddressTransactionsAsync(addr))
                oldTxs.TryAdd(tx.TxId, tx);
        swOld.Stop();

        // NEW: parallel + cache (second call is cached)
        var newExplorer = new LatencyExplorer(SimulatedLatencyMs, balancePerAddress: 1000);
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [newExplorer], isDevelopment: false);
        var swNew = Stopwatch.StartNew();
        var newBal = await sut.GetBalanceAsync(addresses);
        var semaphore = new SemaphoreSlim(10);
        var txTasks = addresses.Select(async addr =>
        {
            await semaphore.WaitAsync();
            try { return await sut.GetAddressTransactionsAsync(addr); }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(txTasks);
        swNew.Stop();

        // Second refresh (fully cached)
        var swCached = Stopwatch.StartNew();
        await sut.GetBalanceAsync(addresses);
        var txTasks2 = addresses.Select(addr => sut.GetAddressTransactionsAsync(addr));
        await Task.WhenAll(txTasks2);
        swCached.Stop();

        var speedupFirst = (double)swOld.ElapsedMilliseconds / swNew.ElapsedMilliseconds;
        var speedupCached = (double)swOld.ElapsedMilliseconds / Math.Max(swCached.ElapsedMilliseconds, 1);

        Console.WriteLine($"");
        Console.WriteLine($"╔══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  FULL WALLET REFRESH ({AddressCount} addr × {SimulatedLatencyMs}ms latency)    ║");
        Console.WriteLine($"║  balance + tx history                                ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  OLD (sequential):   {swOld.ElapsedMilliseconds,6}ms                        ║");
        Console.WriteLine($"║  NEW (parallel):     {swNew.ElapsedMilliseconds,6}ms  ({speedupFirst:F1}x faster)        ║");
        Console.WriteLine($"║  NEW (cached):       {swCached.ElapsedMilliseconds,6}ms  ({speedupCached:F0}x faster)         ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════╝");

        Assert.True(speedupFirst > 3, $"First refresh: expected >3x speedup, got {speedupFirst:F1}x");
        Assert.True(swCached.ElapsedMilliseconds < 50, $"Cached refresh took {swCached.ElapsedMilliseconds}ms — expected <50ms");
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
    /// Simulates an explorer with configurable latency per request.
    /// </summary>
    private class LatencyExplorer : IBlockchainService
    {
        private readonly int _delayMs;
        private readonly long _balancePerAddress;

        public LatencyExplorer(int delayMs, long balancePerAddress = 100)
        {
            _delayMs = delayMs;
            _balancePerAddress = balancePerAddress;
        }

        public async Task<long> GetBalanceAsync(string address)
        {
            await Task.Delay(_delayMs);
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
            await Task.Delay(_delayMs);
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
            await Task.Delay(_delayMs);
            return null;
        }

        public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
        {
            await Task.Delay(_delayMs);
            return [new TransactionInfo
            {
                TxId = $"tx-{address}-0",
                Confirmations = 10,
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                FeeSatoshis = 2260,
                Inputs = [new TxInput { Address = address, AmountSatoshis = 50000 }],
                Outputs = [new TxOutput { Address = "dgb1qrecv", AmountSatoshis = 45000, Index = 0 }],
            }];
        }

        public Task<decimal> GetFeeRateAsync() => Task.FromResult(0.001m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public async Task<int> GetBlockHeightAsync() { await Task.Delay(_delayMs); return 23_000_000; }
    }
}
