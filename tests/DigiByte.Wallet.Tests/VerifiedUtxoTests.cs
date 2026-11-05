using System.Net;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// <see cref="IBlockchainService.GetVerifiedUtxosAsync"/> proves a coin before the wallet signs for it, so a source that can't
/// answer must throw. An empty list would read as a spent coin (FR-007, FR-013).
/// </summary>
public class VerifiedUtxoTests
{
    private const string Address = "DPSNEw5UmtUMQwTZPNoiUFDmCLu3Tp5SrK";
    private const string TxId = "1111111111111111111111111111111111111111111111111111111111111111";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private static Task<List<UtxoInfo>> Verified(IBlockchainService source) => source.GetVerifiedUtxosAsync(Address);

    public static IEnumerable<object[]> Failures() =>
    [
        ["transport error", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("connection refused"))],
        ["http 429", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.TooManyRequests, "{}"))],
        ["http 502", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>"))],
        ["html reply", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.OK, "<!DOCTYPE html><html></html>"))],
    ];

    // ─── Esplora ───

    private static BlockchainApiService Esplora(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new HttpClient(new Handler(respond)));

    [Fact]
    public async Task Esplora_ReturnsTheOutputs()
    {
        var esplora = Esplora(_ => Json(HttpStatusCode.OK,
            $"[{{\"txid\":\"{TxId}\",\"vout\":1,\"value\":5000,\"status\":{{\"confirmed\":true}}}}]"));

        var utxo = Assert.Single(await Verified(esplora));

        Assert.Equal(TxId, utxo.TxId);
        Assert.Equal(1u, utxo.OutputIndex);
        Assert.Equal(5000, utxo.AmountSatoshis);
    }

    [Fact]
    public async Task Esplora_AddressWithNoCoins_IsAnEmptyList()
    {
        Assert.Empty(await Verified(Esplora(_ => Json(HttpStatusCode.OK, "[]"))));
    }

    [Theory, MemberData(nameof(Failures))]
    public async Task Esplora_WhenItCannotAnswer_Throws(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Verified(Esplora(respond)));
        _ = mode;
    }

    [Fact]
    public async Task Esplora_PlainGetUtxos_StillHidesFailures_SoBalanceAndSendAreUnchanged()
    {
        Assert.Empty(await Esplora(_ => Json(HttpStatusCode.BadGateway, "")).GetUtxosAsync(Address));
    }

    // ─── Blockbook ───

    private static BlockbookApiService Blockbook(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)), "https://blockbook.test");

    [Fact]
    public async Task Blockbook_ReturnsTheOutputs()
    {
        var blockbook = Blockbook(_ => Json(HttpStatusCode.OK,
            $"[{{\"txid\":\"{TxId}\",\"vout\":0,\"value\":\"7000\",\"confirmations\":3}}]"));

        var utxo = Assert.Single(await Verified(blockbook));

        Assert.Equal(7000, utxo.AmountSatoshis);
        Assert.Equal(3, utxo.Confirmations);
    }

    [Fact]
    public async Task Blockbook_AddressWithNoCoins_IsAnEmptyList()
    {
        Assert.Empty(await Verified(Blockbook(_ => Json(HttpStatusCode.OK, "[]"))));
    }

    [Theory, MemberData(nameof(Failures))]
    public async Task Blockbook_WhenItCannotAnswer_Throws(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Verified(Blockbook(respond)));
        _ = mode;
    }

    // ─── The node, strict and lenient ───

    private static NodeApiBlockchainService Node(Func<HttpRequestMessage, HttpResponseMessage> respond, bool strict) =>
        new(new HttpClient(new Handler(respond)), "http://fake-node:5260", strict);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Node_ReturnsTheOutputs(bool strict)
    {
        var node = Node(_ => Json(HttpStatusCode.OK,
            $"{{\"utxos\":[{{\"txid\":\"{TxId}\",\"vout\":2,\"amountSatoshis\":9000,\"confirmations\":1}}]}}"), strict);

        var utxo = Assert.Single(await Verified(node));

        Assert.Equal(9000, utxo.AmountSatoshis);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Node_AddressWithNoCoins_IsAnEmptyList(bool strict)
    {
        Assert.Empty(await Verified(Node(_ => Json(HttpStatusCode.OK, "{\"utxos\":[]}"), strict)));
    }

    public static IEnumerable<object[]> NodeFailures()
    {
        foreach (var strict in new[] { true, false })
            foreach (var failure in Failures())
                yield return [strict, failure[0], failure[1]];
    }

    [Theory, MemberData(nameof(NodeFailures))]
    public async Task Node_WhenItCannotAnswer_Throws_EvenWhenLenient(bool strict, string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Verified(Node(respond, strict)));
        _ = mode;
    }

    // ─── The fallback service ───

    private static FallbackBlockchainService Fallback(FakeNodeApi node, FakeNodeState? state, params ScriptedExplorer[] explorers) =>
        new(node.Client(strict: state is not null), explorers, new HttpClient(), isDevelopment: false, nodeState: state, failureSink: state);

    private static UtxoInfo Utxo(long amount) => new() { TxId = TxId, OutputIndex = 0, AmountSatoshis = amount, ScriptPubKey = "", Confirmations = 1 };

    [Fact]
    public async Task Fallback_WhenAnExplorerFails_AsksTheNextOne()
    {
        var first = new ScriptedExplorer("first") { Fails = true };
        var second = new ScriptedExplorer("second") { Utxos = [Utxo(4000)] };

        var utxo = Assert.Single(await Verified(Fallback(new FakeNodeApi(), null, first, second)));

        Assert.Equal(4000, utxo.AmountSatoshis);
    }

    [Fact]
    public async Task Fallback_OnRegtest_UsesTheNode()
    {
        var explorer = new ScriptedExplorer("explorer") { Utxos = [Utxo(1)] };
        var node = new FakeNodeApi();
        var sut = Fallback(node, null, explorer);
        sut.SetNetworkMode("regtest");

        Assert.Empty(await Verified(sut));

        Assert.Empty(explorer.Calls);
        Assert.Contains(node.Requests, r => r.EndsWith("/utxos"));
    }

    [Fact]
    public async Task Fallback_WhenExplorersFail_UsesTheActiveNode()
    {
        var node = new FakeNodeApi();
        var sut = Fallback(node, new FakeNodeState { State = NodeSourceState.Active }, new ScriptedExplorer("explorer") { Fails = true });

        Assert.Empty(await Verified(sut));

        Assert.Contains(node.Requests, r => r.EndsWith("/utxos"));
    }

    [Fact]
    public async Task Fallback_WhenExplorersFail_DoesNotAskANodeThatIsNotActive()
    {
        var node = new FakeNodeApi();
        var sut = Fallback(node, new FakeNodeState { State = NodeSourceState.NotReady }, new ScriptedExplorer("explorer") { Fails = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Verified(sut));

        Assert.Empty(node.Requests);
    }

    [Fact]
    public async Task Fallback_WhenNoSourceAnswers_Throws()
    {
        var sut = Fallback(new FakeNodeApi { Fails = true }, new FakeNodeState { State = NodeSourceState.Active }, new ScriptedExplorer("explorer") { Fails = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Verified(sut));
    }

    [Theory]
    [InlineData("mainnet")]
    [InlineData("regtest")]
    public async Task Fallback_InDevelopment_WhenNoSourceAnswers_ThrowsRatherThanUsingDemoData(string network)
    {
        // Demo data never holds the coin being proven, so falling back to it would report a real coin as spent.
        var sut = new FallbackBlockchainService(
            new FakeNodeApi { Fails = true }.Client(strict: true), [new ScriptedExplorer("explorer") { Fails = true }], new HttpClient(),
            isDevelopment: true, mock: new MockBlockchainService(), nodeState: new FakeNodeState { State = NodeSourceState.Active });
        sut.SetNetworkMode(network);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Verified(sut));
    }

    [Fact]
    public async Task Fallback_AFailure_SetsNoCooldownAndNoOutageState()
    {
        var explorer = new ScriptedExplorer("explorer") { Fails = true };
        var sut = Fallback(new FakeNodeApi { Fails = true }, new FakeNodeState { State = NodeSourceState.Active }, explorer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Verified(sut));
        Assert.False(sut.AllSourcesFailed);

        explorer.Fails = false;

        Assert.Equal(explorer.Balance, await sut.GetBalanceAsync(Address));
    }

    [Fact]
    public async Task Fallback_DoesNotCacheTheAnswer()
    {
        var explorer = new ScriptedExplorer("explorer") { Utxos = [Utxo(1000)] };
        var sut = Fallback(new FakeNodeApi(), null, explorer);
        Assert.Single(await Verified(sut));

        explorer.Utxos = [];

        Assert.Empty(await Verified(sut));
    }
}
