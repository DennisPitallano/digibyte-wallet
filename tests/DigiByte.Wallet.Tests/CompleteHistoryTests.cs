using System.Net;
using System.Text.Json;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The strict "complete history" read the CSV export uses (spec 031, research R1, FR-003/FR-006): no per-address cap, no silent
/// empty list on a failure, and never the saved copy Activity shows offline.
/// </summary>
public class CompleteHistoryTests
{
    // ── Esplora ──

    [Fact]
    public async Task Esplora_PagesPastTheTwoThousandCap_ToTheLastPage()
    {
        var stub = new EsploraPagingTests.EsploraStub { TipHeight = 5_000 };
        stub.Busy("dgb1qbusy", 2_060, pending: 2);
        var api = new BlockchainApiService(new HttpClient(stub));

        var all = await api.GetCompleteAddressHistoryAsync("dgb1qbusy");

        Assert.Equal(2_062, all.Count);
        Assert.Equal(all.Count, all.Select(t => t.TxId).Distinct().Count());
    }

    [Fact]
    public async Task Esplora_AFailedPage_Throws_NeverAPartialList()
    {
        var stub = new FailingAfterHandler(new EsploraPagingTests.EsploraStub(), failPageRequestNumber: 3);
        stub.Inner.Busy("dgb1qbusy", 200);
        var api = new BlockchainApiService(new HttpClient(stub));

        await Assert.ThrowsAnyAsync<Exception>(() => api.GetCompleteAddressHistoryAsync("dgb1qbusy"));
    }

    // ── Blockbook ──

    [Fact]
    public async Task Blockbook_PagesByThousand_UntilTotalPages()
    {
        var stub = new BlockbookStub(total: 2_345);
        var api = new BlockbookApiService(new HttpClient(stub), "https://bb.test");

        var all = await api.GetCompleteAddressHistoryAsync("dgb1qbusy");

        Assert.Equal(2_345, all.Count);
        Assert.Equal([1, 2, 3], stub.PagesAsked);
        Assert.All(stub.PageSizes, s => Assert.Equal(1000, s));
    }

    [Fact]
    public async Task Blockbook_AFailedPage_Throws()
    {
        var stub = new BlockbookStub(total: 2_345) { FailPage = 2 };
        var api = new BlockbookApiService(new HttpClient(stub), "https://bb.test");

        await Assert.ThrowsAnyAsync<Exception>(() => api.GetCompleteAddressHistoryAsync("dgb1qbusy"));
    }

    // ── Fallback: skip sources that can't do a complete read ──

    [Fact]
    public async Task Fallback_SkipsAnUnsupportedSource_AndUsesTheNextExplorer()
    {
        var unsupported = new ChangeAddressTests.FakeChain(); // a source without the complete read
        var unsupportedOnly = new UnsupportedSource(unsupported);
        var stub = new EsploraPagingTests.EsploraStub();
        stub.Busy("dgb1qa", 3);
        var esplora = new BlockchainApiService(new HttpClient(stub));
        var sut = new FallbackBlockchainService(new NodeApiBlockchainService(new HttpClient()), [unsupportedOnly, esplora],
            new HttpClient(), isDevelopment: false);

        var all = await sut.GetCompleteAddressHistoryAsync("dgb1qa");

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Fallback_OnlyUnsupportedSources_Throws()
    {
        var sut = new FallbackBlockchainService(new NodeApiBlockchainService(new HttpClient()),
            [new UnsupportedSource(new ChangeAddressTests.FakeChain())], new HttpClient(), isDevelopment: false);

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => sut.GetCompleteAddressHistoryAsync("dgb1qa"));
    }

