using System.Net;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>The wallet runs in a browser on another origin, so the gateway must answer cross-origin requests, without credentials.</summary>
public class CorsTests
{
    private static HttpRequestMessage Preflight(string path, string method, string headers) =>
        new(HttpMethod.Options, path)
        {
            Headers =
            {
                { "Origin", "https://wallet.example.org" },
                { "Access-Control-Request-Method", method },
                { "Access-Control-Request-Headers", headers },
            },
        };

    [Theory]
    [InlineData("/v1/scan", "POST", "content-type")]
    [InlineData("/v1/broadcast", "POST", "content-type,ngrok-skip-browser-warning")]
    [InlineData("/v1/health", "GET", "ngrok-skip-browser-warning")]
    public async Task A_preflight_from_a_browser_wallet_is_allowed(string path, string method, string headers)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight(path, method, headers));

        Assert.True(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains(method, string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")));
    }

    [Fact]
    public async Task Credentials_are_never_allowed()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight("/v1/scan", "POST", "content-type"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Answers_expose_the_headers_the_wallet_reads()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health") { Headers = { { "Origin", "https://wallet.example.org" } } };

        var response = await client.SendAsync(request);

        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-Network", exposed);
        Assert.Contains("X-Tip-Height", exposed);
    }
}
