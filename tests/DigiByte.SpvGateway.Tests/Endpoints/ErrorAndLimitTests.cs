using System.Text.Json;
using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Endpoints;
using DigiByte.SpvGateway.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Endpoints;

public class ErrorAndLimitTests
{
    // A tiny host that uses the real middleware with routes that raise each kind of error.
    private static async Task<WebApplication> StartAsync(int perMinute = 120, bool trustForwarded = false, GatewayNetwork network = GatewayNetwork.Mainnet)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var options = new SpvGatewayOptions { RateLimitPerMinute = perMinute, TrustForwardedFor = trustForwarded, Network = network };
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new ClientLimiter(perMinute));
        var app = builder.Build();
        app.UseMiddleware<GatewayMiddleware>();
        app.MapGet("/v1/health", () => "ok");
        app.MapGet("/ok", () => "ok");
        app.MapGet("/boom", () => { throw new InvalidOperationException("secret address dgb1qexample"); });
        app.MapGet("/err/{code}", (string code) => { throw new GatewayException(Enum.Parse<GatewayError>(code), "reason"); });
        await app.StartAsync();
        return app;
    }

    [Theory]
    [InlineData("BadRequest", "bad_request", 400)]
    [InlineData("WrongNetwork", "wrong_network", 400)]
    [InlineData("OverLimit", "over_limit", 400)]
    [InlineData("RateLimited", "rate_limited", 429)]
    [InlineData("RangeUnavailable", "range_unavailable", 404)]
    [InlineData("NotReady", "not_ready", 503)]
    [InlineData("Internal", "internal", 500)]
    public async Task Every_error_code_has_the_contract_status_and_json_shape(string name, string code, int status)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync($"/err/{name}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("reason", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task An_unexpected_failure_is_reported_as_internal_without_its_message()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Contains("\"internal\"", body);
        Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("dgb1q", body);
    }

    [Fact]
    public async Task Every_response_names_the_network()
    {
        await using var app = await StartAsync(network: GatewayNetwork.Testnet);
        using var client = app.GetTestClient();

        Assert.Equal("testnet", (await client.GetAsync("/ok")).Headers.GetValues("X-Network").Single());
        Assert.Equal("testnet", (await client.GetAsync("/boom")).Headers.GetValues("X-Network").Single());
    }

    [Fact]
    public async Task A_client_over_its_allowance_gets_rate_limited_with_a_retry_time()
    {
        await using var app = await StartAsync(perMinute: 3);
        using var client = app.GetTestClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(200, (int)(await client.GetAsync("/ok")).StatusCode);
        var limited = await client.GetAsync("/ok");
        using var doc = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());

        Assert.Equal(429, (int)limited.StatusCode);
        Assert.Equal("rate_limited", doc.RootElement.GetProperty("error").GetString());
        Assert.True(doc.RootElement.GetProperty("retryAfterSeconds").GetInt32() >= 1);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task The_health_report_is_never_rate_limited()
    {
        await using var app = await StartAsync(perMinute: 1);
        using var client = app.GetTestClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(200, (int)(await client.GetAsync("/v1/health")).StatusCode);
    }

    [Fact]
    public async Task Forwarded_addresses_are_ignored_unless_the_proxy_is_trusted()
    {
        await using var app = await StartAsync(perMinute: 1, trustForwarded: false);
        using var client = app.GetTestClient();

        Assert.Equal(200, (int)(await Get(client, "1.1.1.1")).StatusCode);
        // Same real client, different claimed address: still the same bucket, so limited.
        Assert.Equal(429, (int)(await Get(client, "2.2.2.2")).StatusCode);
    }

    [Fact]
    public async Task Trusted_forwarded_addresses_get_their_own_allowance()
    {
        await using var app = await StartAsync(perMinute: 1, trustForwarded: true);
        using var client = app.GetTestClient();

        Assert.Equal(200, (int)(await Get(client, "1.1.1.1")).StatusCode);
        Assert.Equal(200, (int)(await Get(client, "2.2.2.2")).StatusCode);
        Assert.Equal(429, (int)(await Get(client, "1.1.1.1")).StatusCode);
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    // ---- address checks -------------------------------------------------------------------------------------------------

    private static string NewAddress(Network network, ScriptPubKeyType type = ScriptPubKeyType.Segwit) =>
        new Key().PubKey.GetAddress(type, network).ToString();

    [Fact]
    public void Addresses_for_this_network_are_accepted()
    {
        var list = new[] { NewAddress(DigiByteNetwork.Mainnet), NewAddress(DigiByteNetwork.Mainnet, ScriptPubKeyType.Legacy) };

        Assert.Equal(2, AddressNetworkGuard.Require(GatewayNetwork.Mainnet, list, 100).Count);
    }

    [Fact]
    public void A_testnet_address_sent_to_a_mainnet_gateway_is_refused_as_wrong_network()
    {
        var e = Assert.Throws<GatewayException>(() =>
            AddressNetworkGuard.Require(GatewayNetwork.Mainnet, [NewAddress(DigiByteNetwork.Testnet)], 100));

        Assert.Equal(GatewayError.WrongNetwork, e.Error);
        Assert.Equal(400, GatewayErrors.Status(e.Error));
    }

    [Fact]
    public void A_mainnet_address_sent_to_a_testnet_gateway_is_refused_as_wrong_network()
    {
        var e = Assert.Throws<GatewayException>(() =>
            AddressNetworkGuard.Require(GatewayNetwork.Testnet, [NewAddress(DigiByteNetwork.Mainnet)], 100));

        Assert.Equal(GatewayError.WrongNetwork, e.Error);
    }

    [Fact]
    public void An_invalid_address_is_a_bad_request_and_the_message_never_repeats_it()
    {
        var e = Assert.Throws<GatewayException>(() => AddressNetworkGuard.Require(GatewayNetwork.Mainnet, ["not-an-address-xyz"], 100));

        Assert.Equal(GatewayError.BadRequest, e.Error);
        Assert.DoesNotContain("xyz", e.Message);
    }

    [Fact]
    public void Too_many_addresses_are_over_the_limit()
    {
        var many = Enumerable.Range(0, 101).Select(_ => NewAddress(DigiByteNetwork.Mainnet)).ToList();

        var e = Assert.Throws<GatewayException>(() => AddressNetworkGuard.Require(GatewayNetwork.Mainnet, many, 100));

        Assert.Equal(GatewayError.OverLimit, e.Error);
    }

    [Fact]
    public void An_empty_list_is_a_bad_request()
    {
        Assert.Equal(GatewayError.BadRequest, Assert.Throws<GatewayException>(() => AddressNetworkGuard.Require(GatewayNetwork.Mainnet, [], 100)).Error);
        Assert.Equal(GatewayError.BadRequest, Assert.Throws<GatewayException>(() => AddressNetworkGuard.Require(GatewayNetwork.Mainnet, null, 100)).Error);
    }
}

