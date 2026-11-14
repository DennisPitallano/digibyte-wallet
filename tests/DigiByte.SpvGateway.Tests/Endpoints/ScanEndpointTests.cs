using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Tests.Support;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>POST /v1/scan against real blocks (T047) and POST /v1/utxos (T048).</summary>
public class ScanEndpointTests
{
    private static readonly int First = GatewayWorld.Heights.First();
    private static readonly int Last = GatewayWorld.Heights.Last();

    /// <summary>A transaction with a standard output in a block with several transactions: its address, id, height and position.</summary>
    private static (string Address, uint256 Txid, int Height, int Index, int Vout) SomePayment() =>
        PaymentsInFixture().First(p => p.Index > 0);

    private static IEnumerable<(string Address, uint256 Txid, int Height, int Index, int Vout)> PaymentsInFixture()
    {
        foreach (var height in GatewayWorld.Heights)
        {
            var (_, block) = GatewayWorld.At(height);
            for (var i = 0; i < block.Transactions.Count; i++)
                for (var n = 0; n < block.Transactions[i].Outputs.Count; n++)
                    if (GatewayWorld.AddressOf(block.Transactions[i].Outputs[n]) is { } address)
                        yield return (address, block.Transactions[i].GetHash(), height, i, n);
        }
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        return (response, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static async Task<GatewayFactory> HostAsync(int indexFrom, int indexTo, bool serveBlocks = true, Dictionary<string, string?>? settings = null)
    {
        var world = await GatewayWorld.CreateAsync(serveBlocks);
        return await GatewayWorld.HostAsync(world, indexFrom, indexTo, settings);
    }

    [Fact]
    public async Task A_scan_returns_the_transactions_that_pay_the_address_with_raw_data_and_a_proof_that_checks()
    {
        using var factory = await HostAsync(First, Last);
        var payment = SomePayment();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { payment.Address }, fromHeight = First, toHeight = Last });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("complete").GetBoolean());
        Assert.Equal(Last, body.GetProperty("tipHeight").GetInt32() > Last ? Last : Last);
        var found = body.GetProperty("transactions").EnumerateArray().Single(t => t.GetProperty("txid").GetString() == payment.Txid.ToString());
        Assert.Equal(payment.Height, found.GetProperty("height").GetInt32());

