using System.Diagnostics;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Live benchmarks comparing Esplora (digiexplorer.info) vs Blockbook (digibyteblockexplorer.com).
/// These tests hit real APIs — they require internet access and may be slow.
/// 
/// Run with: dotnet test --filter "FullyQualifiedName~LiveExplorerBenchmarks" --logger "console;verbosity=detailed"
/// </summary>
public class LiveExplorerBenchmarks
{
    // Known mainnet legacy addresses (both Esplora and Blockbook accept D... format)
    // Note: digibyteblockexplorer.com runs DigiByte 7.17.2 which doesn't support bech32/SegWit (dgb1...) addresses
    private static readonly string[] TestAddresses =
    [
        "DNcTFEGrhJ4RyfZYG3eVfm3BjLHkQSAi5F",
        "DEyHcgHBSseXbcfCxyUASvNMF9BVLAEAUi",
        "DK8ZTp89aahJpmnwdZ8LNsAUNoGA6qS43G",
        "DGox32UN27ZvZyJBSZajtsJqWUcjZttr9w",
        "DBGWprSb8J55rQa9o21ptTnR6Zehm8k4mV",
    ];

    // Simulate HD wallet — 20 addresses
    private static readonly string[] HdAddresses = Enumerable.Range(0, 20)
        .Select(i => i < TestAddresses.Length ? TestAddresses[i] : TestAddresses[i % TestAddresses.Length])
        .ToArray();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "DigiByte-Wallet-Benchmark/1.0");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static BlockchainApiService CreateEsplora()
        => new(CreateHttpClient(), isTestnet: false);

    private static BlockbookApiService CreateBlockbook()
        => new(CreateHttpClient(), "https://digibyteblockexplorer.com", "blockbook-main");

    // ============================================================
    // Single-address benchmarks
    // ============================================================

    [Fact]
    public async Task Live_SingleAddress_Balance()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();
        var addr = TestAddresses[0];

        // Warm up (DNS, TLS)
        await esplora.GetBalanceAsync(addr);
        await blockbook.GetBalanceAsync(addr);

        const int rounds = 5;
        var esploraTimes = new List<long>();
        var blockbookTimes = new List<long>();

        for (int i = 0; i < rounds; i++)
        {
            var sw1 = Stopwatch.StartNew();
            await esplora.GetBalanceAsync(addr);
            sw1.Stop();
            esploraTimes.Add(sw1.ElapsedMilliseconds);

            var sw2 = Stopwatch.StartNew();
            await blockbook.GetBalanceAsync(addr);
            sw2.Stop();
            blockbookTimes.Add(sw2.ElapsedMilliseconds);
        }

        PrintResults("SINGLE ADDRESS BALANCE", esploraTimes, blockbookTimes);
    }

    [Fact]
    public async Task Live_SingleAddress_UTXOs()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();
        var addr = TestAddresses[0];

        await esplora.GetUtxosAsync(addr);
        await blockbook.GetUtxosAsync(addr);

        const int rounds = 5;
        var esploraTimes = new List<long>();
        var blockbookTimes = new List<long>();

        for (int i = 0; i < rounds; i++)
        {
            var sw1 = Stopwatch.StartNew();
            await esplora.GetUtxosAsync(addr);
            sw1.Stop();
            esploraTimes.Add(sw1.ElapsedMilliseconds);

            var sw2 = Stopwatch.StartNew();
            await blockbook.GetUtxosAsync(addr);
            sw2.Stop();
            blockbookTimes.Add(sw2.ElapsedMilliseconds);
        }

        PrintResults("SINGLE ADDRESS UTXOs", esploraTimes, blockbookTimes);
    }

    [Fact]
    public async Task Live_SingleAddress_TxHistory()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();
        var addr = TestAddresses[1]; // Legacy addr, likely has more history

        await esplora.GetAddressTransactionsAsync(addr);
        await blockbook.GetAddressTransactionsAsync(addr);

        const int rounds = 3;
        var esploraTimes = new List<long>();
        var blockbookTimes = new List<long>();

        for (int i = 0; i < rounds; i++)
        {
            var sw1 = Stopwatch.StartNew();
            var esTxs = await esplora.GetAddressTransactionsAsync(addr);
            sw1.Stop();
            esploraTimes.Add(sw1.ElapsedMilliseconds);

            var sw2 = Stopwatch.StartNew();
            var bbTxs = await blockbook.GetAddressTransactionsAsync(addr);
            sw2.Stop();
            blockbookTimes.Add(sw2.ElapsedMilliseconds);

            Console.WriteLine($"  Round {i + 1}: Esplora returned {esTxs.Count} txs, Blockbook returned {bbTxs.Count} txs");
        }

        PrintResults("SINGLE ADDRESS TX HISTORY", esploraTimes, blockbookTimes);
    }

    // ============================================================
    // Multi-address benchmarks (HD wallet simulation)
    // ============================================================

    [Fact]
    public async Task Live_MultiAddress_Balance_Sequential()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();

        // Warm up
        await esplora.GetBalanceAsync(TestAddresses[0]);
        await blockbook.GetBalanceAsync(TestAddresses[0]);

        // Sequential — old behavior
        var sw1 = Stopwatch.StartNew();
        long esBal = 0;
        foreach (var addr in HdAddresses)
            esBal += await esplora.GetBalanceAsync(addr);
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        long bbBal = 0;
        foreach (var addr in HdAddresses)
            bbBal += await blockbook.GetBalanceAsync(addr);
        sw2.Stop();

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  MULTI-ADDRESS BALANCE — SEQUENTIAL ({HdAddresses.Length} addresses)       ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Esplora  (digiexplorer.info):     {sw1.ElapsedMilliseconds,6}ms               ║");
        Console.WriteLine($"║  Blockbook (digibyteblockexplorer): {sw2.ElapsedMilliseconds,6}ms               ║");
        Console.WriteLine($"║  Winner: {(sw1.ElapsedMilliseconds < sw2.ElapsedMilliseconds ? "Esplora" : "Blockbook"),-10}                                        ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
    }

    [Fact]
    public async Task Live_MultiAddress_Balance_Parallel()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();

        // Warm up
        await esplora.GetBalanceAsync(TestAddresses[0]);
        await blockbook.GetBalanceAsync(TestAddresses[0]);

        // Parallel — new behavior
        var sw1 = Stopwatch.StartNew();
        var esBal = await esplora.GetBalanceAsync(HdAddresses);
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        var bbBal = await blockbook.GetBalanceAsync(HdAddresses);
        sw2.Stop();

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  MULTI-ADDRESS BALANCE — PARALLEL ({HdAddresses.Length} addresses)         ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Esplora  (digiexplorer.info):     {sw1.ElapsedMilliseconds,6}ms               ║");
        Console.WriteLine($"║  Blockbook (digibyteblockexplorer): {sw2.ElapsedMilliseconds,6}ms               ║");
        Console.WriteLine($"║  Winner: {(sw1.ElapsedMilliseconds < sw2.ElapsedMilliseconds ? "Esplora" : "Blockbook"),-10}                                        ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
    }

    [Fact]
    public async Task Live_MultiAddress_UTXOs_Parallel()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();

        await esplora.GetUtxosAsync(TestAddresses[0]);
        await blockbook.GetUtxosAsync(TestAddresses[0]);

        var sw1 = Stopwatch.StartNew();
        var esUtxos = await esplora.GetUtxosAsync(HdAddresses);
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        var bbUtxos = await blockbook.GetUtxosAsync(HdAddresses);
        sw2.Stop();

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  MULTI-ADDRESS UTXOs — PARALLEL ({HdAddresses.Length} addresses)           ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Esplora  ({esUtxos.Count,3} utxos):               {sw1.ElapsedMilliseconds,6}ms               ║");
        Console.WriteLine($"║  Blockbook ({bbUtxos.Count,3} utxos):               {sw2.ElapsedMilliseconds,6}ms               ║");
        Console.WriteLine($"║  Winner: {(sw1.ElapsedMilliseconds < sw2.ElapsedMilliseconds ? "Esplora" : "Blockbook"),-10}                                        ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
    }

    // ============================================================
    // Block height / metadata
    // ============================================================

    [Fact]
    public async Task Live_BlockHeight()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();

        // Warm up
        await esplora.GetBlockHeightAsync();
        await blockbook.GetBlockHeightAsync();

        const int rounds = 5;
        var esploraTimes = new List<long>();
        var blockbookTimes = new List<long>();

        for (int i = 0; i < rounds; i++)
        {
            var sw1 = Stopwatch.StartNew();
            var esHeight = await esplora.GetBlockHeightAsync();
            sw1.Stop();
            esploraTimes.Add(sw1.ElapsedMilliseconds);

            var sw2 = Stopwatch.StartNew();
            var bbHeight = await blockbook.GetBlockHeightAsync();
            sw2.Stop();
            blockbookTimes.Add(sw2.ElapsedMilliseconds);

            if (i == 0)
                Console.WriteLine($"  Block heights: Esplora={esHeight}, Blockbook={bbHeight}");
        }

        PrintResults("BLOCK HEIGHT", esploraTimes, blockbookTimes);
    }

    [Fact]
    public async Task Live_FeeEstimate()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();

        var sw1 = Stopwatch.StartNew();
        var esFee = await esplora.GetFeeRateAsync();
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        var bbFee = await blockbook.GetFeeRateAsync();
        sw2.Stop();

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  FEE ESTIMATE                                                ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Esplora:  {esFee:F8} DGB/kB  ({sw1.ElapsedMilliseconds,4}ms)                    ║");
        Console.WriteLine($"║  Blockbook: {bbFee:F8} DGB/kB  ({sw2.ElapsedMilliseconds,4}ms)                    ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
    }

    // ============================================================
    // Full wallet simulation
    // ============================================================

    [Fact]
    public async Task Live_FullWalletRefresh_Comparison()
    {
        var esplora = CreateEsplora();
        var blockbook = CreateBlockbook();

        // Warm up
        await esplora.GetBalanceAsync(TestAddresses[0]);
        await blockbook.GetBalanceAsync(TestAddresses[0]);

        // Esplora: parallel balance + parallel tx history
        var sw1 = Stopwatch.StartNew();
        var esBal = await esplora.GetBalanceAsync(HdAddresses);
        var esSem = new SemaphoreSlim(10);
        var esTxTasks = HdAddresses.Select(async addr =>
        {
            await esSem.WaitAsync();
            try { return await esplora.GetAddressTransactionsAsync(addr); }
            finally { esSem.Release(); }
        });
        var esTxResults = await Task.WhenAll(esTxTasks);
        var esTxCount = esTxResults.SelectMany(t => t).DistinctBy(t => t.TxId).Count();
        sw1.Stop();

        // Blockbook: parallel balance + parallel tx history
        var sw2 = Stopwatch.StartNew();
        var bbBal = await blockbook.GetBalanceAsync(HdAddresses);
        var bbSem = new SemaphoreSlim(10);
        var bbTxTasks = HdAddresses.Select(async addr =>
        {
            await bbSem.WaitAsync();
            try { return await blockbook.GetAddressTransactionsAsync(addr); }
            finally { bbSem.Release(); }
        });
        var bbTxResults = await Task.WhenAll(bbTxTasks);
        var bbTxCount = bbTxResults.SelectMany(t => t).DistinctBy(t => t.TxId).Count();
        sw2.Stop();

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  FULL WALLET REFRESH ({HdAddresses.Length} addresses, parallel)             ║");
        Console.WriteLine($"║  balance + tx history                                        ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Esplora  (digiexplorer.info):                               ║");
        Console.WriteLine($"║    Time: {sw1.ElapsedMilliseconds,6}ms | Balance: {esBal,12} sat | Txs: {esTxCount,4}  ║");
        Console.WriteLine($"║  Blockbook (digibyteblockexplorer.com):                      ║");
        Console.WriteLine($"║    Time: {sw2.ElapsedMilliseconds,6}ms | Balance: {bbBal,12} sat | Txs: {bbTxCount,4}  ║");
        Console.WriteLine($"║                                                              ║");
        Console.WriteLine($"║  Winner: {(sw1.ElapsedMilliseconds < sw2.ElapsedMilliseconds ? "Esplora" : "Blockbook"),-10} ({Math.Abs(sw1.ElapsedMilliseconds - sw2.ElapsedMilliseconds)}ms faster)                         ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static void PrintResults(string title, List<long> esploraTimes, List<long> blockbookTimes)
    {
        var esAvg = esploraTimes.Average();
        var bbAvg = blockbookTimes.Average();
        var esMin = esploraTimes.Min();
        var bbMin = blockbookTimes.Min();
        var esMax = esploraTimes.Max();
        var bbMax = blockbookTimes.Max();

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  {title,-58}║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║             Avg       Min       Max                          ║");
        Console.WriteLine($"║  Esplora:  {esAvg,5:F0}ms   {esMin,5}ms   {esMax,5}ms                        ║");
        Console.WriteLine($"║  Blockbook:{bbAvg,5:F0}ms   {bbMin,5}ms   {bbMax,5}ms                        ║");
        Console.WriteLine($"║  Winner: {(esAvg < bbAvg ? "Esplora" : "Blockbook"),-10} ({Math.Abs(esAvg - bbAvg):F0}ms faster avg)                   ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
    }
}
