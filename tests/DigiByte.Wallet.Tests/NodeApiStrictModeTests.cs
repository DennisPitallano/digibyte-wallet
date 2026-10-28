using System.Net;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T057: with strict mode on, a failed node request must surface as an exception (so the fallback service can try an explorer and
/// count the failure), never as a zero balance, empty list, missing transaction, default fee or height 0. With strict mode off
/// (the default) every method keeps exactly today's lenient results, so behaviour with the feature off is unchanged.
/// </summary>
public class NodeApiStrictModeTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static NodeApiBlockchainService Node(Func<HttpRequestMessage, HttpResponseMessage> respond, bool strict) =>
        new(new HttpClient(new Handler(respond)), "http://fake-node:5260", strict);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    // ---------- the failure modes ----------

    public static IEnumerable<object[]> FailureModes() =>
    [
        ["transport error", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("connection refused"))],
        ["timeout", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new TaskCanceledException("timed out"))],
        ["http 500", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"))],
        ["http 503", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"node down\"}"))],
        ["unreadable body", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.OK, "<html>bad gateway</html>"))],
        ["empty object", (Func<HttpRequestMessage, HttpResponseMessage>)(_ => Json(HttpStatusCode.OK, "{}"))],
    ];

    private static readonly (string Name, Func<NodeApiBlockchainService, Task> Call)[] Operations =
    [
        ("balance", n => n.GetBalanceAsync("DAbc")),
        ("balance batch", n => n.GetBalanceAsync(new[] { "DAbc", "DDef" })),
        ("utxos", n => n.GetUtxosAsync("DAbc")),
        ("utxos batch", n => n.GetUtxosAsync(new[] { "DAbc", "DDef" })),
        ("fee", n => n.GetFeeRateAsync()),
        ("height", n => n.GetBlockHeightAsync()),
    ];

    public static IEnumerable<object[]> StrictFailures()
    {
        foreach (var op in Operations)
            foreach (var mode in FailureModes())
                yield return [op.Name, mode[0], mode[1]];
    }

    [Theory, MemberData(nameof(StrictFailures))]
    public async Task Strict_FailedRead_Throws(string operation, string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var call = Operations.First(o => o.Name == operation).Call;

        await Assert.ThrowsAnyAsync<Exception>(() => call(Node(respond, strict: true)));
        _ = mode;
    }

    [Theory, MemberData(nameof(FailureModes))]
    public async Task Strict_TransactionLookup_ThrowsOnFailure(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Node(respond, strict: true).GetTransactionAsync("abc"));
        _ = mode;
    }

    [Theory, MemberData(nameof(FailureModes))]
    public async Task Strict_Broadcast_ThrowsOnFailure(string mode, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Node(respond, strict: true).BroadcastTransactionAsync([1, 0, 0, 0]));
        _ = mode;
    }

    // ---------- not found is the one thing that is not a failure ----------

    [Fact]
    public async Task Strict_TransactionLookup_Http404_IsNull()
    {
        var node = Node(_ => Json(HttpStatusCode.NotFound, "{}"), strict: true);

        Assert.Null(await node.GetTransactionAsync("abc"));
    }

    [Fact]
    public async Task Strict_TransactionLookup_NodeNotFoundError_IsNull()
    {
        // The node API answers 400 with the node's own "no such transaction" code (-5) for an unknown transaction.
        var node = Node(_ => Json(HttpStatusCode.BadRequest, "{\"error\":\"No such mempool or blockchain transaction\",\"code\":-5}"), strict: true);

        Assert.Null(await node.GetTransactionAsync("abc"));
    }

    [Fact]
    public async Task Strict_TransactionLookup_OtherBadRequest_StillThrows()
    {
        var node = Node(_ => Json(HttpStatusCode.BadRequest, "{\"error\":\"bad\",\"code\":-8}"), strict: true);

        await Assert.ThrowsAnyAsync<Exception>(() => node.GetTransactionAsync("abc"));
    }

    // ---------- strict still returns real answers, including real zeros ----------

    [Fact]
    public async Task Strict_RealAnswers_AreReturned()
    {
        var node = Node(req => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/balance") => Json(HttpStatusCode.OK, "{\"balanceSatoshis\":123456}"),
            var p when p.EndsWith("/utxos") => Json(HttpStatusCode.OK,
                "{\"utxos\":[{\"txid\":\"aa\",\"vout\":1,\"amountSatoshis\":500,\"scriptPubKey\":\"76\",\"height\":10,\"confirmations\":3}]}"),
            "/api/network/fee/6" => Json(HttpStatusCode.OK, "{\"feerate\":0.1001}"),
            "/api/blockchain/height" => Json(HttpStatusCode.OK, "{\"height\":24465504}"),
            "/api/tx/abc" => Json(HttpStatusCode.OK,
                "{\"txid\":\"abc\",\"confirmations\":5,\"vin\":[],\"vout\":[{\"value\":1.5,\"scriptPubKey\":{\"address\":\"D1\",\"hex\":\"76\"}}]}"),
            _ => Json(HttpStatusCode.NotFound, "{}"),
        }, strict: true);

        Assert.Equal(123456, await node.GetBalanceAsync("DAbc"));
        var utxo = Assert.Single(await node.GetUtxosAsync("DAbc"));
        Assert.Equal("aa", utxo.TxId);
        Assert.Equal(500, utxo.AmountSatoshis);
        Assert.Equal(0.1001m, await node.GetFeeRateAsync());
        Assert.Equal(24465504, await node.GetBlockHeightAsync());
        var tx = await node.GetTransactionAsync("abc");
        Assert.Equal("abc", tx!.TxId);
        Assert.Equal(150_000_000, Assert.Single(tx.Outputs).AmountSatoshis);
    }

    [Fact]
    public async Task Strict_ARealZeroBalance_IsStillZero()
    {
        var node = Node(_ => Json(HttpStatusCode.OK, "{\"balanceSatoshis\":0}"), strict: true);

        Assert.Equal(0, await node.GetBalanceAsync("DAbc"));
    }

    [Fact]
    public async Task Strict_HeightZeroFromTheNode_IsAFailure()
    {
        var node = Node(_ => Json(HttpStatusCode.OK, "{\"height\":0}"), strict: true);

        await Assert.ThrowsAnyAsync<Exception>(() => node.GetBlockHeightAsync());
    }

    [Fact]
    public async Task Strict_BatchBalance_DoesNotFallBackToSequentialZeros()
    {
        var calls = 0;
        var node = Node(_ => { calls++; return Json(HttpStatusCode.InternalServerError, "{}"); }, strict: true);

        await Assert.ThrowsAnyAsync<Exception>(() => node.GetBalanceAsync(new[] { "DAbc", "DDef", "DGhi" }));
        Assert.Equal(1, calls);   // one batch request, no per-address retries that could end in zeros
    }

    [Fact]
    public async Task Strict_AddressHistory_IsNotSupported()
    {
        var node = Node(_ => Json(HttpStatusCode.OK, "{}"), strict: true);

        await Assert.ThrowsAsync<NotSupportedException>(() => node.GetAddressTransactionsAsync("DAbc"));
    }

    // ---------- lenient (default) behaviour is exactly today's ----------

    [Fact]
    public async Task Lenient_IsTheDefault_AndKeepsTodaysNeutralResults()
    {
        var node = new NodeApiBlockchainService(
            new HttpClient(new Handler(_ => throw new HttpRequestException("down"))), "http://fake-node:5260");

        Assert.Equal(0, await node.GetBalanceAsync("DAbc"));
        Assert.Equal(0, await node.GetBalanceAsync(new[] { "DAbc" }));
        Assert.Empty(await node.GetUtxosAsync("DAbc"));
        Assert.Empty(await node.GetUtxosAsync(new[] { "DAbc" }));
        Assert.Null(await node.GetTransactionAsync("abc"));
        Assert.Equal(0.00001m, await node.GetFeeRateAsync());
        Assert.Equal(0, await node.GetBlockHeightAsync());
        Assert.Empty(await node.GetAddressTransactionsAsync("DAbc"));
    }

    [Fact]
    public async Task Lenient_ExplicitFalse_BehavesTheSameAsTheDefault()
    {
        var node = Node(_ => Json(HttpStatusCode.InternalServerError, "{}"), strict: false);

        Assert.Equal(0, await node.GetBalanceAsync("DAbc"));
        Assert.Equal(0, await node.GetBlockHeightAsync());
        Assert.Equal(0.00001m, await node.GetFeeRateAsync());
    }
}