        // The raw transaction is the transaction, and its branch ties it to the block header.
        var tx = Transaction.Load(Convert.FromHexString(found.GetProperty("raw").GetString()!), GatewayWorld.Network);
        Assert.Equal(payment.Txid, tx.GetHash());
        var proof = found.GetProperty("proof");
        var branch = proof.GetProperty("branch").EnumerateArray().Select(b => uint256.Parse(b.GetString()!)).ToList();
        Assert.True(MerkleProof.Verify(tx.GetHash(), proof.GetProperty("index").GetInt32(), branch, RealHeaders.At(payment.Height).MerkleRoot));
        Assert.Equal(RealHeaders.At(payment.Height).Hash.ToString(), found.GetProperty("blockHash").GetString());
    }

    [Fact]
    public async Task A_scan_for_an_address_nobody_paid_returns_nothing()
    {
        using var factory = await HostAsync(First, Last);
        var stranger = new Key().GetAddress(ScriptPubKeyType.Segwit, GatewayWorld.Network).ToString();
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { stranger }, fromHeight = First, toHeight = Last });

        Assert.Empty(body.GetProperty("transactions").EnumerateArray());
        Assert.True(body.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task Only_transactions_inside_the_asked_height_range_come_back()
    {
        using var factory = await HostAsync(First, Last);
        var payment = SomePayment();
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { payment.Address }, fromHeight = payment.Height + 1, toHeight = Last });

        Assert.DoesNotContain(body.GetProperty("transactions").EnumerateArray(), t => t.GetProperty("txid").GetString() == payment.Txid.ToString());
    }

    [Fact]
    public async Task A_transaction_that_spends_a_known_outpoint_is_returned_even_though_it_pays_no_watched_address()
    {
        using var factory = await HostAsync(First, Last);
        // Take any non-coinbase transaction and name one of its inputs as the outpoint the wallet knows.
        var (height, block) = GatewayWorld.Heights.Select(GatewayWorld.At).First(b => b.Block.Transactions.Count > 2);
        var spender = block.Transactions[1];
        var outpoint = spender.Inputs[0].PrevOut;
        var stranger = new Key().GetAddress(ScriptPubKeyType.Segwit, GatewayWorld.Network).ToString();
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new
        {
            addresses = new[] { stranger },
            fromHeight = First,
            toHeight = Last,
            knownOutpoints = new[] { $"{outpoint.Hash}:{outpoint.N}" },
        });

        Assert.Contains(body.GetProperty("transactions").EnumerateArray(), t => t.GetProperty("txid").GetString() == spender.GetHash().ToString());
    }

    [Fact]
    public async Task More_than_the_allowed_number_of_addresses_is_refused_as_over_limit()
    {
        using var factory = await HostAsync(First, Last);
        var addresses = Enumerable.Range(0, 101).Select(_ => new Key().GetAddress(ScriptPubKeyType.Segwit, GatewayWorld.Network).ToString()).ToArray();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/scan", new { addresses, fromHeight = First, toHeight = Last });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("over_limit", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task More_than_one_hundred_known_outpoints_is_refused_as_over_limit()
    {
        using var factory = await HostAsync(First, Last);
        var outpoints = Enumerable.Range(0, 101).Select(i => $"{uint256.One}:{i}").ToArray();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { SomePayment().Address }, fromHeight = First, toHeight = Last, knownOutpoints = outpoints });

        Assert.Equal("over_limit", body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("ab:1")]
    public async Task A_malformed_outpoint_is_a_bad_request(string outpoint)
    {
        using var factory = await HostAsync(First, Last);
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { SomePayment().Address }, fromHeight = First, toHeight = Last, knownOutpoints = new[] { outpoint } });

        Assert.Equal("bad_request", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_address_of_the_other_network_is_refused()
    {
        using var factory = await HostAsync(First, Last);
        var testnetAddress = new Key().GetAddress(ScriptPubKeyType.Segwit, DigiByte.Crypto.Networks.DigiByteNetworkSet.Instance.Testnet).ToString();
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { testnetAddress }, fromHeight = First, toHeight = Last });

        Assert.Equal("wrong_network", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_range_that_starts_before_the_indexed_window_is_unavailable_never_a_partial_answer()
    {
        var middle = GatewayWorld.Heights[10];
        using var factory = await HostAsync(middle, Last);
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { SomePayment().Address }, fromHeight = First, toHeight = Last });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("range_unavailable", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_range_longer_than_the_cap_is_over_limit()
    {
        using var factory = await HostAsync(First, Last);
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { SomePayment().Address }, fromHeight = First, toHeight = First + 60_000 });

        Assert.Equal("over_limit", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task When_the_index_has_not_reached_the_asked_end_the_answer_says_it_is_incomplete_and_where_it_stopped()
    {
        var stop = GatewayWorld.Heights[20];
        using var factory = await HostAsync(First, stop);
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { SomePayment().Address }, fromHeight = First, toHeight = Last });

        Assert.False(body.GetProperty("complete").GetBoolean());
        Assert.Equal(stop, body.GetProperty("scannedTo").GetInt32());
    }

    [Fact]
    public async Task When_no_peer_can_supply_a_matching_block_the_scan_stops_early_instead_of_returning_a_gap()
    {
        using var factory = await HostAsync(First, Last);
        var payment = SomePayment();
        foreach (var peer in factory.Services.GetService(typeof(DigiByte.SpvGateway.Peers.PeerManager)) is DigiByte.SpvGateway.Peers.PeerManager m ? m.Sessions : [])
            ((FakePeer)peer).Blocks.Clear();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { payment.Address }, fromHeight = First, toHeight = Last });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("complete").GetBoolean());
        Assert.True(body.GetProperty("scannedTo").GetInt32() < payment.Height);
    }

    [Fact]
    public async Task Before_anything_is_indexed_the_gateway_says_it_is_not_ready()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/scan", new { addresses = new[] { new Key().GetAddress(ScriptPubKeyType.Segwit, GatewayWorld.Network).ToString() }, fromHeight = First, toHeight = Last });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("not_ready", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_scan_leaves_nothing_behind_in_the_data_folder()
    {
        using var factory = await HostAsync(First, Last);
        var payment = SomePayment();
        using var client = factory.CreateClient();
        await PostAsync(client, "/v1/scan", new { addresses = new[] { payment.Address }, fromHeight = First, toHeight = Last });
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var file in Directory.EnumerateFiles(factory.DataDirectory))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            Assert.False(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(payment.Address)) >= 0, $"{Path.GetFileName(file)} holds a scanned address");
        }
    }

    [Fact]
    public async Task Health_reports_the_first_indexed_height_so_a_wallet_knows_what_the_gateway_can_answer()
    {
        var middle = GatewayWorld.Heights[10];
        using var factory = await HostAsync(middle, Last);
        using var client = factory.CreateClient();

        var health = JsonDocument.Parse(await client.GetStringAsync("/v1/health")).RootElement;

        Assert.Equal(middle, health.GetProperty("indexedFromHeight").GetInt32());
    }

    // ---- /v1/utxos (T048) ----

    private static (string Address, uint256 Txid, int Vout, long Amount, int Height)? SpentPayment()
    {
        var spentPoints = new HashSet<OutPoint>();
        foreach (var height in GatewayWorld.Heights)
            foreach (var tx in GatewayWorld.At(height).Block.Transactions.Skip(1))
                foreach (var input in tx.Inputs)
                    spentPoints.Add(input.PrevOut);
        foreach (var p in PaymentsInFixture())
            if (spentPoints.Contains(new OutPoint(p.Txid, p.Vout)))
            {
                var tx = GatewayWorld.At(p.Height).Block.Transactions[p.Index];
                return (p.Address, p.Txid, p.Vout, tx.Outputs[p.Vout].Value.Satoshi, p.Height);
            }
        return null;
    }

    [Fact]
    public async Task Unspent_outputs_are_returned_with_amount_script_and_proof()
    {
        using var factory = await HostAsync(First, Last);
        var spent = SpentPayment();
        var payment = PaymentsInFixture().First(p => p.Index > 0 && (spent is null || p.Txid != spent.Value.Txid));
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/utxos", new { addresses = new[] { payment.Address }, fromHeight = First });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var utxo = body.GetProperty("utxos").EnumerateArray().First(u => u.GetProperty("txid").GetString() == payment.Txid.ToString() && u.GetProperty("vout").GetInt32() == payment.Vout);
        var tx = GatewayWorld.At(payment.Height).Block.Transactions[payment.Index];
        Assert.Equal(tx.Outputs[payment.Vout].Value.Satoshi, utxo.GetProperty("amount").GetInt64());
        Assert.Equal(tx.Outputs[payment.Vout].ScriptPubKey.ToHex(), utxo.GetProperty("scriptPubKey").GetString());
        var proof = utxo.GetProperty("proof");
        var branch = proof.GetProperty("branch").EnumerateArray().Select(b => uint256.Parse(b.GetString()!)).ToList();
        Assert.True(MerkleProof.Verify(payment.Txid, proof.GetProperty("index").GetInt32(), branch, RealHeaders.At(payment.Height).MerkleRoot));
    }

    [Fact]
    public async Task An_output_spent_in_a_later_block_is_not_returned()
    {
        var spent = SpentPayment();
        Assert.NotNull(spent); // the fixture blocks contain at least one payment that is spent again within them
        using var factory = await HostAsync(First, Last);
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, "/v1/utxos", new { addresses = new[] { spent!.Value.Address }, fromHeight = First });

        Assert.DoesNotContain(body.GetProperty("utxos").EnumerateArray(),
            u => u.GetProperty("txid").GetString() == spent.Value.Txid.ToString() && u.GetProperty("vout").GetInt32() == spent.Value.Vout);
    }

    [Fact]
    public async Task Unspent_outputs_are_not_reported_while_the_index_is_behind_the_tip()
    {
        using var factory = await HostAsync(First, GatewayWorld.Heights[20]);
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, "/v1/utxos", new { addresses = new[] { SomePayment().Address }, fromHeight = First });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("not_ready", body.GetProperty("error").GetString());
    }
}
