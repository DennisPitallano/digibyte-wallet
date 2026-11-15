using System.Text.Json;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Health;

/// <summary>The health report states every field the wallet monitor needs and nothing about any wallet (T105).</summary>
public class HealthReportCompletenessTests
{
    private static readonly string[] Expected =
    [
        "status", "network", "headerHeight", "lagBlocks", "peerCount", "fullHistoryPeerCount", "filterPeerCount", "bloomPeerCount", "matchingMode", "indexedFromHeight", "buildVersion",
    ];

    [Fact]
    public async Task The_report_has_exactly_the_documented_fields()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var body = JsonDocument.Parse(await client.GetStringAsync("/v1/health")).RootElement;

        Assert.Equal(Expected.Order().ToList(), body.EnumerateObject().Select(p => p.Name).Order().ToList());
    }

    [Fact]
    public async Task The_report_is_complete_and_reports_the_matching_mode_and_first_indexed_height_once_blocks_are_indexed()
    {
        var world = await GatewayWorld.CreateAsync();
        using var factory = await GatewayWorld.HostAsync(world, GatewayWorld.Heights[3], GatewayWorld.Heights.Last());
        using var client = factory.CreateClient();

        var body = JsonDocument.Parse(await client.GetStringAsync("/v1/health")).RootElement;

        Assert.Equal("blocks", body.GetProperty("matchingMode").GetString());
        Assert.Equal(GatewayWorld.Heights[3], body.GetProperty("indexedFromHeight").GetInt32());
        Assert.True(body.GetProperty("peerCount").GetInt32() > 0);
        Assert.True(body.GetProperty("fullHistoryPeerCount").GetInt32() >= 1);
        Assert.Equal(0, body.GetProperty("filterPeerCount").GetInt32());
        Assert.Equal(0, body.GetProperty("lagBlocks").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("buildVersion").GetString()));
    }

    [Fact]
    public async Task The_report_contains_no_wallet_or_client_data_even_after_requests()
    {
        var world = await GatewayWorld.CreateAsync();
        using var factory = await GatewayWorld.HostAsync(world, GatewayWorld.Heights.First(), GatewayWorld.Heights.Last());
        using var client = factory.CreateClient();
        await client.PostAsync("/v1/scan", new StringContent("{\"addresses\":[\"DExample\"],\"fromHeight\":1,\"toHeight\":2}", System.Text.Encoding.UTF8, "application/json"));

        var text = await client.GetStringAsync("/v1/health");

        Assert.DoesNotContain("DExample", text);
        Assert.DoesNotContain("wallet", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", text, StringComparison.OrdinalIgnoreCase);
    }
}
