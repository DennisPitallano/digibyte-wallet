using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Crypto.Addresses;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The Esplora-backed DigiDollar backend, tested against real digiexplorer.info responses saved under Fixtures/esplora:
/// a mainnet mint (parent) of $500.00 and the transfer that spent it into $1.42 and $498.58 (block 24,411,694).
/// </summary>
public class EsploraDigiDollarApiTests
{
    private const string Base = "https://explorer.test/api";
    private const string TransferTxid = "4fb30cbaefd160d07717470ef4dc8a4aa6133718b76908954b050c0620cd0b1b";
    private const string MintTxid = "c59a6716028c720666e7c07c0de63bcca4388f1051013e7e7eb7635e4a6cf011";

    // The two DD output keys in those transactions, and their explorer (bech32m P2TR) addresses.
    private static readonly byte[] KeyA0 = Convert.FromHexString("b42be14e35aeafcd503cfa260e34b61a96845f717988869c504e21e0718a04f6"); // received $1.42
    private static readonly byte[] KeyA1 = Convert.FromHexString("d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f"); // minted $500, change $498.58
    private const string ExplorerA0 = "dgb1pks47zn3446hu65pulgnqud9kr2tgghm30xygd8zsfcs7quv2qnmqrq09d0";
    private const string ExplorerA1 = "dgb1p6yjz43frrhwxghkh2za47zrxvxjec48nxc4sxf2pqerfuc4dkphs9q46j8";

