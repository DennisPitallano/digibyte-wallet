using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Endpoints;

public class HealthAndHeaderEndpointTests
{
    private const int Cp = 24563100;

    [Fact]
    public async Task Health_reports_the_contract_fields_and_no_wallet_data()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("starting", root.GetProperty("status").GetString());
        Assert.Equal("mainnet", root.GetProperty("network").GetString());
        Assert.Equal(Cp, root.GetProperty("headerHeight").GetInt32());
        Assert.Equal(0, root.GetProperty("peerCount").GetInt32());
        Assert.Equal(0, root.GetProperty("fullHistoryPeerCount").GetInt32());
        Assert.Equal(0, root.GetProperty("filterPeerCount").GetInt32());
        Assert.Equal("blocks", root.GetProperty("matchingMode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("buildVersion").GetString()));
        Assert.True(root.TryGetProperty("lagBlocks", out _));
        Assert.True(root.TryGetProperty("indexedFromHeight", out _));
        // Nothing about wallets, addresses or clients.
        Assert.DoesNotContain("wallet", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mainnet", response.Headers.GetValues("X-Network").Single());
    }

    [Fact]
    public async Task Headers_come_back_as_consecutive_80_byte_records_with_the_tip_in_headers()
    {
        using var factory = new GatewayFactory();
        factory.Sync.Ingest(Enumerable.Range(Cp + 1, 20).Select(RealHeaders.At));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/headers?from={Cp + 1}&count=5");
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(5 * 80, body.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal(RealHeaders.At(Cp + 1 + i).Hash, BlockHeader80.Parse(body.AsSpan(i * 80, 80)).Hash);
        Assert.Equal((Cp + 20).ToString(), response.Headers.GetValues("X-Tip-Height").Single());
        Assert.Equal(RealHeaders.At(Cp + 20).Hash.ToString(), response.Headers.GetValues("X-Tip-Hash").Single());
    }

    [Fact]
    public async Task Asking_past_the_tip_gives_an_empty_body_not_an_error()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/headers?from={Cp + 500}&count=10");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/v1/headers?count=5", "bad_request", 400)]
    [InlineData("/v1/headers?from=abc&count=5", "bad_request", 400)]
    [InlineData("/v1/headers?from=-4&count=5", "bad_request", 400)]
    [InlineData("/v1/headers?from=24563100", "bad_request", 400)]
    [InlineData("/v1/headers?from=24563100&count=0", "bad_request", 400)]
    [InlineData("/v1/headers?from=24563100&count=2001", "over_limit", 400)]
    [InlineData("/v1/headers?from=100&count=5", "range_unavailable", 404)]
    public async Task Bad_header_requests_get_the_contract_error_shape(string url, string code, int status)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(url);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, doc.RootElement.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task The_header_count_limit_follows_the_setting()
    {
        using var factory = new GatewayFactory(new() { ["SpvGateway:MaxHeadersPerRequest"] = "10" });
        using var client = factory.CreateClient();

        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync($"/v1/headers?from={Cp}&count=10")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.GetAsync($"/v1/headers?from={Cp}&count=11")).StatusCode);
    }

    [Fact]
    public async Task Headers_below_the_earliest_stored_one_are_unavailable()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        var earliest = Checkpoints.Mainnet.FirstHeight;

        var ok = await client.GetAsync($"/v1/headers?from={earliest}&count=3");
        var gone = await client.GetAsync($"/v1/headers?from={earliest - 1}&count=3");

        Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, gone.StatusCode);
    }
}
