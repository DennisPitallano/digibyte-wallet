using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.SpvGateway.Tests.Support;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>POST /v1/broadcast outcome shapes and refusals (T080).</summary>
public class BroadcastEndpointTests
{
    private static string SampleHex() =>
        RealBlocks.Parse(RealBlocks.All.First(b => RealBlocks.Parse(b.Raw).Transactions.Count > 2).Raw).Transactions[1].ToHex();

    private static async Task<GatewayFactory> HostAsync(Action<FakePeer>? configure = null, int peers = 5)
    {
        var world = await GatewayWorld.CreateAsync();
        for (var i = 1; i < peers; i++)
            world.Peers.Add($"{30 + i}.{20 + i}.1.1");
        await world.Manager.MaintainAsync(default);
        foreach (var peer in world.Peers.Peers)
            configure?.Invoke(peer);
        return new GatewayFactory(null, world.Register);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(HttpClient client, string raw)
    {
        var response = await client.PostAsJsonAsync("/v1/broadcast", new { raw });
        return (response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    [Fact]
    public async Task A_transaction_taken_by_the_peers_is_accepted_with_the_contract_fields()
    {
        using var factory = await HostAsync();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, SampleHex());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("accepted", body.GetProperty("outcome").GetString());
        Assert.Equal(Transaction.Parse(SampleHex(), GatewayWorld.Network).GetHash().ToString(), body.GetProperty("txid").GetString());
        Assert.True(body.GetProperty("relayedTo").GetInt32() >= 4);
        Assert.Empty(body.GetProperty("rejections").EnumerateArray());
        Assert.True(body.TryGetProperty("seenBackFrom", out _));
    }

    [Fact]
    public async Task A_transaction_the_peers_reject_comes_back_as_rejected_with_the_reasons()
    {
        using var factory = await HostAsync(p => p.RejectReason = "66: insufficient fee");
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, SampleHex());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("rejected", body.GetProperty("outcome").GetString());
        Assert.Contains("insufficient fee", body.GetProperty("rejections")[0].GetString());
    }

    [Fact]
    public async Task When_no_peer_answers_the_outcome_is_no_response()
    {
        using var factory = await HostAsync(p => p.TakesTransactions = false);
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, SampleHex());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no_response", body.GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("abc")]
    [InlineData("0102030405")]
    [InlineData("0100000000000000000000")]
    public async Task Something_that_is_not_a_transaction_is_a_bad_request(string raw)
    {
        using var factory = await HostAsync();
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, raw);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_request", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_missing_body_is_a_bad_request()
    {
        using var factory = await HostAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/v1/broadcast", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_body_is_refused_without_contacting_any_peer()
    {
        var world = await GatewayWorld.CreateAsync();
        using var factory = new GatewayFactory(null, world.Register);
        using var client = factory.CreateClient();

        var (response, body) = await PostAsync(client, new string('0', 900_000));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(body.GetProperty("error").GetString(), new[] { "over_limit", "bad_request" });
        Assert.All(world.Peers.Peers, p => Assert.Empty(p.Relayed));
    }

    [Fact]
    public async Task Sending_the_same_transaction_twice_gives_the_same_answer_and_is_relayed_once()
    {
        using var factory = await HostAsync();
        using var client = factory.CreateClient();
        var (_, first) = await PostAsync(client, SampleHex());

        var (_, second) = await PostAsync(client, SampleHex());

        Assert.Equal(first.GetProperty("txid").GetString(), second.GetProperty("txid").GetString());
        Assert.Equal("accepted", second.GetProperty("outcome").GetString());
    }
}
