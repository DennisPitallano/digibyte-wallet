using DigiByte.Wallet.Services;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T034: node use switched off never contacts anything; an invalid or unreachable node address is just "unavailable" with no
/// repeated events; the check interval from the options is respected and checks never overlap.
/// </summary>
public class NodeSourceMonitorLoopTests
{
    private static NodeSourceMonitor Monitor(NodeSourceOptions options, FakeHealthSource health, FakeExplorerHeightSource explorer) =>
        new(options, health, explorer, new SystemClock());

    private static NodeSourceOptions Options(bool enabled, TimeSpan interval, string? url = "https://node.example.org") => new()
    {
        Enabled = enabled,
        ApiUrl = url,
        CheckInterval = interval,
        HealthTimeout = TimeSpan.FromMilliseconds(300),
        ExplorerBudget = TimeSpan.FromMilliseconds(300),
    };

    [Fact]
    public async Task Disabled_TheLoopNeverStartsAndNothingIsContacted()
    {
        var health = new FakeHealthSource();
        var explorer = new FakeExplorerHeightSource();
        var options = Options(enabled: false, TimeSpan.FromMilliseconds(50));
        await using var loop = new NodeSourceMonitorLoop(Monitor(options, health, explorer), options);

        loop.Start();
        await Task.Delay(300);

        Assert.Equal(0, health.Calls);
        Assert.Equal(0, explorer.Calls);
    }

    [Fact]
    public async Task Enabled_ChecksOnceAtOnce_ThenFollowsTheInterval()
    {
        var health = new FakeHealthSource();
        var explorer = new FakeExplorerHeightSource();
        var options = Options(enabled: true, TimeSpan.FromMilliseconds(120));
        await using var loop = new NodeSourceMonitorLoop(Monitor(options, health, explorer), options);

        loop.Start();
        await Task.Delay(60);
        Assert.Equal(1, health.Calls);          // the first check does not wait for an interval

        await Task.Delay(500);
        Assert.InRange(health.Calls, 3, 7);     // roughly one per 120 ms
    }

    [Fact]
    public async Task StartingTwice_DoesNotRunTwoLoops()
    {
        var health = new FakeHealthSource();
        var options = Options(enabled: true, TimeSpan.FromMilliseconds(100));
        await using var loop = new NodeSourceMonitorLoop(Monitor(options, health, new FakeExplorerHeightSource()), options);

        loop.Start();
        loop.Start();
        await Task.Delay(350);

        Assert.InRange(health.Calls, 2, 5);
    }

    [Fact]
    public async Task ASlowCheck_DelaysTheNextOne_ChecksNeverOverlap()
    {
        var running = 0;
        var maxRunning = 0;
        var health = new FakeHealthSource
        {
            Respond = async _ =>
            {
                var now = Interlocked.Increment(ref running);
                lock (typeof(NodeSourceMonitorLoopTests)) maxRunning = Math.Max(maxRunning, now);
                await Task.Delay(200);
                Interlocked.Decrement(ref running);
                return FakeHealthSource.Ok();
            },
        };
        var options = Options(enabled: true, TimeSpan.FromMilliseconds(50));
        await using var loop = new NodeSourceMonitorLoop(Monitor(options, health, new FakeExplorerHeightSource()), options);

        loop.Start();
        await Task.Delay(700);

        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Disposing_StopsTheChecks()
    {
        var health = new FakeHealthSource();
        var options = Options(enabled: true, TimeSpan.FromMilliseconds(50));
        var loop = new NodeSourceMonitorLoop(Monitor(options, health, new FakeExplorerHeightSource()), options);
        loop.Start();
        await Task.Delay(200);

        await loop.DisposeAsync();
        var callsAtStop = health.Calls;
        await Task.Delay(300);

        Assert.Equal(callsAtStop, health.Calls);
    }

    // ---------- an invalid or unreachable node address ----------

    [Fact]
    public async Task InvalidAddress_IsUnavailable_WithoutThrowing_AndWithoutRequests()
    {
        var options = NodeSourceOptions.FromConfiguration(key => key switch
        {
            "OwnNode:Enabled" => "true",
            "OwnNode:ApiUrl" => "not a url",
            _ => null,
        });
        Assert.False(options.HasApiUrl);
        var handler = new CountingHandler();
        var monitor = new NodeSourceMonitor(
            options, new NodeHealthHttpSource(new HttpClient(handler), options), new FakeExplorerHeightSource(), new SystemClock());

        await monitor.CheckAsync();

        Assert.Equal(NodeSourceState.Unavailable, monitor.State);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task UnreachableAddress_ReportsUnavailable_AndDoesNotRepeatChangeEvents()
    {
        var options = Options(enabled: true, TimeSpan.FromSeconds(30));
        var health = new FakeHealthSource();
        health.Returns(NodeHealthResult.Unreachable);
        var monitor = Monitor(options, health, new FakeExplorerHeightSource());
        var events = 0;
        monitor.Changed += () => events++;

        for (var i = 0; i < 10; i++)
            await monitor.CheckAsync();

        Assert.Equal(NodeSourceState.Unavailable, monitor.State);
        Assert.Equal(NodeNotReadyReason.Unreachable, monitor.Reason);
        Assert.Equal(1, events);   // one change from "not yet checked" to "unreachable", then nothing
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            throw new HttpRequestException("no network in tests");
        }
    }
}
