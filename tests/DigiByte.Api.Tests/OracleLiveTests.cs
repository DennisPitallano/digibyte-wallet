using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigiByte.Api.Tests;

/// <summary>
/// Opt-in check against the real digibyte.io (specs/014-digibyte-io-oracle SC-002). It runs only with the environment variable
/// <c>ORACLE_LIVE=1</c>, so the normal test run never touches the network.
/// </summary>
public class OracleLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("ORACLE_LIVE") == "1";

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production");
    }

    [Fact]
    public async Task The_route_gives_a_valid_reading_whose_price_matches_the_real_site()
    {
        if (!Enabled) return; // opt in with ORACLE_LIVE=1

        using var factory = new Factory();
        using var direct = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var before = await DirectPrice(direct);
        var response = await factory.CreateClient().GetAsync("/api/oracle");
        var after = await DirectPrice(direct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var price = body.GetProperty("priceMicroUsd").GetInt64();
        Assert.True(price > 0);
        Assert.True(body.GetProperty("lastUpdateHeight").GetInt64() > 0);
        Assert.Equal("digibyte.io", body.GetProperty("source").GetString());
        // The price may have changed once between the reads; it must equal one of the two direct reads.
        Assert.Contains(price, new[] { before, after });
    }

    private static async Task<long> DirectPrice(HttpClient http)
    {
        var text = await http.GetStringAsync("https://digibyte.io/api/getoracleprice");
        return JsonDocument.Parse(text).RootElement.GetProperty("price_micro_usd").GetInt64();
    }
}
