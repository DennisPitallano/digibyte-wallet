using System.Net;
using System.Text.Json;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The Esplora client (spec 023): one tip height shared by every caller within 10 s, strict address summaries, history pages,
/// and complete history past the explorer's 25-transaction page.
/// </summary>
public class EsploraPagingTests
{
    /// <summary>An Esplora-shaped HTTP stub: per-address confirmed (newest first) and pending transactions.</summary>
    internal sealed class EsploraStub : HttpMessageHandler
    {
        public const int PageSize = 25;
        public int TipHeight { get; set; } = 1000;
        public int TipRequests, SummaryRequests, PageRequests, TxRequests;
        public List<string> Paths { get; } = [];
        public Dictionary<string, List<(string TxId, int? Height)>> Txs { get; } = new();
        public string? SummaryBody { get; set; }
        public HttpStatusCode SummaryStatus { get; set; } = HttpStatusCode.OK;
        public bool EndlessPages { get; set; }
        public TaskCompletionSource? TipGate { get; set; }

        /// <summary>Gives the address <paramref name="confirmed"/> confirmed transactions (heights 1..n) and <paramref name="pending"/> pending ones.</summary>
        public void Busy(string address, int confirmed, int pending = 0)
        {
            var list = new List<(string, int?)>();
            for (var i = 0; i < pending; i++) list.Add(($"{address}-p{i}", null));
            for (var h = confirmed; h >= 1; h--) list.Add(($"{address}-c{h:0000}", h));
            Txs[address] = list;
        }

        private static object Tx(string id, int? height, string address) => new
        {
            txid = id,
            fee = 100,
            status = new { confirmed = height is not null, block_height = height ?? 0, block_time = 1_700_000_000 + (height ?? 0) },
            vin = new[] { new { prevout = new { value = 1100, scriptpubkey_address = "dgb1qsender" } } },
            vout = new[] { new { value = 1000, scriptpubkey_address = address } },
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var at = path.IndexOf("/api/", StringComparison.Ordinal);
            path = at >= 0 ? path[(at + 4)..] : path;
            lock (Paths) Paths.Add(path);
            var parts = path.Trim('/').Split('/');

            if (path == "/blocks/tip/height")
            {
                Interlocked.Increment(ref TipRequests);
                if (TipGate is { } gate) await gate.Task;
                return Text(TipHeight.ToString());
            }

            if (parts is ["tx", var id])
            {
                Interlocked.Increment(ref TxRequests);
                return Json(Tx(id, 990, "dgb1qsomeone"));
            }

            if (parts is ["address", var address])
            {
                Interlocked.Increment(ref SummaryRequests);
                if (SummaryStatus != HttpStatusCode.OK) return new HttpResponseMessage(SummaryStatus);
                if (SummaryBody is not null) return Text(SummaryBody);
                var list = Txs.GetValueOrDefault(address) ?? [];
                return Json(new
                {
                    address,
                    chain_stats = new { tx_count = list.Count(t => t.Height is not null), funded_txo_sum = 5000L, spent_txo_sum = 1200L },
                    mempool_stats = new { tx_count = list.Count(t => t.Height is null), funded_txo_sum = 300L, spent_txo_sum = 50L },
                });
            }

            if (parts is ["address", var a, "txs", ..])
            {
                Interlocked.Increment(ref PageRequests);
                var list = Txs.GetValueOrDefault(a) ?? [];
                var confirmed = list.Where(t => t.Height is not null).ToList();
                IEnumerable<(string TxId, int? Height)> page;
                if (parts.Length == 3)
                    page = list.Where(t => t.Height is null).Concat(confirmed.Take(PageSize));
                else if (EndlessPages)
                    page = Enumerable.Range(0, PageSize).Select(i => ($"{parts[4]}-{i}", (int?)500)).ToList();
                else
                {
                    var after = confirmed.FindIndex(t => t.TxId == parts[4]);
                    page = after < 0 ? [] : confirmed.Skip(after + 1).Take(PageSize);
                }
                return Json(page.Select(t => Tx(t.TxId, t.Height, a)).ToList());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
        private static HttpResponseMessage Json(object body) => Text(JsonSerializer.Serialize(body));
    }

    private static (BlockchainApiService Api, EsploraStub Stub) Make()
    {
        var stub = new EsploraStub();
        return (new BlockchainApiService(new HttpClient(stub)), stub);
    }

    // ── One tip height (T007, FR-001) ──

    [Fact]
    public async Task HistoryLoadOverTenAddresses_SendsOneTipRequest()
    {
        var (api, stub) = Make();
        var addresses = Enumerable.Range(0, 10).Select(i => $"dgb1qaddr{i}").ToList();
        foreach (var a in addresses) stub.Busy(a, 3);

        await Task.WhenAll(addresses.Select(a => api.GetAddressTransactionsAsync(a)));

        Assert.Equal(1, stub.TipRequests);
    }

