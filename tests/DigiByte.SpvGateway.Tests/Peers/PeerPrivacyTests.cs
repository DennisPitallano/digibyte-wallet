using System.Net.Http.Json;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Peers;

/// <summary>Nothing derived from a watched address ever goes to a peer (T052, spec FR-005 and FR-006).</summary>
public class PeerPrivacyTests
{
    [Fact]
    public async Task Peers_are_only_ever_sent_block_hashes_locators_and_the_transaction_being_broadcast()
    {
        var world = await GatewayWorld.CreateAsync();
        for (var i = 1; i < 5; i++)
            world.Peers.Add($"{30 + i}.{20 + i}.1.1");
        await world.Manager.MaintainAsync(default);
        using var factory = new GatewayFactory(null, world.Register);
        factory.Sync.Ingest(GatewayWorld.Heights.Select(RealHeaders.At));
        var fetcher = factory.Services.GetRequiredService<DigiByte.SpvGateway.Sync.BlockFetcher>();
        var index = factory.Services.GetRequiredService<DigiByte.SpvGateway.Index.BlockIndexStore>();
        await GatewayWorld.IndexAsync(index, fetcher, GatewayWorld.Heights.First(), GatewayWorld.Heights.Last());

        var (_, block) = GatewayWorld.Heights.Select(GatewayWorld.At).First(b => b.Block.Transactions.Count > 2);
        var watched = block.Transactions[1].Outputs.Select(GatewayWorld.AddressOf).First(a => a is not null)!;
        var script = BitcoinAddress.Create(watched, GatewayWorld.Network).ScriptPubKey;
        var unrelatedTx = block.Transactions[2];
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/v1/scan", new { addresses = new[] { watched }, fromHeight = GatewayWorld.Heights.First(), toHeight = GatewayWorld.Heights.Last() });
        await client.PostAsJsonAsync("/v1/utxos", new { addresses = new[] { watched }, fromHeight = GatewayWorld.Heights.First() });
        await client.PostAsJsonAsync("/v1/broadcast", new { raw = unrelatedTx.ToHex() });

        var watchedBytes = new[] { script.ToBytes(), System.Text.Encoding.UTF8.GetBytes(watched), DigiByte.SpvGateway.Index.ScriptKey.Of(script), script.Hash.ToBytes() };
        foreach (var peer in world.Peers.Peers)
        {
            var received = peer.BlockRequests.Select(h => h.ToBytes())
                .Concat(peer.Locators.SelectMany(l => l.Select(h => h.ToBytes())))
                .Concat(peer.Relayed)
                .ToList();
            foreach (var message in received)
                foreach (var secret in watchedBytes)
                    Assert.False(message.AsSpan().IndexOf(secret) >= 0, "a peer was sent something derived from a watched address");
            Assert.All(peer.Relayed, relayed => Assert.Equal(unrelatedTx.ToBytes(), relayed));
        }
        Assert.Contains(world.Peers.Peers, p => p.BlockRequests.Count > 0);
    }

    [Fact]
    public void The_peer_interface_has_no_way_to_send_an_address_a_script_or_a_filter()
    {
        var parameterTypes = typeof(IPeerSession).GetMethods()
            .Where(m => m.DeclaringType == typeof(IPeerSession))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.DoesNotContain(typeof(string), parameterTypes);
        Assert.DoesNotContain(typeof(Script), parameterTypes);
        Assert.DoesNotContain(typeof(BitcoinAddress), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Filter", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Bloom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_real_connector_never_asks_peers_to_load_a_filter_or_a_bloom_filter()
    {
        var source = File.ReadAllText(System.IO.Path.Combine(FindRepositoryRoot(), "src", "DigiByte.SpvGateway", "Peers", "NBitcoinPeerConnector.cs"));

        foreach (var banned in new[] { "FilterLoad", "FilterAdd", "BloomFilter", "MemPoolPayload" })
            Assert.DoesNotContain(banned, source);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "src", "DigiByte.SpvGateway")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
    }
}
