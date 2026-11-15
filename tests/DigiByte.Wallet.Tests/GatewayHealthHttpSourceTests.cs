using System.Net;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Reading the gateway's health report and judging it with the existing readiness monitor (T040 and T041).</summary>
public class GatewayHealthHttpSourceTests
{
    private const string OkBody =
        "{\"status\":\"ok\",\"network\":\"mainnet\",\"headerHeight\":24563100,\"lagBlocks\":1,\"peerCount\":8,\"fullHistoryPeerCount\":5,\"filterPeerCount\":0,\"matchingMode\":\"blocks\",\"indexedFromHeight\":null,\"buildVersion\":\"1.0.0\"}";

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return respond(request, cancellationToken);
        }
    }

    private static P2pGatewayOptions Options(string? url = "https://gateway.example.org", string? timeout = null) =>
        P2pGatewayOptions.FromConfiguration(k => k switch
        {
            "SpvGateway:ApiUrl" => url,
            "SpvGateway:HealthTimeoutSeconds" => timeout,
            _ => null,
        });

    private static GatewayHealthHttpSource Source(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, out Handler handler, P2pGatewayOptions? options = null)
    {
        handler = new Handler((request, _) => respond(request));
        return new GatewayHealthHttpSource(new HttpClient(handler), options ?? Options());
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode code, string body) =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });

    [Fact]
    public async Task A_good_report_becomes_a_node_style_result()
    {
        var source = Source(_ => Reply(HttpStatusCode.OK, OkBody), out var handler);

        var health = await source.GetAsync();

        Assert.Equal(["https://gateway.example.org/v1/health"], handler.Urls);
        Assert.True(health.Reachable);
        Assert.False(health.Malformed);
        Assert.Equal("ok", health.Status);
        Assert.Equal(24563100, health.Blocks);
        Assert.Equal(1, health.Lag);
        Assert.Equal(8, health.Peers);
        Assert.True(health.Txindex);
        Assert.True(health.VersionOk);
    }

    [Theory]
    [InlineData("starting")]
    [InlineData("syncing")]
    [InlineData("unhealthy")]
    public void The_status_words_are_the_nodes(string status)
    {
        var health = GatewayHealthHttpSource.Parse(200, $"{{\"status\":\"{status}\",\"headerHeight\":10}}");

        Assert.Equal(status, health.Status);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(404)]
    public void A_failure_status_means_unreachable(int status)
    {
        Assert.False(GatewayHealthHttpSource.Parse(status, OkBody).Reachable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    public void An_unreadable_body_means_malformed(string body)
    {
        var health = GatewayHealthHttpSource.Parse(200, body);

        Assert.True(health.Reachable);
        Assert.True(health.Malformed);
    }

    [Fact]
    public async Task A_connection_error_is_reported_as_unreachable_and_never_thrown()
    {
        var source = Source(_ => throw new HttpRequestException("refused"), out _);

        Assert.False((await source.GetAsync()).Reachable);
    }

    [Fact]
    public async Task A_slow_gateway_times_out_as_unreachable()
    {
        var handler = new Handler(async (request, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var source = new GatewayHealthHttpSource(new HttpClient(handler), Options(timeout: "1"));

        var start = DateTime.UtcNow;
        var health = await source.GetAsync();

        Assert.False(health.Reachable);
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task No_configured_url_means_unreachable_without_any_request()
    {
        var source = Source(_ => Reply(HttpStatusCode.OK, OkBody), out var handler, Options(url: null));

        Assert.False((await source.GetAsync()).Reachable);
        Assert.Empty(handler.Urls);
    }

    // ---- the existing monitor judging a gateway (T041) ----------------------------------------------------------------

    private static (NodeSourceMonitor Monitor, FakeHealthSource Health, FakeExplorerHeightSource Explorer, FakeClock Clock) NewMonitor()
    {
        var options = P2pGatewayOptions.FromConfiguration(k => k switch
        {
            "SpvGateway:Enabled" => "true",
            "SpvGateway:ApiUrl" => "https://gateway.example.org",
            "SpvGateway:CheckIntervalSeconds" => "30",
            "SpvGateway:StablePeriodSeconds" => "120",
            _ => null,
        }).ToMonitorOptions();
        var health = new FakeHealthSource();
        var explorer = new FakeExplorerHeightSource();
        var clock = new FakeClock();
        return (new NodeSourceMonitor(options, health, explorer, clock), health, explorer, clock);
    }

    [Fact]
    public async Task A_healthy_gateway_becomes_active_only_after_the_stable_period()
    {
        var (monitor, health, explorer, clock) = NewMonitor();
        health.Returns(GatewayHealthHttpSource.Parse(200, "{\"status\":\"ok\",\"headerHeight\":1000,\"lagBlocks\":0,\"peerCount\":8}"));
        explorer.Returns(1000);

        await monitor.CheckAsync();
        Assert.Equal(NodeSourceState.Stabilizing, monitor.State);

        clock.Advance(TimeSpan.FromSeconds(60));
        await monitor.CheckAsync();
        Assert.Equal(NodeSourceState.Stabilizing, monitor.State);

        clock.Advance(TimeSpan.FromSeconds(70));
        await monitor.CheckAsync();
        Assert.Equal(NodeSourceState.Active, monitor.State);
    }

    [Theory]
    [InlineData("starting", NodeNotReadyReason.Starting)]
    [InlineData("syncing", NodeNotReadyReason.Syncing)]
    [InlineData("unhealthy", NodeNotReadyReason.Unhealthy)]
    public async Task A_gateway_that_is_not_ready_is_not_used(string status, NodeNotReadyReason reason)
    {
        var (monitor, health, explorer, _) = NewMonitor();
        health.Returns(GatewayHealthHttpSource.Parse(200, $"{{\"status\":\"{status}\",\"headerHeight\":1000}}"));
        explorer.Returns(1000);

        await monitor.CheckAsync();

        Assert.False(monitor.IsActive);
        Assert.Equal(reason, monitor.Reason);
    }

    [Fact]
    public async Task A_gateway_far_from_the_explorers_height_is_not_used()
    {
        var (monitor, health, explorer, _) = NewMonitor();
        health.Returns(GatewayHealthHttpSource.Parse(200, "{\"status\":\"ok\",\"headerHeight\":900}"));
        explorer.Returns(1000);

        await monitor.CheckAsync();

        Assert.Equal(NodeNotReadyReason.HeightDrift, monitor.Reason);
    }

    [Fact]
    public async Task Three_failed_reads_while_active_demote_the_gateway_at_once()
    {
        var (monitor, health, explorer, clock) = NewMonitor();
        health.Returns(GatewayHealthHttpSource.Parse(200, "{\"status\":\"ok\",\"headerHeight\":1000}"));
        explorer.Returns(1000);
        await monitor.CheckAsync();
        clock.Advance(TimeSpan.FromSeconds(130));
        await monitor.CheckAsync();
        Assert.True(monitor.IsActive);

        monitor.ReportNodeReadFailure();
        monitor.ReportNodeReadFailure();
        Assert.True(monitor.IsActive);
        monitor.ReportNodeReadFailure();

        Assert.False(monitor.IsActive);
        Assert.Equal(NodeNotReadyReason.ReadFailures, monitor.Reason);
    }

    [Fact]
    public async Task A_missing_explorer_height_neither_promotes_nor_demotes()
    {
        var (monitor, health, explorer, clock) = NewMonitor();
        health.Returns(GatewayHealthHttpSource.Parse(200, "{\"status\":\"ok\",\"headerHeight\":1000}"));
        explorer.Returns(1000);
        await monitor.CheckAsync();
        clock.Advance(TimeSpan.FromSeconds(130));
        await monitor.CheckAsync();
        Assert.True(monitor.IsActive);

        explorer.Returns(null);
        await monitor.CheckAsync();

        Assert.True(monitor.IsActive);
    }
}
