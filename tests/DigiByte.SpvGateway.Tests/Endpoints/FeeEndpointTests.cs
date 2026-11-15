using System.Net;
using System.Text.Json;
using DigiByte.SpvGateway.Endpoints;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>GET /v1/fee is advisory and honest about what it is based on (T085, spec FR-010).</summary>
public class FeeEndpointTests
{
    [Fact]
    public async Task The_fee_is_the_network_minimum_and_says_so()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/fee");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100m, body.GetProperty("satoshisPerVbyte").GetDecimal());
        Assert.Equal("network-minimum", body.GetProperty("basis").GetString());
        Assert.Equal(FeeEndpoints.MinimumRelaySatoshisPerVbyte, body.GetProperty("satoshisPerVbyte").GetDecimal());
    }

    [Fact]
    public async Task The_fee_answer_carries_nothing_about_any_client()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync("/v1/fee");

        Assert.Equal(["basis", "satoshisPerVbyte"], JsonDocument.Parse(text).RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
    }
}