    [Fact]
    public async Task HistoryLoadAndATipRead_Overlapping_SendOneTipRequestInTotal()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qa", 3);
        stub.TipGate = new TaskCompletionSource();

        var history = api.GetAddressTransactionsAsync("dgb1qa");
        var tip = api.GetBlockHeightAsync();
        stub.TipGate.SetResult();
        await history;

        Assert.Equal(1000, await tip);
        Assert.Equal(1000, await api.GetBlockHeightAsync());
        Assert.Equal(1, stub.TipRequests);
    }

    [Fact]
    public async Task GetTransactionAsync_ReusesTheSameTip()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qa", 3);

        await api.GetAddressTransactionsAsync("dgb1qa");
        var tx = await api.GetTransactionAsync("t1");

        Assert.Equal(11, tx!.Confirmations); // 1000 − 990 + 1
        Assert.Equal(1, stub.TipRequests);
    }

    // ── Address summary (T007) ──

    [Fact]
    public async Task Summary_ParsesChainAndMempoolStats()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qa", confirmed: 4, pending: 2);

        var summary = await api.GetAddressSummaryAsync("dgb1qa");

        Assert.Equal("dgb1qa", summary.Address);
        Assert.Equal(4, summary.ConfirmedTxCount);
        Assert.Equal(2, summary.PendingTxCount);
        Assert.Equal(5000, summary.ConfirmedFunded);
        Assert.Equal(1200, summary.ConfirmedSpent);
        Assert.Equal(300, summary.PendingFunded);
        Assert.Equal(50, summary.PendingSpent);
    }

    [Theory]
    [InlineData("""{"chain_stats":{"tx_count":1,"funded_txo_sum":5,"spent_txo_sum":0}}""")]
    [InlineData("""{"mempool_stats":{"tx_count":0,"funded_txo_sum":0,"spent_txo_sum":0}}""")]
    public async Task Summary_MissingStats_Throws(string body)
    {
        var (api, stub) = Make();
        stub.SummaryBody = body;
        await Assert.ThrowsAnyAsync<Exception>(() => api.GetAddressSummaryAsync("dgb1qa"));
    }

    [Fact]
    public async Task Summary_NonSuccessStatus_Throws()
    {
        var (api, stub) = Make();
        stub.SummaryStatus = HttpStatusCode.TooManyRequests;
        await Assert.ThrowsAnyAsync<Exception>(() => api.GetAddressSummaryAsync("dgb1qa"));
    }

    // ── History pages (T007) ──

    [Fact]
    public async Task HistoryPage_NewestAndAfter_UseTheRightEndpoints()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qa", confirmed: 30, pending: 1);

        var newest = await api.GetAddressHistoryPageAsync("dgb1qa", null);
        var older = await api.GetAddressHistoryPageAsync("dgb1qa", newest.LastConfirmedTxId);

        Assert.Contains("/address/dgb1qa/txs", stub.Paths);
        Assert.Contains($"/address/dgb1qa/txs/chain/{newest.LastConfirmedTxId}", stub.Paths);
        Assert.Equal(26, newest.Transactions.Count);           // 1 pending + 25 confirmed
        Assert.Equal(0, newest.Transactions[0].Confirmations);
        Assert.Equal("dgb1qa-c0006", newest.LastConfirmedTxId);
        Assert.Equal(1000 - 30 + 1, newest.Transactions[1].Confirmations);
        Assert.Equal(30, newest.BlockHeights!["dgb1qa-c0030"]);
        Assert.Equal(5, older.Transactions.Count);
    }

    // ── Complete history (T030, FR-012) ──

    [Fact]
    public async Task AddressTransactions_SixtyConfirmed_ReturnsAllSixty()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qbusy", 60);

        var all = await api.GetAddressTransactionsAsync("dgb1qbusy", 0, 1000);

        Assert.Equal(60, all.Count);
        Assert.Equal(60, all.Select(t => t.TxId).Distinct().Count());
        Assert.Contains("/address/dgb1qbusy/txs/chain/dgb1qbusy-c0036", stub.Paths);
    }

    [Fact]
    public async Task AddressTransactions_SkipAndTakeApplyToTheFullList()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qbusy", 60);

        var slice = await api.GetAddressTransactionsAsync("dgb1qbusy", 40, 5);

        Assert.Equal(["dgb1qbusy-c0020", "dgb1qbusy-c0019", "dgb1qbusy-c0018", "dgb1qbusy-c0017", "dgb1qbusy-c0016"],
            slice.Select(t => t.TxId));
    }

    [Fact]
    public async Task AddressTransactions_AnEndlessSource_StopsAtTheCap()
    {
        var (api, stub) = Make();
        stub.Busy("dgb1qbusy", 25);
        stub.EndlessPages = true;

        var all = await api.GetAddressTransactionsAsync("dgb1qbusy", 0, int.MaxValue);

        Assert.Equal(BlockchainApiService.MaxTransactionsPerAddress, all.Count);
    }
}
