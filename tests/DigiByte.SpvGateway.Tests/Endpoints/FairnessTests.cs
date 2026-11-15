using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DigiByte.SpvGateway.Matching;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.SpvGateway.Tests.Endpoints;

/// <summary>A heavy restore cannot starve normal reads: scans queue for a bounded number of slots (T097, SC-009).</summary>
public class FairnessTests
{
    private static readonly int First = GatewayWorld.Heights.First();
    private static readonly int Last = GatewayWorld.Heights.Last();

    private static string SomeAddress()
    {
        foreach (var height in GatewayWorld.Heights)
        {
            var (_, block) = GatewayWorld.At(height);
            foreach (var tx in block.Transactions.Skip(1))
                foreach (var output in tx.Outputs)
                    if (GatewayWorld.AddressOf(output) is { } address)
                        return address;
        }
        throw new InvalidOperationException("no address in the fixture blocks");
    }

    private static async Task<GatewayFactory> SlowHostAsync(int slots, int queueWaitSeconds)
    {
        var world = await GatewayWorld.CreateAsync();
        foreach (var peer in world.Peers.Peers)
            peer.BlockDelay = TimeSpan.Zero;
        var factory = await GatewayWorld.HostAsync(world, First, Last, new Dictionary<string, string?>
        {
            ["SpvGateway:MaxConcurrentScans"] = slots.ToString(),
            ["SpvGateway:ScanQueueWaitSeconds"] = queueWaitSeconds.ToString(),
        });
        // Only now make block fetches slow, so indexing the fixture was fast but every scan has to wait for its blocks.
        foreach (var peer in world.Peers.Peers)
            peer.BlockDelay = TimeSpan.FromMilliseconds(1500);
        return factory;
    }

    [Fact]
    public async Task Never_more_scans_run_at_once_than_the_pool_allows_and_the_rest_are_told_the_gateway_is_busy()
    {
        using var factory = await SlowHostAsync(slots: 2, queueWaitSeconds: 1);
        var scan = factory.Services.GetRequiredService<ScanService>();
        var address = SomeAddress();
        using var client = factory.CreateClient();

        var peak = 0;
        var watching = true;
        var watcher = Task.Run(async () =>
        {
            while (watching)
            {
                peak = Math.Max(peak, scan.ActiveScans);
                await Task.Delay(10);
            }
        });
        var requests = Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync("/v1/scan", new { addresses = new[] { address }, fromHeight = First, toHeight = Last })).ToArray();
        var responses = await Task.WhenAll(requests);
        watching = false;
        await watcher;

        Assert.True(peak <= 2, $"peak concurrent scans was {peak}");
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.ServiceUnavailable);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(0, scan.ActiveScans); // every slot came back
    }

    [Fact]
    public async Task While_scans_are_queued_health_and_headers_still_answer_at_once()
    {
        using var factory = await SlowHostAsync(slots: 1, queueWaitSeconds: 5);
        var address = SomeAddress();
        using var client = factory.CreateClient();
        var heavy = Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync("/v1/scan", new { addresses = new[] { address }, fromHeight = First, toHeight = Last })).ToArray();
        await Task.Delay(200);

        var clock = Stopwatch.StartNew();
        var health = await client.GetAsync("/v1/health");
        var headers = await client.GetAsync($"/v1/headers?from={First}&count=5");
        clock.Stop();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, headers.StatusCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"reads took {clock.Elapsed}");
        await Task.WhenAll(heavy);
    }

    [Fact]
    public async Task A_scan_that_fails_still_gives_its_slot_back()
    {
        using var factory = await SlowHostAsync(slots: 1, queueWaitSeconds: 1);
        var scan = factory.Services.GetRequiredService<ScanService>();
        using var client = factory.CreateClient();

        var refused = await client.PostAsJsonAsync("/v1/scan", new { addresses = new[] { SomeAddress() }, fromHeight = First, toHeight = First + 60_000 });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, scan.ActiveScans);
    }
}