    private static readonly string DdA0 = DigiDollarAddressValidator.Encode(KeyA0, "mainnet");
    private static readonly string DdA1 = DigiDollarAddressValidator.Encode(KeyA1, "mainnet");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "esplora", name));

    private sealed class Explorer : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = new();
        public List<string> Requests { get; } = [];
        public string? LastPostBody { get; private set; }

        public Explorer Add(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            Routes[path] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsoluteUri[Base.Length..];
            Requests.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Post) LastPostBody = await request.Content!.ReadAsStringAsync(ct);
            if (Routes.TryGetValue(path, out var r))
                return new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") };
            // Like the real explorer: an address nobody has used answers with an empty list, anything else is a 404.
            if (path.StartsWith("/address/", StringComparison.Ordinal) && (path.EndsWith("/utxo", StringComparison.Ordinal) || path.EndsWith("/txs", StringComparison.Ordinal)))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") };
        }
    }

    private static Explorer RealChain() => new Explorer()
        .Add("/blocks/tip/height", "24412373")
        .Add($"/address/{ExplorerA0}/utxo", Fixture("utxo_a0.json"))
        .Add($"/address/{ExplorerA1}/utxo", Fixture("utxo_a1.json"))
        .Add($"/address/{ExplorerA0}/txs", Fixture("txs_a0.json"))
        .Add($"/address/{ExplorerA1}/txs", Fixture("txs_a1.json"))
        .Add($"/tx/{TransferTxid}", Fixture("tx_transfer.json"))
        .Add($"/tx/{MintTxid}", Fixture("tx_parent.json"))
        .Add($"/tx/{TransferTxid}/status", Fixture("status_transfer.json"));

    private static EsploraDigiDollarApi Api(Explorer h, bool testnet = false) =>
        new(new HttpClient(h), Base, "https://explorer.test/testnet/api", () => testnet);

    // ---- UTXOs ----

    [Fact]
    public async Task Utxo_AmountComesFromTheTransferOpReturn()
    {
        var result = await Api(RealChain()).GetUtxosAsync([DdA0], 0, default);

        var u = Assert.Single(result[DdA0]);
        Assert.Equal(TransferTxid, u.Txid);
        Assert.Equal(0, u.Vout);
        Assert.Equal(142, u.AmountCents);                       // $1.42
        Assert.Equal(0, u.ValueSats);
        Assert.Equal("5120" + Convert.ToHexString(KeyA0).ToLowerInvariant(), u.ScriptPubKey);
        Assert.Equal(24412373 - 24411694 + 1, u.Confirmations);
        Assert.True(u.IsSpendable);
    }

    [Fact]
    public async Task Utxo_ChangeOutput_IsTheRemainderOfTheMint()
    {
        var result = await Api(RealChain()).GetUtxosAsync([DdA1], 0, default);

        var u = Assert.Single(result[DdA1]);
        Assert.Equal(1, u.Vout);
        Assert.Equal(49858, u.AmountCents);                     // $498.58
    }

    [Fact]
    public async Task Utxo_EveryRequestedAddressHasAnEntry_AndBadOnesAreEmpty()
    {
        var result = await Api(RealChain()).GetUtxosAsync([DdA0, "not-an-address", DigiDollarAddressValidator.Encode(new byte[32].Select((_, i) => (byte)(i + 7)).ToArray(), "mainnet")], 0, default);

        Assert.Equal(3, result.Count);
        Assert.Single(result[DdA0]);
        Assert.Empty(result["not-an-address"]);
    }

    [Fact]
    public async Task Utxo_BelowMinConf_IsFiltered()
    {
        var result = await Api(RealChain()).GetUtxosAsync([DdA0], 1000, default);

        Assert.Empty(result[DdA0]);
    }

    [Fact]
    public async Task Utxo_ZeroValueOutputThatIsNotDigiDollar_IsIgnored()
    {
        // A value-0 output whose transaction is not a DD transaction must not be counted.
        var h = RealChain().Add($"/tx/{TransferTxid}", "{\"txid\":\"" + TransferTxid + "\",\"version\":2,\"vin\":[],\"vout\":[],\"status\":{\"confirmed\":true}}");

        var result = await Api(h).GetUtxosAsync([DdA0], 0, default);

        Assert.Empty(result[DdA0]);
    }

    [Fact]
    public async Task Utxo_QueriesTheNetworksOwnExplorer()
    {
        var h = RealChain();
        await Api(h).GetUtxosAsync([DdA0], 0, default);

        Assert.All(h.Requests, r => Assert.DoesNotContain("/testnet/", r));
        Assert.Contains($"GET /address/{ExplorerA0}/utxo", h.Requests);
    }

    // ---- History ----

    [Fact]
    public async Task History_Transfer_ShowsInputsOutputsAndFee()
    {
        var result = await Api(RealChain()).GetHistoryAsync([DdA1], 50, null, default);

        var transfer = result.Items.Single(e => e.Txid == TransferTxid);
        Assert.Equal(24411694, transfer.BlockHeight);
        Assert.Equal(10_990_000, transfer.FeeSats);
        Assert.Equal(680, transfer.Confirmations);
        var input = Assert.Single(transfer.DdInputs);
        Assert.Equal(DdA1, input.Address);
        Assert.Equal(50000, input.AmountCents);
        Assert.Equal([(DdA0, 142L), (DdA1, 49858L)], transfer.DdOutputs.Select(o => (o.Address, o.AmountCents)).ToArray());
        Assert.Equal(transfer.DdInputs.Sum(i => i.AmountCents), transfer.DdOutputs.Sum(o => o.AmountCents)); // conservation
    }

    [Fact]
    public async Task History_Mint_ShowsTheMintedAmountAndNoDdInputs()
    {
        var result = await Api(RealChain()).GetHistoryAsync([DdA1], 50, null, default);

        var mint = result.Items.Single(e => e.Txid == MintTxid);
        Assert.Empty(mint.DdInputs);
        var output = Assert.Single(mint.DdOutputs);
        Assert.Equal(DdA1, output.Address);
        Assert.Equal(50000, output.AmountCents);                // $500.00
    }

    [Fact]
    public async Task History_IsNewestFirst_AndHonoursCount()
    {
        var all = await Api(RealChain()).GetHistoryAsync([DdA1], 50, null, default);
        Assert.Equal([TransferTxid, MintTxid], all.Items.Select(e => e.Txid).ToArray());

        var one = await Api(RealChain()).GetHistoryAsync([DdA1], 1, null, default);
        Assert.Equal(TransferTxid, Assert.Single(one.Items).Txid);
    }

    [Fact]
    public async Task History_SameTxAcrossTwoAddresses_AppearsOnce()
    {
        var result = await Api(RealChain()).GetHistoryAsync([DdA0, DdA1], 50, null, default);

        Assert.Single(result.Items, e => e.Txid == TransferTxid);
        Assert.Null(result.Next);
    }

    [Fact]
    public async Task History_ContinuationCursor_ReturnsNothingMore()
    {
        var result = await Api(RealChain()).GetHistoryAsync([DdA1], 50, "anything", default);

        Assert.Empty(result.Items);
        Assert.Null(result.Next);
    }

    // ---- Transaction status ----

    [Fact]
    public async Task Status_Confirmed_HasConfirmations()
    {
        var s = await Api(RealChain()).GetTxStatusAsync(TransferTxid, default);

        Assert.Equal(DdTxState.Confirmed, s.State);
        Assert.Equal(680, s.Confirmations);
    }

    [Fact]
    public async Task Status_Mempool_And_Unknown()
    {
        var h = RealChain().Add("/tx/aa/status", "{\"confirmed\":false}");

        Assert.Equal(DdTxState.Mempool, (await Api(h).GetTxStatusAsync("aa", default)).State);
        Assert.Equal(DdTxState.Unknown, (await Api(h).GetTxStatusAsync("bb", default)).State); // 404
    }

    // ---- Broadcast ----

    [Fact]
    public async Task Broadcast_PostsTheRawHex_AndReturnsTheTxid()
    {
        var h = RealChain().Add("/tx", TransferTxid);

        var txid = await Api(h).BroadcastAsync("0200000001", default);

        Assert.Equal(TransferTxid, txid);
        Assert.Equal("0200000001", h.LastPostBody);
    }

    [Fact]
    public async Task Broadcast_Rejection_CarriesTheReason()
    {
        var h = RealChain().Add("/tx", "sendrawtransaction RPC error: bad-txns-inputs-missingorspent", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<DdApiException>(() => Api(h).BroadcastAsync("00", default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("bad-txns-inputs-missingorspent", ex.Detail);
    }

    [Fact]
    public async Task Broadcast_ServerError_IsBackendUnavailable()
    {
        var h = RealChain().Add("/tx", "oops", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<DdBackendUnavailableException>(() => Api(h).BroadcastAsync("00", default));
    }

    // ---- Health ----

    [Fact]
    public async Task Health_Mainnet_AfterActivation_IsActive_WithoutOracleData()
    {
        var health = await Api(RealChain()).GetHealthAsync(default);

        Assert.True(health.Available);
        Assert.True(health.DeploymentActive);
        Assert.Equal(24412373, health.NodeHeight);
        Assert.False(health.OraclePriceAvailable);              // an explorer has no oracle
        Assert.False(health.ErrActive);
        Assert.False(health.VolatilityFreeze);
    }

    [Fact]
    public async Task Health_Mainnet_BeforeActivation_IsNotActive()
    {
        var h = new Explorer().Add("/blocks/tip/height", "23000000");

        Assert.False((await Api(h).GetHealthAsync(default)).DeploymentActive);
    }

    [Fact]
    public async Task Health_Testnet_UsesTheTestnetExplorer()
    {
        var requests = new List<string>();
        var handler = new RecordingHandler(requests, "5000");

        var health = await new EsploraDigiDollarApi(new HttpClient(handler), Base, "https://explorer.test/testnet/api", () => true).GetHealthAsync(default);

        Assert.True(health.Available);
        Assert.True(health.DeploymentActive);
        Assert.Contains("https://explorer.test/testnet/api/blocks/tip/height", requests);
    }

    private sealed class RecordingHandler(List<string> requests, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Health_Unreachable_IsUnavailable_NotAnException()
    {
        var h = new Explorer().Add("/blocks/tip/height", "boom", HttpStatusCode.ServiceUnavailable);

        Assert.False((await Api(h).GetHealthAsync(default)).Available);
    }

    [Fact]
    public async Task Utxos_ExplorerDown_IsBackendUnavailable()
    {
        var h = new Explorer().Add("/blocks/tip/height", "boom", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<DdBackendUnavailableException>(() => Api(h).GetUtxosAsync([DdA0], 0, default));
    }

    [Fact]
    public async Task Cancellation_IsRespected()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(RealChain()).GetUtxosAsync([DdA0], 0, cts.Token));
    }

    // ---- Transaction parsing ----

    [Fact]
    public void Parse_RealMint_UsesTheFirstAmountForItsSingleDdOutput()
    {
        var tx = EsploraDigiDollarApi.ParseDdTx(JsonDocument.Parse(Fixture("tx_parent.json")).RootElement);

        Assert.NotNull(tx);
        Assert.Equal(DigiByte.Crypto.Transactions.DigiDollarTxType.Mint, tx!.Type);
        var o = Assert.Single(tx.Outputs);
        Assert.Equal(1, o.Vout);
        Assert.Equal(50000, o.AmountCents);
    }

    [Fact]
    public void Parse_RealTransfer_PairsEachDdOutputWithItsAmount()
    {
        var tx = EsploraDigiDollarApi.ParseDdTx(JsonDocument.Parse(Fixture("tx_transfer.json")).RootElement);

        Assert.NotNull(tx);
        Assert.Equal([(0, 142L), (1, 49858L)], tx!.Outputs.Select(o => (o.Vout, o.AmountCents)).ToArray());
    }

    [Theory]
    [InlineData("{\"txid\":\"a\",\"version\":2,\"vout\":[]}")]                 // an ordinary transaction
    [InlineData("{\"txid\":\"a\",\"version\":33556336,\"vout\":[]}")]           // DD version but no DD OP_RETURN
    [InlineData("{\"txid\":\"a\"}")]                                            // no version
    public void Parse_NonDigiDollar_IsNull(string json) =>
        Assert.Null(EsploraDigiDollarApi.ParseDdTx(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void Parse_AmountCountMismatch_IsNull()
    {
        // The real transfer, with one DD output hidden by giving it a non-zero value: now 1 output but 2 amounts.
        var original = Fixture("tx_transfer.json");
        var at = original.IndexOf("\"value\":0}", original.IndexOf("\"vout\":[", StringComparison.Ordinal), StringComparison.Ordinal);
        var text = original[..at] + "\"value\":1}" + original[(at + "\"value\":0}".Length)..];

        Assert.Null(EsploraDigiDollarApi.ParseDdTx(JsonDocument.Parse(text).RootElement));
    }
}
