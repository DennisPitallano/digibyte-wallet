using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The wallet side of the gateway contract: answers are read strictly, failures throw, request bodies hold only what the contract allows (T053).</summary>
public class GatewayClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Method, string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body));
            return respond(request, body);
        }
    }

    private static P2pGatewayOptions Options() => P2pGatewayOptions.FromConfiguration(k => k == "SpvGateway:ApiUrl" ? "https://gateway.example.org" : null);

    private static GatewayClient Client(Func<HttpRequestMessage, string?, HttpResponseMessage> respond, out Handler handler)
    {
        handler = new Handler(respond);
        return new GatewayClient(new HttpClient(handler), Options());
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Tx = "{\"txid\":\"" + "ab" + "\",\"height\":10,\"blockHash\":\"cd\",\"raw\":\"0100\",\"proof\":{\"index\":2,\"branch\":[\"11\",\"22\"]}}";

    // ---- scan ----

    [Fact]
    public async Task A_scan_sends_addresses_heights_and_outpoints_and_reads_the_proofs()
    {
        var client = Client((_, _) => Json(HttpStatusCode.OK, "{\"tipHeight\":100,\"scannedFrom\":5,\"scannedTo\":50,\"complete\":true,\"transactions\":[" + Tx + "]}"), out var handler);

        var scan = await client.ScanAsync(["dgb1qaaa", "Dbbb"], 5, 50, ["ab:1"]);

        Assert.Equal("POST", handler.Requests.Single().Method);
        Assert.Equal("https://gateway.example.org/v1/scan", handler.Requests.Single().Url);
        using var sent = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.Equal(["addresses", "fromHeight", "knownOutpoints", "toHeight"], sent.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
        Assert.Equal(100, scan.TipHeight);
        Assert.True(scan.Complete);
        var tx = scan.Transactions.Single();
        Assert.Equal((10, 2, "cd"), (tx.Height, tx.Index, tx.BlockHash));
        Assert.Equal(["11", "22"], tx.Branch);
    }

    [Fact]
    public async Task Requests_never_carry_anything_but_the_allowed_fields()
    {
        var client = Client((request, _) => Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("utxos")
            ? "{\"tipHeight\":1,\"utxos\":[]}"
            : "{\"tipHeight\":1,\"scannedFrom\":1,\"scannedTo\":1,\"complete\":true,\"transactions\":[]}"), out var handler);

        await client.ScanAsync(["dgb1qaaa"], 1, 1);
        await client.GetUtxosAsync(["dgb1qaaa"], 1);

        var bodies = string.Join(" ", handler.Requests.Select(r => r.Body));
        foreach (var forbidden in new[] { "xpub", "xprv", "seed", "mnemonic", "privateKey", "passphrase", "password" })
            Assert.DoesNotContain(forbidden, bodies, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task More_than_one_hundred_addresses_or_outpoints_in_one_request_is_a_programming_error()
    {
        var client = Client((_, _) => Json(HttpStatusCode.OK, "{}"), out var handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ScanAsync(Enumerable.Range(0, 101).Select(i => $"a{i}").ToList(), 1, 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ScanAsync(["a"], 1, 2, Enumerable.Range(0, 101).Select(i => $"{i}:0").ToList()));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("{\"error\":\"range_unavailable\",\"message\":\"x\"}", 404, "range_unavailable")]
    [InlineData("{\"error\":\"rate_limited\",\"message\":\"x\",\"retryAfterSeconds\":5}", 429, "rate_limited")]
    [InlineData("{\"error\":\"not_ready\",\"message\":\"x\"}", 503, "not_ready")]
    [InlineData("<html>bad gateway</html>", 502, "unreadable")]
    public async Task A_failure_throws_with_the_gateway_code_and_never_returns_an_empty_scan(string body, int status, string code)
    {
        var client = Client((_, _) => Json((HttpStatusCode)status, body), out _);

        var error = await Assert.ThrowsAsync<GatewayRequestException>(() => client.ScanAsync(["a"], 1, 2));

        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code == "range_unavailable", error.IsRangeUnavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"tipHeight\":1}")]
    [InlineData("{\"tipHeight\":\"x\",\"scannedFrom\":1,\"scannedTo\":1,\"complete\":true,\"transactions\":[]}")]
    public async Task An_answer_that_cannot_be_read_throws_unreadable(string body)
    {
        var client = Client((_, _) => Json(HttpStatusCode.OK, body), out _);

        var error = await Assert.ThrowsAsync<GatewayRequestException>(() => client.ScanAsync(["a"], 1, 2));

        Assert.Equal("unreadable", error.Code);
    }

    [Fact]
    public async Task A_connection_failure_throws_unreachable()
    {
        var client = Client((_, _) => throw new HttpRequestException("refused"), out _);

        var error = await Assert.ThrowsAsync<GatewayRequestException>(() => client.ScanAsync(["a"], 1, 2));

        Assert.Equal("unreachable", error.Code);
    }

    [Fact]
    public async Task Without_a_configured_address_every_call_is_unreachable_and_sends_nothing()
    {
        var handler = new Handler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var client = new GatewayClient(new HttpClient(handler), P2pGatewayOptions.FromConfiguration(_ => null));

        await Assert.ThrowsAsync<GatewayRequestException>(() => client.ScanAsync(["a"], 1, 2));
        Assert.Empty(handler.Requests);
    }

    // ---- utxos, broadcast, proofs ----

    [Fact]
    public async Task Utxos_are_read_with_amount_script_and_proof_position()
    {
        var client = Client((_, _) => Json(HttpStatusCode.OK,
            "{\"tipHeight\":100,\"utxos\":[{\"txid\":\"ab\",\"vout\":1,\"amount\":150000000,\"scriptPubKey\":\"0014\",\"height\":10,\"blockHash\":\"cd\",\"proof\":{\"index\":3,\"branch\":[\"11\"]}}]}"), out _);

        var answer = await client.GetUtxosAsync(["a"], 5);

        var utxo = answer.Utxos.Single();
        Assert.Equal((1, 150000000L, "0014"), (utxo.Vout, utxo.Amount, utxo.ScriptPubKey));
        Assert.Equal((10, 3), (utxo.Proof.Height, utxo.Proof.Index));
    }

    [Fact]
    public async Task A_broadcast_sends_only_the_signed_transaction_as_hex_and_reads_the_outcome()
    {
        var client = Client((_, _) => Json(HttpStatusCode.OK, "{\"txid\":\"ab\",\"relayedTo\":6,\"rejections\":[\"66: low fee\"],\"seenBackFrom\":2,\"outcome\":\"accepted\"}"), out var handler);

        var result = await client.BroadcastAsync([1, 2, 255]);

        using var sent = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.Equal(["raw"], sent.RootElement.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("0102ff", sent.RootElement.GetProperty("raw").GetString());
        Assert.Equal(("accepted", 6, 2), (result.Outcome, result.RelayedTo, result.SeenBackFrom));
        Assert.Equal(["66: low fee"], result.Rejections);
    }

    [Fact]
    public async Task Proofs_are_requested_in_groups_of_at_most_twenty_and_unavailable_items_are_reported()
    {
        var client = Client((_, _) => Json(HttpStatusCode.OK, "{\"proofs\":[" + Tx + "],\"unavailable\":[{\"txid\":\"ef\",\"reason\":\"block_not_served\"}]}"), out _);

        var answer = await client.GetProofsAsync([("ab", 10), ("ef", 11)]);

        Assert.Single(answer.Proofs);
        Assert.Equal(("ef", "block_not_served"), answer.Unavailable.Single());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetProofsAsync(Enumerable.Range(0, 21).Select(i => ("ab", i)).ToList()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetProofsAsync([]));
    }

    // ---- headers ----

    [Fact]
    public async Task Headers_are_read_as_consecutive_80_byte_records_with_the_tip_from_the_response_headers()
    {
        var run = Enumerable.Range(24563101, 3).Select(h => Convert.FromHexString(RealHeaderHex(h))).ToList();
        var client = Client((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(run.SelectMany(r => r).ToArray()) };
            response.Headers.Add("X-Tip-Height", "24563500");
            response.Headers.Add("X-Tip-Hash", new string('a', 64));
            return response;
        }, out var handler);

        var answer = await client.GetHeadersAsync(24563101, 3);

        Assert.Equal("https://gateway.example.org/v1/headers?from=24563101&count=3", handler.Requests.Single().Url);
        Assert.Equal(3, answer.Headers.Count);
        Assert.Equal(BlockHeader80.Parse(run[1]).Hash, answer.Headers[1].Hash);
        Assert.Equal(24563500, answer.TipHeight);
    }

    [Fact]
    public async Task A_headers_body_that_is_not_a_whole_number_of_records_is_unreadable()
    {
        var client = Client((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[81]) };
            response.Headers.Add("X-Tip-Height", "1");
            response.Headers.Add("X-Tip-Hash", new string('a', 64));
            return response;
        }, out _);

        var error = await Assert.ThrowsAsync<GatewayRequestException>(() => client.GetHeadersAsync(1, 1));

        Assert.Equal("unreadable", error.Code);
    }

    private static string RealHeaderHex(int height)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray().First(e => e.GetProperty("height").GetInt32() == height).GetProperty("header").GetString()!;
    }
}
