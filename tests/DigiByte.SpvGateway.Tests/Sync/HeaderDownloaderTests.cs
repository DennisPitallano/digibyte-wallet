using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using DigiByte.SpvGateway.Peers;

namespace DigiByte.SpvGateway.Tests.Sync;

/// <summary>Headers fetched from peers end up in the store, switch on a reorganization, and a peer sending bad ones is dropped (T033).</summary>
public class HeaderDownloaderTests
{
    private const int Cp = 24563100;

    private static List<BlockHeader80> Real(int from, int to) => Enumerable.Range(Cp + from, to - from + 1).Select(RealHeaders.At).ToList();

    private static async Task<(HeaderDownloader Downloader, HeaderStore Store, PeerManager Manager, FakePeerNetwork Net)> RigAsync(int peerCount = 3)
    {
        var net = new FakePeerNetwork();
        for (var i = 0; i < peerCount; i++)
        {
            var peer = net.Add($"{20 + i}.{10 + i}.1.1");
            peer.Anchor = Checkpoints.Mainnet.Hash;
            peer.Chain = Real(1, 60);
        }
        var book = PeerBook.Open(":memory:");
        var manager = new PeerManager(new SpvGatewayOptions { PeerTarget = Math.Max(1, peerCount) }, book, net, net, new FakeTimeProvider());
        await manager.MaintainAsync(default);
        var store = HeaderStore.Open(":memory:", "mainnet");
        var sync = new HeaderSync(store, Checkpoints.Mainnet, hasher: null);
        return (new HeaderDownloader(store, sync, manager), store, manager, net);
    }

    [Fact]
    public async Task Downloads_from_the_checkpoint_to_the_peer_tip()
    {
        var (downloader, store, _, _) = await RigAsync();

        var result = await downloader.DownloadAsync(default);

        Assert.True(result.Reached);
        Assert.Equal(60, result.Accepted);
        Assert.Equal(Cp + 60, store.BestTip()!.Height);
    }

    [Fact]
    public async Task A_second_download_with_nothing_new_changes_nothing()
    {
        var (downloader, store, _, _) = await RigAsync();
        await downloader.DownloadAsync(default);

        var result = await downloader.DownloadAsync(default);

        Assert.True(result.Reached);
        Assert.Equal(0, result.Accepted);
        Assert.Equal(Cp + 60, store.BestTip()!.Height);
    }

    [Fact]
    public async Task New_headers_are_picked_up_where_the_gateway_left_off()
    {
        var (downloader, store, _, net) = await RigAsync(1);
        net.Peers.Single().Chain = Real(1, 30);
        await downloader.DownloadAsync(default);

        net.Peers.Single().Chain = Real(1, 45);
        var result = await downloader.DownloadAsync(default);

        Assert.Equal(15, result.Accepted);
        Assert.Equal(Cp + 45, store.BestTip()!.Height);
    }

    [Fact]
    public async Task A_longer_branch_from_the_peers_causes_a_reorganization()
    {
        var (downloader, store, _, net) = await RigAsync(1);
        net.Peers.Single().Chain = Real(1, 30);
        await downloader.DownloadAsync(default);
        var oldTip = store.BestTip()!.Header.Hash;

        net.Peers.Single().Chain = [.. Real(1, 20), .. RealHeaders.Fork(Cp + 20, 30)];
        var result = await downloader.DownloadAsync(default);

        Assert.Equal(Cp + 50, store.BestTip()!.Height);
        Assert.NotEqual(oldTip, store.BestTip()!.Header.Hash);
        Assert.Contains(store.Reorganizations(), r => r.ForkHeight == Cp + 20 && r.Depth == 10);
        Assert.True(result.Reached);
    }

    [Fact]
    public async Task A_peer_sending_a_header_that_fails_the_checks_is_dropped_and_another_peer_is_used()
    {
        var (downloader, store, manager, net) = await RigAsync(2);
        var bad = net.Peers.First();
        var broken = Real(1, 60);
        var raw = broken[5].ToBytes();
        raw[72] ^= 0x01; // wrong difficulty
        broken[5] = BlockHeader80.Parse(raw);
        bad.Chain = broken;

        var result = await downloader.DownloadAsync(default);

        Assert.Equal(1, result.PeersDropped);
        Assert.DoesNotContain(bad.Endpoint, manager.Sessions.Select(s => s.Endpoint));
        Assert.Equal(Cp + 60, store.BestTip()!.Height);
    }

    private sealed class HangingPeer(FakePeer inner) : IPeerSession
    {
        public PeerEndpoint Endpoint => inner.Endpoint;
        public ulong Services => inner.Services;
        public string UserAgent => inner.UserAgent;
        public int ProtocolVersion => inner.ProtocolVersion;
        public int StartHeight => inner.StartHeight;
        public bool IsConnected => true;
        public async Task<IReadOnlyList<BlockHeader80>> GetHeadersAsync(IReadOnlyList<NBitcoin.uint256> locator, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
        public Task<byte[]?> GetBlockAsync(NBitcoin.uint256 hash, CancellationToken cancellationToken) => inner.GetBlockAsync(hash, cancellationToken);
        public Task<PeerRelayOutcome> RelayTransactionAsync(NBitcoin.uint256 txid, byte[] raw, CancellationToken cancellationToken) => inner.RelayTransactionAsync(txid, raw, cancellationToken);
        public bool HasAnnounced(NBitcoin.uint256 txid) => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HangingConnector(FakePeerNetwork inner, string hangingAddress) : IPeerConnector
    {
        public async Task<IPeerSession> ConnectAsync(PeerEndpoint endpoint, CancellationToken cancellationToken)
        {
            var session = (FakePeer)await inner.ConnectAsync(endpoint, cancellationToken);
            return endpoint.Address.ToString() == hangingAddress ? new HangingPeer(session) : session;
        }
    }

    [Fact]
    public async Task A_peer_that_never_answers_is_skipped_after_its_time_limit_and_the_download_still_completes()
    {
        var net = new FakePeerNetwork();
        for (var i = 0; i < 3; i++)
        {
            var peer = net.Add($"{20 + i}.{10 + i}.1.1");
            peer.Anchor = Checkpoints.Mainnet.Hash;
            peer.Chain = Real(1, 60);
        }
        var manager = new PeerManager(new SpvGatewayOptions { PeerTarget = 3 }, PeerBook.Open(":memory:"), new HangingConnector(net, "20.10.1.1"), net, new FakeTimeProvider());
        await manager.MaintainAsync(default);
        var store = HeaderStore.Open(":memory:", "mainnet");
        var downloader = new HeaderDownloader(store, new HeaderSync(store, Checkpoints.Mainnet, hasher: null), manager, TimeSpan.FromMilliseconds(300));

        var result = await downloader.DownloadAsync(default);

        Assert.True(result.Reached);
        Assert.Equal(Cp + 60, store.BestTip()!.Height);
    }

    [Fact]
    public async Task With_no_peers_nothing_happens_and_the_gateway_is_not_level()
    {
        var (downloader, store, _, _) = await RigAsync(0);

        var result = await downloader.DownloadAsync(default);

        Assert.False(result.Reached);
        Assert.Equal(Cp, store.BestTip()!.Height);
    }
}
