using System.Net;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// An explorer that cannot answer must never look like a wallet with no transactions (spec 025, FR-015; Principle V). The explorer
/// clients used to turn every failure into an empty list, so with the explorers unreachable (circuit breakers open) a history load
/// reported <c>Complete</c> with no transactions and Activity said "No transactions yet".
/// </summary>
public class UnreachableExplorerHistoryTests
{
    private const string Address = "DPSNEw5UmtUMQwTZPNoiUFDmCLu3Tp5SrK";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    /// <summary>What an unreachable or unhealthy explorer looks like from the client.</summary>
    public static IEnumerable<object[]> Failures() =>
    [
        ["transport error", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("connection refused"))],
        ["circuit open", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new InvalidOperationException("The circuit is now open"))],
        ["http 429", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Reply(HttpStatusCode.TooManyRequests, "{}"))],
        ["http 502", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Reply(HttpStatusCode.BadGateway, "<html>bad gateway</html>"))],
        ["unreadable body", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Reply(HttpStatusCode.OK, "<html>maintenance</html>"))],
    ];

    private static HttpClient Down() => new(new Handler(_ => throw new HttpRequestException("connection refused")));

    // ─── The explorer clients ───

    [Theory, MemberData(nameof(Failures))]
    public async Task Esplora_History_WhenItCannotAnswer_Throws(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var esplora = new BlockchainApiService(new HttpClient(new Handler(respond)));

        await Assert.ThrowsAnyAsync<Exception>(() => esplora.GetAddressTransactionsAsync(Address));
        _ = mode;
    }

    [Theory, MemberData(nameof(Failures))]
    public async Task Blockbook_History_WhenItCannotAnswer_Throws(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var blockbook = new BlockbookApiService(new HttpClient(new Handler(respond)), "https://blockbook.test");

        await Assert.ThrowsAnyAsync<Exception>(() => blockbook.GetAddressTransactionsAsync(Address));
        _ = mode;
    }

    [Fact]
    public async Task Esplora_History_OfAnUnusedAddress_IsStillEmpty()
    {
        var esplora = new BlockchainApiService(new HttpClient(new Handler(_ => Reply(HttpStatusCode.OK, "[]"))));

        Assert.Empty(await esplora.GetAddressTransactionsAsync(Address));
    }

    [Fact]
    public async Task Blockbook_History_OfAnUnusedAddress_IsStillEmpty()
    {
        var blockbook = new BlockbookApiService(new HttpClient(new Handler(_ =>
            Reply(HttpStatusCode.OK, $"{{\"address\":\"{Address}\",\"txs\":0,\"unconfirmedTxs\":0}}"))), "https://blockbook.test");

        Assert.Empty(await blockbook.GetAddressTransactionsAsync(Address));
    }

    // ─── The fallback service ───

    /// <summary>
    /// Node use off (the default): the node is the last resort, and its lenient client reads its own failure as "no transactions".
    /// It can never list full history anyway, so with the explorers down the history read must fail rather than answer from it.
    /// </summary>
    private static FallbackBlockchainService AllExplorersDown(FakeNodeApi node) =>
        new(node.Client(strict: false),
            [new BlockbookApiService(Down(), "https://blockbook.test"), new BlockchainApiService(Down())],
            new HttpClient(), isDevelopment: false);

    [Fact]
    public async Task Fallback_History_WithEveryExplorerDown_AndTheNodeDown_Throws()
    {
        var sut = AllExplorersDown(new FakeNodeApi { Fails = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAddressTransactionsAsync(Address));
        Assert.True(sut.AllSourcesFailed);
    }

    [Fact]
    public async Task Fallback_History_WithEveryExplorerDown_IsNotAnsweredByTheNode()
    {
        var sut = AllExplorersDown(new FakeNodeApi());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAddressTransactionsAsync(Address));
    }

    // ─── Activity's history load ───

    /// <summary>Address discovery answers from the scripted explorer; the history itself goes to the fallback service.</summary>
    private sealed class HistoryThrough(SummaryExplorer discovery, IBlockchainService history) : IBlockchainService
    {
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
            history.GetAddressTransactionsAsync(address, skip, take);
        public Task<bool> HasAddressHistoryAsync(string address) => discovery.HasAddressHistoryAsync(address);
        public Task<long> GetBalanceAsync(string address) => discovery.GetBalanceAsync(address);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => discovery.GetBalanceAsync(addresses);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => discovery.GetUtxosAsync(address);
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => discovery.GetUtxosAsync(addresses);
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => discovery.BroadcastTransactionAsync(rawTransaction);
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => discovery.GetTransactionAsync(txId);
        public Task<decimal> GetFeeRateAsync() => discovery.GetFeeRateAsync();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => discovery.GetDgbPriceAsync(fiatCurrency);
        public Task<int> GetBlockHeightAsync() => discovery.GetBlockHeightAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoryLoad_WithTheExplorersUnreachable_IsNotComplete(bool nodeDown)
    {
        var env = new HistorySyncEnv();
        env.Options.ChangeOnly = false;
        env.Explorer.Pay(env.Receive(0), 100_000_000);
        var service = env.NewService(new HistoryThrough(env.Explorer, AllExplorersDown(new FakeNodeApi { Fails = nodeDown })));
        await service.CreateWalletAsync("Test", env.Words, ChangeAddressTests.Pin);

        var load = await service.LoadTransactionHistoryAsync();

        Assert.True(load.AddressesSettled);
        Assert.False(load.Complete);
        Assert.Empty(load.Transactions);
    }
}
