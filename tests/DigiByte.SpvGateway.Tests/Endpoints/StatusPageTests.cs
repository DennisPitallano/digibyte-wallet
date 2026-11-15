using System.Net;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>The operator status page shows sync progress from the health report and nothing else.</summary>
public class StatusPageTests
{
    [Fact]
    public async Task The_root_serves_a_page_that_polls_the_health_report()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("/v1/health", html);
        Assert.Contains("lagBlocks", html);
    }

    [Fact]
    public async Task The_page_loads_nothing_from_another_site_and_names_no_address_or_client()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("address", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", html, StringComparison.OrdinalIgnoreCase);
        // Every file it asks for is one of the gateway's own: fonts and the mascot.
        var urls = System.Text.RegularExpressions.Regex.Matches(html, @"(?:src=""|url\(')(/[^""')]+)").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(urls);
        Assert.All(urls, u => Assert.Matches("^/(fonts|baku)/", u));
    }
}

public class StatusJsonTests
{
    [Fact]
    public async Task The_status_json_reports_only_index_heights_and_is_empty_before_indexing()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync("/status.json");
        using var doc = System.Text.Json.JsonDocument.Parse(text);

        Assert.Equal(["indexedFromHeight", "indexedToHeight"], doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("indexedFromHeight").ValueKind);
    }

    [Fact]
    public async Task Once_blocks_are_indexed_the_heights_show_the_range()
    {
        var world = await GatewayWorld.CreateAsync();
        using var factory = await GatewayWorld.HostAsync(world, GatewayWorld.Heights[2], GatewayWorld.Heights[9]);
        using var client = factory.CreateClient();

        using var doc = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/status.json"));

        Assert.Equal(GatewayWorld.Heights[2], doc.RootElement.GetProperty("indexedFromHeight").GetInt32());
        Assert.Equal(GatewayWorld.Heights[9], doc.RootElement.GetProperty("indexedToHeight").GetInt32());
    }
}

public class StatusPageAssetTests
{
    [Theory]
    [InlineData("/fonts/jetbrains-mono-latin-wght-normal.woff2", "font/woff2")]
    [InlineData("/fonts/inter-latin-wght-normal.woff2", "font/woff2")]
    [InlineData("/baku/thinking.webp", "image/webp")]
    [InlineData("/baku/celebrating.webp", "image/webp")]
    public async Task The_fonts_and_mascot_the_page_uses_are_served_by_the_gateway_itself(string path, string type)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(type, response.Content.Headers.ContentType!.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 5000);
    }

    [Fact]
    public async Task Every_pose_the_page_can_show_exists()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        var poses = new[] { "thinking", "idea", "celebrating", "greeting", "guard", "error" };

        foreach (var pose in poses)
        {
            Assert.Contains($"'{pose}'", html);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync($"/baku/{pose}.webp")).StatusCode);
        }
    }

    [Fact]
    public async Task Numbers_use_the_same_monospace_face_as_the_wallet()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("'JetBrains Mono'", html);
        Assert.Contains("tabular-nums", html);
    }
}
