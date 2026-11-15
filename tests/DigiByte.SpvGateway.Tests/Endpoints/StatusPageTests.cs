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
    public async Task The_status_json_reports_only_index_heights_and_the_request_summary_and_is_empty_before_indexing()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync("/status.json");
        using var doc = System.Text.Json.JsonDocument.Parse(text);

        Assert.Equal(["indexedFromHeight", "indexedToHeight", "requests"], doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
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

/// <summary>The status page lists what wallets asked for, and nothing that identifies a wallet.</summary>
public class RequestLogTests
{
    private static async Task<System.Text.Json.JsonElement> RequestsAsync(HttpClient client)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/status.json"));
        return doc.RootElement.GetProperty("requests").Clone();
    }

    [Fact]
    public async Task A_request_shows_up_as_its_route_status_and_time()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        Assert.Equal(0, (await RequestsAsync(client)).GetProperty("total").GetInt64());
        await client.GetAsync("/v1/fee");

        var requests = await RequestsAsync(client);
        var entry = requests.GetProperty("recent")[0];
        Assert.Equal(1, requests.GetProperty("total").GetInt64());
        Assert.Equal("GET", entry.GetProperty("method").GetString());
        Assert.Equal("/v1/fee", entry.GetProperty("route").GetString());
        Assert.Equal(200, entry.GetProperty("status").GetInt32());
        Assert.True(entry.GetProperty("ms").GetInt32() >= 0);
        Assert.Equal(1, requests.GetProperty("byRoute").GetProperty("GET /v1/fee").GetInt64());
    }

    [Fact]
    public async Task The_route_pattern_is_listed_never_the_value_in_it_and_never_the_query()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        var txid = new string('a', 64);

        await client.GetAsync($"/v1/tx/{txid}?secret=DSomeAddress123");

        var text = (await RequestsAsync(client)).GetRawText();
        Assert.Contains("/v1/tx/{txid}", text);
        Assert.DoesNotContain(txid, text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("DSomeAddress123", text);
    }

    [Fact]
    public async Task The_status_page_itself_its_files_and_preflight_checks_are_not_listed()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        await client.GetAsync("/");
        await client.GetAsync("/fonts/inter-latin-wght-normal.woff2");
        using (var polling = new HttpRequestMessage(HttpMethod.Get, "/v1/health")) { polling.Headers.Add("X-Status-Page", "1"); await client.SendAsync(polling); }
        using (var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/fee")) { preflight.Headers.Add("Origin", "https://wallet.example"); preflight.Headers.Add("Access-Control-Request-Method", "GET"); await client.SendAsync(preflight); }
        await client.GetAsync("/status.json");

        Assert.Equal(0, (await RequestsAsync(client)).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task A_test_request_sent_from_the_page_is_listed_and_marked_as_a_test()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        using (var test = new HttpRequestMessage(HttpMethod.Get, "/v1/fee")) { test.Headers.Add("X-Test-Request", "1"); await client.SendAsync(test); }
        await client.GetAsync("/v1/fee");

        var recent = (await RequestsAsync(client)).GetProperty("recent");
        Assert.False(recent[0].GetProperty("test").GetBoolean()); // newest first: the ordinary one
        Assert.True(recent[1].GetProperty("test").GetBoolean());
    }

    [Fact]
    public async Task The_page_shows_how_many_peers_advertise_bloom_filters_and_says_they_are_not_used()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("bloomPeerCount", html);
        Assert.Contains("Bloom filter peers", html);
        Assert.Contains("Not used", html);
    }

    [Fact]
    public async Task The_test_request_shows_the_response_it_got()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("JSON.stringify(JSON.parse(body)", html); // the answer is shown pretty-printed
        Assert.Contains("testWhat", html);                       // and the operator picks what to ask
    }

    [Fact]
    public async Task The_page_offers_a_test_request_that_asks_the_gateway_the_way_a_wallet_does()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Send test request", html);
        Assert.Contains("X-Test-Request", html);
        Assert.Contains("/v1/health", html);
        Assert.Contains("/v1/fee", html);
    }

    [Fact]
    public async Task A_monitors_health_request_is_listed_so_the_operator_can_see_wallets_reaching_the_gateway()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        await client.GetAsync("/v1/health");

        Assert.Equal("/v1/health", (await RequestsAsync(client)).GetProperty("recent")[0].GetProperty("route").GetString());
    }

    [Fact]
    public async Task Failed_requests_count_as_errors()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        await client.GetAsync($"/v1/tx/not-a-txid");

        var requests = await RequestsAsync(client);
        Assert.Equal(1, requests.GetProperty("errors").GetInt64());
    }

    [Fact]
    public async Task The_operator_can_switch_the_list_off()
    {
        using var factory = new GatewayFactory(new Dictionary<string, string?> { ["SpvGateway:ShowRequestLog"] = "false" });
        using var client = factory.CreateClient();

        await client.GetAsync("/v1/fee");

        using var doc = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/status.json"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("requests").ValueKind);
    }

    private const string Token = "test-admin-token-0123456789-abcdefghijklmnop";

    private static GatewayFactory WithToken() => new(new Dictionary<string, string?> { ["SpvGateway:AdminToken"] = Token });

    private static HttpRequestMessage Reset(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/status/reset");
        if (token is not null)
            request.Headers.Add("X-Admin-Token", token);
        return request;
    }

    [Fact]
    public async Task Without_an_admin_token_there_is_no_reset_and_the_page_offers_none()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        await client.GetAsync("/v1/fee");

        using var attempt = Reset("anything");
        var response = await client.SendAsync(attempt);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        var requests = await RequestsAsync(client);
        Assert.False(requests.GetProperty("canReset").GetBoolean());
        Assert.Equal(1, requests.GetProperty("total").GetInt64()); // nothing was cleared
    }

    [Fact]
    public async Task A_wrong_or_missing_token_is_refused_and_clears_nothing()
    {
        using var factory = WithToken();
        using var client = factory.CreateClient();
        await client.GetAsync("/v1/fee");

        foreach (var given in new string?[] { null, "", "wrong", Token + "x", Token[..^1] })
        {
            using var attempt = Reset(given);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.SendAsync(attempt)).StatusCode);
        }

        Assert.Equal(1, (await RequestsAsync(client)).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task The_right_token_clears_the_list_the_totals_and_the_route_counts()
    {
        using var factory = WithToken();
        using var client = factory.CreateClient();
        await client.GetAsync("/v1/fee");
        await client.GetAsync("/v1/tx/not-a-txid");
        var before = await RequestsAsync(client);
        Assert.True(before.GetProperty("canReset").GetBoolean());
        Assert.Equal(2, before.GetProperty("total").GetInt64());

        using var attempt = Reset(Token);
        var response = await client.SendAsync(attempt);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var after = await RequestsAsync(client);
        Assert.Equal(0, after.GetProperty("total").GetInt64());
        Assert.Equal(0, after.GetProperty("errors").GetInt64());
        Assert.Equal(0, after.GetProperty("recent").GetArrayLength());
        Assert.Empty(after.GetProperty("byRoute").EnumerateObject());
    }

    [Fact]
    public async Task The_reset_cannot_be_triggered_from_another_site()
    {
        using var factory = WithToken();
        using var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/status/reset");
        preflight.Headers.Add("Origin", "https://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-admin-token");
        var response = await client.SendAsync(preflight);

        // The cross-origin policy does not list the admin header, so a page on another site is not allowed to send it.
        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Headers", out var allowed) && string.Join(",", allowed).Contains("x-admin-token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_page_has_a_reset_control_that_shows_only_when_the_gateway_allows_it()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("canReset", html);
        Assert.Contains("X-Admin-Token", html);
        Assert.Contains("resetBar", html);
    }

    [Fact]
    public void Clearing_the_log_starts_the_totals_again()
    {
        var log = new DigiByte.SpvGateway.Endpoints.RequestLog();
        log.Record(new DigiByte.SpvGateway.Endpoints.RequestEntry(DateTimeOffset.UtcNow, "GET", "/v1/fee", 500, 1));

        log.Clear();
        var snapshot = log.Snapshot();

        Assert.Equal(0, snapshot.Total);
        Assert.Equal(0, snapshot.Errors);
        Assert.Empty(snapshot.Recent);
        Assert.Empty(snapshot.ByRoute);
    }

    [Fact]
    public void Only_the_most_recent_requests_are_kept_newest_first()
    {
        var log = new DigiByte.SpvGateway.Endpoints.RequestLog(capacity: 3);
        for (var i = 1; i <= 5; i++)
            log.Record(new DigiByte.SpvGateway.Endpoints.RequestEntry(DateTimeOffset.UtcNow, "GET", "/v1/fee", 200, i));

        var snapshot = log.Snapshot();

        Assert.Equal(5, snapshot.Total);
        Assert.Equal([5, 4, 3], snapshot.Recent.Select(r => r.Ms).ToList());
    }
}