    [Fact]
    public async Task Fallback_EveryExplorerFails_Throws()
    {
        var stub = new FailingAfterHandler(new EsploraPagingTests.EsploraStub(), failPageRequestNumber: 1);
        var sut = new FallbackBlockchainService(new NodeApiBlockchainService(new HttpClient()),
            [new BlockchainApiService(new HttpClient(stub))], new HttpClient(), isDevelopment: false);

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => sut.GetCompleteAddressHistoryAsync("dgb1qa"));
    }

    // ── WalletService.LoadCompleteHistoryAsync ──

    private static async Task<(ChangeAddressTests.Env Env, WalletService Service)> FundedWalletAsync(int credits)
    {
        var env = new ChangeAddressTests.Env();
        env.Chain.DetailedHistory = true;
        var service = await env.CreateFundedAsync();
        for (var i = 1; i < credits; i++)
            env.Chain.Credit(env.ReceiveAddress(i % 3), (i + 1) * ChangeAddressTests.OneDgb);
        return (env, service);
    }

    [Fact]
    public async Task Wallet_BuildsTheSameRecordsAsTheActivityLoad()
    {
        var (_, service) = await FundedWalletAsync(5);

        var page = await service.LoadTransactionHistoryAsync(0, 1000);
        var complete = await service.LoadCompleteHistoryAsync();

        Assert.Equal(
            page.Transactions.OrderBy(t => t.TxId).Select(t => (t.TxId, t.Direction, t.AmountSatoshis)),
            complete.OrderBy(t => t.TxId).Select(t => (t.TxId, t.Direction, t.AmountSatoshis)));
        Assert.Equal(5, complete.Count);
    }

    [Fact]
    public async Task Wallet_ReturnsNewestFirst()
    {
        var (_, service) = await FundedWalletAsync(3);
        var complete = await service.LoadCompleteHistoryAsync();
        Assert.Equal(complete.OrderByDescending(t => t.Timestamp).Select(t => t.TxId), complete.Select(t => t.TxId));
    }

    [Fact]
    public async Task Wallet_AnAddressFailsPartway_ThrowsHistoryIncomplete()
    {
        var (env, service) = await FundedWalletAsync(5);
        env.Chain.CompleteHistoryFailAfter = 1;

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => service.LoadCompleteHistoryAsync());
    }

    [Fact]
    public async Task Wallet_NeverFallsBackToWhatActivityLoadedEarlier()
    {
        var (env, service) = await FundedWalletAsync(3);
        await service.LoadTransactionHistoryAsync(0, 1000);   // Activity has its list now

        env.Chain.HistoryDown = true;

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => service.LoadCompleteHistoryAsync());
    }

    [Fact]
    public async Task Wallet_ReportsProgressAsAddressesDoneOfTotal()
    {
        var (_, service) = await FundedWalletAsync(2);
        var reports = new List<HistoryExportProgress>();
        var progress = new SyncProgress<HistoryExportProgress>(reports.Add);

        await service.LoadCompleteHistoryAsync(progress);

        Assert.NotEmpty(reports);
        var last = reports.MaxBy(r => r.Done)!;
        Assert.Equal(last.Total, last.Done);
        Assert.All(reports, r => Assert.InRange(r.Done, 0, r.Total));
    }

    [Fact]
    public async Task Wallet_HonoursCancellation()
    {
        var (_, service) = await FundedWalletAsync(2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadCompleteHistoryAsync(null, cts.Token));
    }

    [Fact]
    public async Task Wallet_Locked_Throws()
    {
        var env = new ChangeAddressTests.Env();
        var service = env.NewService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadCompleteHistoryAsync());
    }

    // ── Test doubles ──

    /// <summary>Reports synchronously, so a test can read the reports as soon as the call returns.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) { lock (this) report(value); }
    }

    /// <summary>Wraps a source and leaves the complete read at its "not supported" default.</summary>
    private sealed class UnsupportedSource(IBlockchainService inner) : IBlockchainService
    {
        public Task<long> GetBalanceAsync(string address) => inner.GetBalanceAsync(address);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => inner.GetBalanceAsync(addresses);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => inner.GetUtxosAsync(address);
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => inner.GetUtxosAsync(addresses);
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => inner.BroadcastTransactionAsync(rawTransaction);
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => inner.GetTransactionAsync(txId);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
            inner.GetAddressTransactionsAsync(address, skip, take);
        public Task<decimal> GetFeeRateAsync() => inner.GetFeeRateAsync();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => inner.GetDgbPriceAsync(fiatCurrency);
        public Task<int> GetBlockHeightAsync() => inner.GetBlockHeightAsync();
    }

    /// <summary>Lets the inner Esplora stub answer, except history page request number N, which fails with a 500.</summary>
    private sealed class FailingAfterHandler(EsploraPagingTests.EsploraStub inner, int failPageRequestNumber) : DelegatingHandler(inner)
    {
        private int _pages;
        public EsploraPagingTests.EsploraStub Inner => inner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/txs") && Interlocked.Increment(ref _pages) == failPageRequestNumber)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return base.SendAsync(request, ct);
        }
    }

    /// <summary>A Blockbook <c>/api/v2/address</c> stub with <paramref name="total"/> transactions in pages.</summary>
    private sealed class BlockbookStub(int total) : HttpMessageHandler
    {
        public List<int> PagesAsked { get; } = [];
        public List<int> PageSizes { get; } = [];
        public int? FailPage { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var page = int.Parse(query["page"] ?? "1");
            var size = int.Parse(query["pageSize"] ?? "1000");
            PagesAsked.Add(page);
            PageSizes.Add(size);
            if (page == FailPage) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var totalPages = (total + size - 1) / size;
            var txs = Enumerable.Range((page - 1) * size, Math.Max(0, Math.Min(size, total - (page - 1) * size)))
                .Select(i => new
                {
                    txid = $"tx{i:00000}",
                    confirmations = 10,
                    blockTime = 1_700_000_000 + i,
                    fees = "100",
                    vin = new[] { new { addresses = new[] { "dgb1qsender" }, value = "1100" } },
                    vout = new[] { new { addresses = new[] { "dgb1qbusy" }, value = "1000", n = 0, hex = "" } },
                }).ToList();
            var body = JsonSerializer.Serialize(new { page, totalPages, itemsOnPage = size, balance = "0", transactions = txs });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
