using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T017: the wallet's DigiDollar NodeApi client (FR-003, FR-023, research R14). It maps an unreachable or
/// unavailable backend to <c>Available = false</c> / a typed exception, keeps addresses out of URLs, and never
/// calls a wallet-scoped endpoint.
/// </summary>
public class DigiDollarNodeApiClientTests
{
    private const string Base = "https://node.example";
    private const string Addr1 = "TDaddressNumberOne000000000000000000000000000";
    private const string Addr2 = "TDaddressNumberTwo000000000000000000000000000";

    private static readonly string[] WalletScopedPaths =
    [
        "/api/digidollar/address/new", "/api/digidollar/addresses", "/api/digidollar/balance", "/api/digidollar/unspent",
        "/api/digidollar/transactions", "/api/digidollar/send", "/api/digidollar/send-many", "/api/digidollar/mint",
        "/api/digidollar/redeem", "/api/digidollar/positions",
    ];

    private sealed record Seen(HttpMethod Method, string PathAndQuery, string? Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); // like a real handler
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.Method, request.RequestUri!.PathAndQuery, body));
            return respond(request);
        }
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw ex;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static DigiDollarNodeApiClient Client(HttpMessageHandler h, string baseUrl = Base) =>
        new(new HttpClient(h), baseUrl);

    // ---------- health ----------

    private const string HealthyJson = """
        { "enabled": true, "deploymentActive": true, "nodeHeight": 1000, "networkHeight": 1003,
          "oracle": { "priceMicroUsd": 6310, "height": 995 },
          "protection": { "errActive": false, "volatilityFreeze": true, "systemCollateralPercent": 187 },
          "indexer": { "height": 1000, "lagBlocks": 0 } }
        """;

    [Fact]
    public async Task Health_MapsAllFields()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, HealthyJson));

        var health = await Client(h).GetHealthAsync(default);

        Assert.True(health.Available);
        Assert.True(health.DeploymentActive);
        Assert.Equal(1000, health.NodeHeight);
        Assert.Equal(1003, health.NetworkHeight);
        Assert.Equal(6310, health.OraclePriceMicroUsd);
        Assert.Equal(995, health.OracleHeight);
        Assert.False(health.ErrActive);
        Assert.True(health.VolatilityFreeze);
        Assert.True(health.OraclePriceAvailable);
        Assert.True(health.IsDegraded); // volatility freeze
        Assert.Equal(HttpMethod.Get, h.Requests[0].Method);
        Assert.Equal("/api/digidollar/health", h.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task Health_NullOracle_MeansNoPrice_AndDegraded()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "enabled": true, "deploymentActive": true, "nodeHeight": 5, "oracle": null, "protection": {} }"""));

        var health = await Client(h).GetHealthAsync(default);

        Assert.True(health.Available);
        Assert.False(health.OraclePriceAvailable);
        Assert.True(health.IsDegraded);
        Assert.Null(health.OracleHeight);
    }

    [Fact]
    public async Task Health_DeploymentNotActive_IsMapped()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "enabled": true, "deploymentActive": false, "nodeHeight": 5, "oracle": null }"""));

        var health = await Client(h).GetHealthAsync(default);

        Assert.True(health.Available);
        Assert.False(health.DeploymentActive);
    }

    [Fact]
    public async Task Health_Disabled_IsUnavailable()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "enabled": false, "message": "DigiDollar not enabled" }"""));
        Assert.False((await Client(h).GetHealthAsync(default)).Available);
    }

    [Fact]
    public async Task Health_503_IsUnavailable()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, """{ "error": "digidollar_disabled" }"""));
        Assert.False((await Client(h).GetHealthAsync(default)).Available);
    }

    [Fact]
    public async Task Health_NetworkFailure_IsUnavailable() =>
        Assert.False((await Client(new ThrowingHandler(new HttpRequestException("boom"))).GetHealthAsync(default)).Available);

    [Fact]
    public async Task Health_Timeout_IsUnavailable() =>
        Assert.False((await Client(new ThrowingHandler(new TaskCanceledException("timeout"))).GetHealthAsync(default)).Available);

    [Fact]
    public async Task Health_MalformedJson_IsUnavailable()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, "<html>bad gateway</html>"));
        Assert.False((await Client(h).GetHealthAsync(default)).Available);
    }

    [Fact]
    public async Task Health_CallerCancellation_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, HealthyJson));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(h).GetHealthAsync(cts.Token));
    }

    // ---------- utxos ----------

    [Fact]
    public async Task Utxos_PostsAddressesInTheBody_NeverInTheUrl_AndParsesTheShape()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""
            { "{{Addr1}}": [ { "txid": "aa", "vout": 1, "amountCents": 2500, "scriptPubKey": "5120ff", "valueSats": 0,
                              "confirmations": 3, "spentByMempool": true } ],
              "{{Addr2}}": [] }
            """));

        var result = await Client(h).GetUtxosAsync([Addr1, Addr2], minConf: 1, default);

        var req = h.Requests.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/api/digidollar/addresses/utxos", req.PathAndQuery);
        Assert.DoesNotContain("TD", req.PathAndQuery);
        using var body = JsonDocument.Parse(req.Body!);
        Assert.Equal([Addr1, Addr2], body.RootElement.GetProperty("addresses").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, body.RootElement.GetProperty("minConf").GetInt32());

        var u = Assert.Single(result[Addr1]);
        Assert.Equal("aa", u.Txid);
        Assert.Equal(1, u.Vout);
        Assert.Equal(Addr1, u.Address);
        Assert.Equal(2500, u.AmountCents);
        Assert.Equal("5120ff", u.ScriptPubKey);
        Assert.Equal(0, u.ValueSats);
        Assert.Equal(3, u.Confirmations);
        Assert.True(u.SpentByMempool);
        Assert.Empty(result[Addr2]);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Utxos_ServerUnavailable_ThrowsBackendUnavailable(HttpStatusCode status)
    {
        var h = new StubHandler(_ => Json(status, """{ "error": "indexer_behind" }"""));
        await Assert.ThrowsAsync<DdBackendUnavailableException>(() => Client(h).GetUtxosAsync([Addr1], 0, default));
    }

    [Fact]
    public async Task Utxos_NetworkFailureAndTimeout_ThrowBackendUnavailable()
    {
        await Assert.ThrowsAsync<DdBackendUnavailableException>(() =>
            Client(new ThrowingHandler(new HttpRequestException("down"))).GetUtxosAsync([Addr1], 0, default));
        await Assert.ThrowsAsync<DdBackendUnavailableException>(() =>
            Client(new ThrowingHandler(new TaskCanceledException("timeout"))).GetUtxosAsync([Addr1], 0, default));
    }

    [Fact]
    public async Task Utxos_400And413_SurfaceAsTypedApiErrors()
    {
        var bad = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{ "error": "invalid_address", "detail": ["x"] }"""));
        var ex = await Assert.ThrowsAsync<DdApiException>(() => Client(bad).GetUtxosAsync([Addr1], 0, default));
        Assert.Equal("invalid_address", ex.Code);
        Assert.Equal(400, ex.StatusCode);

        var tooMany = new StubHandler(_ => Json(HttpStatusCode.RequestEntityTooLarge, """{ "error": "too_many_addresses" }"""));
        Assert.Equal("too_many_addresses", (await Assert.ThrowsAsync<DdApiException>(() =>
            Client(tooMany).GetUtxosAsync([Addr1], 0, default))).Code);
    }

    [Fact]
    public async Task Utxos_EmptyAddressList_ShortCircuits_WithoutARequest()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var result = await Client(h).GetUtxosAsync([], 0, default);
        Assert.Empty(result);
        Assert.Empty(h.Requests);
    }

    // ---------- history ----------

    [Fact]
    public async Task History_PostsBodyOnly_AndParsesItems()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""
            { "items": [
                { "txid": "t1", "blockHeight": 900, "blockTime": "2023-11-14T22:13:20Z", "confirmations": 101, "feeSats": 10000000,
                  "ddInputs": [ { "address": "{{Addr1}}", "amountCents": 2500 } ],
                  "ddOutputs": [ { "address": "{{Addr2}}", "amountCents": 1000 }, { "address": "{{Addr1}}", "amountCents": 1500 } ] },
                { "txid": "t2", "blockHeight": null, "blockTime": null, "confirmations": 0, "feeSats": null,
                  "ddInputs": [], "ddOutputs": [ { "address": "{{Addr1}}", "amountCents": 99 } ] } ],
              "next": "t2" }
            """));

        var page = await Client(h).GetHistoryAsync([Addr1], count: 25, before: "tprev", default);

        var req = h.Requests.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/api/digidollar/addresses/history", req.PathAndQuery);
        using var body = JsonDocument.Parse(req.Body!);
        Assert.Equal(25, body.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("tprev", body.RootElement.GetProperty("before").GetString());

        Assert.Equal("t2", page.Next);
        var first = page.Items[0];
        Assert.Equal(900, first.BlockHeight);
        Assert.Equal(DateTimeOffset.Parse("2023-11-14T22:13:20Z"), first.BlockTime);
        Assert.Equal(10_000_000, first.FeeSats);
        Assert.Equal(new DdAmountAt(Addr1, 2500), first.DdInputs[0]);
        Assert.Equal(2, first.DdOutputs.Count);
        var pending = page.Items[1];
        Assert.Null(pending.BlockHeight);
        Assert.Null(pending.BlockTime);
        Assert.Null(pending.FeeSats);
        Assert.Equal(0, pending.Confirmations);
    }

    [Fact]
    public async Task History_Unavailable_Throws()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        await Assert.ThrowsAsync<DdBackendUnavailableException>(() => Client(h).GetHistoryAsync([Addr1], 10, null, default));
    }

    // ---------- tx status ----------

    [Theory]
    [InlineData("mempool", DdTxState.Mempool, 0)]
    [InlineData("confirmed", DdTxState.Confirmed, 4)]
    [InlineData("unknown", DdTxState.Unknown, 0)]
    public async Task TxStatus_MapsTheStates(string wire, DdTxState expected, int confirmations)
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""{ "status": "{{wire}}", "confirmations": {{confirmations}} }"""));

        var s = await Client(h).GetTxStatusAsync("abc123", default);

        Assert.Equal(new DdTxStatusResult(expected, confirmations), s);
        Assert.Equal(HttpMethod.Get, h.Requests[0].Method);
        Assert.Equal("/api/digidollar/tx/abc123/status", h.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task TxStatus_UnknownWireValue_IsUnknown_NotAnError()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "status": "something-new", "confirmations": 0 }"""));
        Assert.Equal(DdTxState.Unknown, (await Client(h).GetTxStatusAsync("t", default)).State);
    }

    [Fact]
    public async Task TxStatus_Unavailable_Throws_SoTheAbsenceTimerDoesNotAdvance()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        await Assert.ThrowsAsync<DdBackendUnavailableException>(() => Client(h).GetTxStatusAsync("t", default));
    }

    [Fact]
    public async Task TxStatus_Encodes_TheTxidSegment()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "status": "unknown", "confirmations": 0 }"""));
        await Client(h).GetTxStatusAsync("a/b?c", default);
        Assert.DoesNotContain("a/b?c", h.Requests[0].PathAndQuery);
    }

    // ---------- broadcast ----------

    [Fact]
    public async Task Broadcast_PostsHex_ToTheSharedBroadcastEndpoint_AndReturnsTheTxid()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "txid": "deadbeef" }"""));

        var txid = await Client(h).BroadcastAsync("02000770aa", default);

        Assert.Equal("deadbeef", txid);
        var req = h.Requests.Single();
        Assert.Equal("/api/tx/broadcast", req.PathAndQuery);
        Assert.Equal("02000770aa", JsonDocument.Parse(req.Body!).RootElement.GetProperty("hex").GetString());
    }

    [Fact]
    public async Task Broadcast_Rejection_SurfacesTheNodeReason()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{ "error": "transfer-dd-conservation-violation", "code": -26 }"""));

        var ex = await Assert.ThrowsAsync<DdApiException>(() => Client(h).BroadcastAsync("00", default));

        Assert.Equal("transfer-dd-conservation-violation", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Broadcast_Unavailable_ThrowsBackendUnavailable()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, """{ "error": "node down" }"""));
        await Assert.ThrowsAsync<DdBackendUnavailableException>(() => Client(h).BroadcastAsync("00", default));
    }

    // ---------- cross-cutting guarantees ----------

    [Fact]
    public async Task NeverCallsAWalletScopedEndpoint_AndNeverPutsAnAddressInAUrl()
    {
        var h = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/digidollar/health" => Json(HttpStatusCode.OK, HealthyJson),
            "/api/digidollar/addresses/utxos" => Json(HttpStatusCode.OK, "{}"),
            "/api/digidollar/addresses/history" => Json(HttpStatusCode.OK, """{ "items": [], "next": null }"""),
            "/api/tx/broadcast" => Json(HttpStatusCode.OK, """{ "txid": "t" }"""),
            _ => Json(HttpStatusCode.OK, """{ "status": "unknown", "confirmations": 0 }"""),
        });
        var client = Client(h);

        await client.GetHealthAsync(default);
        await client.GetUtxosAsync([Addr1, Addr2], 0, default);
        await client.GetHistoryAsync([Addr1], 10, null, default);
        await client.GetTxStatusAsync("abc", default);
        await client.BroadcastAsync("00", default);

        Assert.Equal(5, h.Requests.Count);
        foreach (var r in h.Requests)
        {
            Assert.DoesNotContain(WalletScopedPaths, p => r.PathAndQuery.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                                                          && !r.PathAndQuery.StartsWith("/api/digidollar/addresses/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(Addr1, r.PathAndQuery);
            Assert.DoesNotContain(Addr2, r.PathAndQuery);
        }
    }

    [Theory]
    [InlineData("https://node.example")]
    [InlineData("https://node.example/")]
    public async Task BaseUrl_TrailingSlash_IsHandled(string baseUrl)
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, HealthyJson));
        await Client(h, baseUrl).GetHealthAsync(default);
        Assert.Equal("/api/digidollar/health", h.Requests[0].PathAndQuery);
    }
}