public class ClientLimiterTests
{
    [Fact]
    public void A_client_can_burst_up_to_its_allowance_then_must_wait()
    {
        var now = DateTimeOffset.UnixEpoch;
        var limiter = new ClientLimiter(60, () => now);

        for (var i = 0; i < 60; i++)
            Assert.True(limiter.TryAcquire("a", out _));
        Assert.False(limiter.TryAcquire("a", out var retry));
        Assert.Equal(1, retry);
    }

    [Fact]
    public void The_allowance_refills_over_time()
    {
        var now = DateTimeOffset.UnixEpoch;
        var limiter = new ClientLimiter(60, () => now);
        for (var i = 0; i < 60; i++)
            limiter.TryAcquire("a", out _);

        now = now.AddSeconds(5);

        Assert.True(limiter.TryAcquire("a", out _));
    }

    [Fact]
    public void Clients_do_not_share_an_allowance()
    {
        var now = DateTimeOffset.UnixEpoch;
        var limiter = new ClientLimiter(1, () => now);

        Assert.True(limiter.TryAcquire("a", out _));
        Assert.False(limiter.TryAcquire("a", out _));
        Assert.True(limiter.TryAcquire("b", out _));
    }

    [Fact]
    public void The_retry_time_grows_when_the_allowance_is_small()
    {
        var now = DateTimeOffset.UnixEpoch;
        var limiter = new ClientLimiter(2, () => now);
        limiter.TryAcquire("a", out _);
        limiter.TryAcquire("a", out _);

        Assert.False(limiter.TryAcquire("a", out var retry));
        Assert.True(retry >= 30);
    }

    [Fact]
    public void A_non_positive_allowance_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientLimiter(0));
    }
}
