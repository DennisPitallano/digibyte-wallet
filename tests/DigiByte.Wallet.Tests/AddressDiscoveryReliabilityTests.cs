using System.Net;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Address discovery decides which addresses count toward the balance, so a source that cannot answer must never look like
/// an unused address, and a slow source must never hold up the balance or a send.
/// </summary>
public class AddressDiscoveryReliabilityTests
{
    private const string Address = "DPSNEw5UmtUMQwTZPNoiUFDmCLu3Tp5SrK";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }

    private static Task<bool> HasHistory(IBlockchainService source) => source.HasAddressHistoryAsync(Address);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    public static IEnumerable<object[]> ExplorerFailures() =>
    [
        ["transport error", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("connection refused"))],
        ["http 429", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.TooManyRequests, "{}"))],
        ["http 502", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>"))],
        ["unreadable body", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.OK, "<html>maintenance</html>"))],
        ["empty object", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.OK, "{}"))],
    ];

    // ─── Esplora ───

    [Theory]
    [InlineData(11, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, 0, false)]
    public async Task Esplora_ReadsTheTransactionCounts_WithOneRequest(int chain, int mempool, bool expected)
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK,
            $"{{\"chain_stats\":{{\"tx_count\":{chain}}},\"mempool_stats\":{{\"tx_count\":{mempool}}}}}"));
        var esplora = new BlockchainApiService(new HttpClient(handler));

        Assert.Equal(expected, await HasHistory(esplora));
        Assert.Single(handler.Paths);
        Assert.EndsWith($"/address/{Address}", handler.Paths[0]);
    }

    [Theory, MemberData(nameof(ExplorerFailures))]
    public async Task Esplora_WhenItCannotAnswer_Throws(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var esplora = new BlockchainApiService(new HttpClient(new Handler(respond)));

        await Assert.ThrowsAnyAsync<Exception>(() => HasHistory(esplora));
        _ = mode;
    }

    // ─── Blockbook ───

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, 0, false)]
    public async Task Blockbook_ReadsTheTransactionCounts_WithOneRequest(int confirmed, int unconfirmed, bool expected)
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK, $"{{\"txs\":{confirmed},\"unconfirmedTxs\":{unconfirmed}}}"));
        var blockbook = new BlockbookApiService(new HttpClient(handler), "https://blockbook.test");

        Assert.Equal(expected, await HasHistory(blockbook));
        Assert.Single(handler.Paths);
    }

    [Theory, MemberData(nameof(ExplorerFailures))]
    public async Task Blockbook_WhenItCannotAnswer_Throws(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var blockbook = new BlockbookApiService(new HttpClient(new Handler(respond)), "https://blockbook.test");

        await Assert.ThrowsAnyAsync<Exception>(() => HasHistory(blockbook));
        _ = mode;
    }

    // ─── The node only sees unspent outputs, so a spent address would look unused ───

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Node_RefusesToAnswer(bool strict)
    {
        var node = new FakeNodeApi();

        await Assert.ThrowsAsync<NotSupportedException>(() => HasHistory(node.Client(strict)));

        Assert.Empty(node.Requests);
    }

    // ─── The fallback service ───

    private static FallbackBlockchainService Fallback(FakeNodeApi node, params ScriptedExplorer[] explorers) =>
        new(node.Client(strict: false), explorers, new HttpClient(), isDevelopment: false);

    private static TransactionInfo AnyTransaction() => new()
    {
        TxId = new string('e', 64), Confirmations = 6, Timestamp = DateTime.UtcNow, FeeSatoshis = 0, Inputs = [], Outputs = [],
    };

    [Fact]
    public async Task Fallback_WhenAnExplorerFails_AsksTheNextOne()
    {
        var first = new ScriptedExplorer("first") { Fails = true };
        var second = new ScriptedExplorer("second") { History = [AnyTransaction()] };

        Assert.True(await HasHistory(Fallback(new FakeNodeApi(), first, second)));
    }

    [Fact]
    public async Task Fallback_WhenEveryExplorerFails_Throws_AndNeverAsksTheNode()
    {
        var node = new FakeNodeApi();
        var sut = Fallback(node, new ScriptedExplorer("only") { Fails = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => HasHistory(sut));

        Assert.Empty(node.Requests);
    }

    /// <summary>Discovery makes many lookups at once. A rate limit on those must not put the explorer on cooldown for balances too.</summary>
    [Fact]
    public async Task Fallback_AFailedLookup_DoesNotAffectOtherReads()
    {
        var explorer = new ScriptedExplorer("only") { Fails = true };
        var sut = Fallback(new FakeNodeApi(), explorer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => HasHistory(sut));
        Assert.False(sut.AllSourcesFailed);

        explorer.Fails = false;

        Assert.Equal(explorer.Balance, await sut.GetBalanceAsync(Address));
    }

    [Fact]
    public async Task Fallback_DoesNotCacheTheAnswer()
    {
        var explorer = new ScriptedExplorer("only");
        var sut = Fallback(new FakeNodeApi(), explorer);
        Assert.False(await HasHistory(sut));

        explorer.History = [AnyTransaction()];

        Assert.True(await HasHistory(sut));
    }

    private static async Task<WalletService> CreateAsync(Env env)
    {
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        return service;
    }

    // ─── A save that finishes after the wallet was deleted ───

    [Fact]
    public async Task IndexSave_AfterTheWalletWasDeleted_DoesNotBringItBack()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ChangeAddress(3), OneDgb);
        var gate = new TaskCompletionSource();
        env.Chain.HistoryGate = gate.Task;
        service.AddressDiscoveryWait = TimeSpan.FromMilliseconds(20);
        await service.GetBalanceAsync();

        await service.DeleteWalletAsync(walletId);
        gate.SetResult();
        await Task.Delay(500);

        Assert.Null(await new WalletKeyStore(env.Storage).GetWalletInfoAsync(walletId));
    }

    // ─── A slow source must not hold up the balance, history or a send ───

    [Fact]
    public async Task Balance_WhenDiscoveryIsSlow_AnswersFromTheSavedScan_AndPicksUpTheResultLater()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.Credit(env.ReceiveAddress(0), OneDgb);
        env.Chain.Credit(env.ChangeAddress(14), 5 * OneDgb);
        var gate = new TaskCompletionSource();
        env.Chain.HistoryGate = gate.Task;
        service.AddressDiscoveryWait = TimeSpan.FromMilliseconds(20);

        var balance = await service.GetBalanceAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(OneDgb, balance.ConfirmedSatoshis);

        gate.SetResult();
        service.AddressDiscoveryWait = TimeSpan.FromSeconds(30);
        Assert.Equal(6 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task History_WhenDiscoveryIsSlow_StillAnswers()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.HistoryGate = new TaskCompletionSource().Task;
        service.AddressDiscoveryWait = TimeSpan.FromMilliseconds(20);

        await service.GetTransactionHistoryAsync().WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Send_WhenDiscoveryIsSlow_StillGoesOut()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        env.Chain.HistoryGate = new TaskCompletionSource().Task;
        service.AddressDiscoverySendWait = TimeSpan.FromMilliseconds(20);

        await service.SendAsync(Stranger(), 1m).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(env.Chain.LastBroadcast);
    }

    // ─── A failed discovery must not repeat its lookups on every balance refresh ───

    [Fact]
    public async Task Discovery_WhenExplorerIsDown_WaitsBeforeTryingAgain()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.HistoryDown = true;

        await service.GetBalanceAsync();
        var afterFirst = env.Chain.HistoryLookups;
        await service.GetBalanceAsync();

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, env.Chain.HistoryLookups);
    }

    [Fact]
    public async Task Generate_AtTheGapLimit_LooksAgainEvenWhileDiscoveryIsWaitingToRetry()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.HistoryDown = true;
        for (var i = 0; i < WalletService.ReceiveGapLimit - 1; i++)
            await service.GenerateNewReceivingAddressAsync();
        env.Chain.Credit(await service.GetReceivingAddressAsync(), OneDgb);
        env.Chain.HistoryDown = false;

        var (index, _) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(WalletService.ReceiveGapLimit, index);
    }
}
