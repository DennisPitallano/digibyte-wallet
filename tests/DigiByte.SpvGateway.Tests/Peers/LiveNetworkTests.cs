using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Headers.Hashers;
using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Sync;
using Xunit.Abstractions;

namespace DigiByte.SpvGateway.Tests.Peers;

/// <summary>
/// Optional check against the real DigiByte network. Runs only when DGB_LIVE=1 is set, because it needs internet access and the
/// result depends on who is online. It connects to real peers and downloads and fully verifies (proof of work included) the headers
/// from the built-in checkpoint to the current tip.
/// </summary>
public class LiveNetworkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Real_peers_serve_headers_that_pass_every_check()
    {
        if (Environment.GetEnvironmentVariable("DGB_LIVE") != "1")
            return;

        var connector = new NBitcoinPeerConnector(DigiByteNetworkSet.Instance.Mainnet, TimeSpan.FromSeconds(15));
        using var book = PeerBook.Open(":memory:");
        await using var manager = new PeerManager(new SpvGatewayOptions { PeerTarget = 6 }, book, connector, connector);
        await manager.MaintainAsync(default);
        output.WriteLine($"peers {manager.Snapshot().PeerCount}, best height {manager.Snapshot().BestPeerHeight}");

        using var store = HeaderStore.Open(":memory:", "mainnet");
        var sync = new HeaderSync(store, Checkpoints.Mainnet, PowHashers.Available);
        var result = await new HeaderDownloader(store, sync, manager).DownloadAsync(default);

        output.WriteLine($"accepted {result.Accepted}, dropped {result.PeersDropped}, tip {result.TipHeight}, reached {result.Reached}");
        Assert.True(result.Reached);
        Assert.Equal(0, result.PeersDropped);
        Assert.True(result.Accepted > 500);
    }
}
