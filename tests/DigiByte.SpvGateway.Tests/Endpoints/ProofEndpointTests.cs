using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>POST /v1/proofs (T056) and GET /v1/tx/{txid} (T065), against real blocks.</summary>
public class ProofEndpointTests
{
    private static readonly int First = GatewayWorld.Heights.First();
    private static readonly int Last = GatewayWorld.Heights.Last();

    private sealed class CapturingLogger : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
                Lines.Add(formatter(state, exception) + " " + exception);
        }
        public void Dispose() { }
    }

    private static (int Height, int Index, Transaction Tx) SomeTransaction()
    {
        var (height, block) = GatewayWorld.Heights.Select(GatewayWorld.At).First(b => b.Block.Transactions.Count > 2);
        return (height, 1, block.Transactions[1]);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/v1/proofs", body);
        return (response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    /// <summary>A host with headers and peers but an EMPTY index: proofs must not need it.</summary>
    private static async Task<GatewayFactory> HostWithoutIndexAsync(CapturingLogger? logger = null)
    {
        var world = await GatewayWorld.CreateAsync();
        var factory = new GatewayFactory(null, services =>
        {
            world.Register(services);
            if (logger is not null)
                services.AddLogging(b => b.AddProvider(logger));
        });
        factory.Sync.Ingest(GatewayWorld.Heights.Select(RealHeaders.At));
        return factory;
    }

    [Fact]
    public async Task A_transaction_is_proven_from_its_block_alone_even_when_the_index_does_not_cover_it()
    {
        using var factory = await HostWithoutIndexAsync();
        var (height, index, tx) = SomeTransaction();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, new { items = new[] { new { txid = tx.GetHash().ToString(), height } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proof = body.GetProperty("proofs").EnumerateArray().Single();
        Assert.Equal(height, proof.GetProperty("height").GetInt32());
        Assert.Equal(RealHeaders.At(height).Hash.ToString(), proof.GetProperty("blockHash").GetString());
        Assert.Equal(tx.ToHex(), proof.GetProperty("raw").GetString());
        var branch = proof.GetProperty("proof").GetProperty("branch").EnumerateArray().Select(b => uint256.Parse(b.GetString()!)).ToList();
        Assert.Equal(index, proof.GetProperty("proof").GetProperty("index").GetInt32());
        Assert.True(MerkleProof.Verify(tx.GetHash(), index, branch, RealHeaders.At(height).MerkleRoot));
        Assert.Empty(body.GetProperty("unavailable").EnumerateArray());
    }

    [Fact]
    public async Task A_wrong_height_or_an_unknown_transaction_is_reported_as_unavailable_with_a_reason()
    {
        using var factory = await HostWithoutIndexAsync();
        var (height, _, tx) = SomeTransaction();
        var otherHeight = GatewayWorld.Heights.First(h => h != height);
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, new
        {
            items = new object[]
            {
                new { txid = tx.GetHash().ToString(), height = otherHeight },
                new { txid = uint256.One.ToString(), height },
                new { txid = tx.GetHash().ToString(), height = Last + 5000 },
            },
        });

        Assert.Empty(body.GetProperty("proofs").EnumerateArray());
        var reasons = body.GetProperty("unavailable").EnumerateArray().Select(u => u.GetProperty("reason").GetString()).ToList();
        Assert.Equal(["not_in_block", "not_in_block", "header_unknown"], reasons);
    }

    [Fact]
    public async Task When_no_peer_can_serve_the_block_the_item_is_unavailable_not_an_error()
    {
        using var factory = await HostWithoutIndexAsync();
        foreach (var peer in factory.Services.GetRequiredService<DigiByte.SpvGateway.Peers.PeerManager>().Sessions.Cast<FakePeer>())
            peer.Blocks.Clear();
        var (height, _, tx) = SomeTransaction();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, new { items = new[] { new { txid = tx.GetHash().ToString(), height } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("block_not_served", body.GetProperty("unavailable")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task More_than_twenty_items_is_over_limit()
    {
        using var factory = await HostWithoutIndexAsync();
        var items = Enumerable.Range(0, 21).Select(_ => new { txid = uint256.One.ToString(), height = First }).ToArray();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, new { items });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("over_limit", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("nonsense", 5)]
    [InlineData("", 5)]
    public async Task A_malformed_item_is_a_bad_request(string txid, int height)
    {
        using var factory = await HostWithoutIndexAsync();
        using var client = factory.CreateClient();

        var (_, body) = await PostAsync(client, new { items = new[] { new { txid, height } } });

        Assert.Equal("bad_request", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Nothing_about_the_request_appears_in_the_logs()
    {
        var logger = new CapturingLogger();
        using var factory = await HostWithoutIndexAsync(logger);
        var (height, _, tx) = SomeTransaction();
        using var client = factory.CreateClient();

        await PostAsync(client, new { items = new[] { new { txid = tx.GetHash().ToString(), height } } });
        await PostAsync(client, new { items = new[] { new { txid = uint256.One.ToString(), height } } });
        await client.GetAsync($"/v1/tx/{tx.GetHash()}");

        string joined;
        lock (logger.Lines)
            joined = string.Join("\n", logger.Lines);
        var leaked = logger.Lines.Where(l => l.Contains(tx.GetHash().ToString(), StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
        Assert.True(leaked.Count == 0, string.Join(" || ", leaked));
        Assert.DoesNotContain(uint256.One.ToString(), joined, StringComparison.OrdinalIgnoreCase);
    }

    // ---- GET /v1/tx/{txid} ----

    [Fact]
    public async Task A_transaction_in_the_indexed_blocks_is_found_by_id_with_its_proof()
    {
        var world = await GatewayWorld.CreateAsync();
        using var factory = await GatewayWorld.HostAsync(world, First, Last);
        var (height, index, tx) = SomeTransaction();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/tx/{tx.GetHash()}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tx.ToHex(), body.GetProperty("raw").GetString());
        Assert.Equal(height, body.GetProperty("height").GetInt32());
        var branch = body.GetProperty("proof").GetProperty("branch").EnumerateArray().Select(b => uint256.Parse(b.GetString()!)).ToList();
        Assert.True(MerkleProof.Verify(tx.GetHash(), index, branch, RealHeaders.At(height).MerkleRoot));
    }

    [Fact]
    public async Task An_unknown_transaction_is_not_found()
    {
        var world = await GatewayWorld.CreateAsync();
        using var factory = await GatewayWorld.HostAsync(world, First, Last);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/tx/{uint256.One}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_malformed_transaction_id_is_a_bad_request()
    {
        using var factory = await HostWithoutIndexAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/tx/nonsense");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
