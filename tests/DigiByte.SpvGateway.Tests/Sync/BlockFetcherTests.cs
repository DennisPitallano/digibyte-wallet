using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Sync;

/// <summary>Blocks come only from full-history peers and are used only when they match the stored header (T061).</summary>
public class BlockFetcherTests
{
    private const ulong Limited = 0x400;

    private static (int Height, uint256 Hash, byte[] Raw) Sample => RealBlocks.All.First(b => RealBlocks.Parse(b.Raw).Transactions.Count >= 3);

    private static async Task<(BlockFetcher Fetcher, FakePeerNetwork Net, PeerManager Manager, PeerBook Book)> RigAsync(params (ulong Services, Action<FakePeer>? Setup)[] peers)
    {
        var net = new FakePeerNetwork();
        for (var i = 0; i < peers.Length; i++)
        {
            var peer = net.Add($"{20 + i}.{10 + i}.1.1", peers[i].Services);
            peer.Blocks[Sample.Hash] = Sample.Raw;
            peers[i].Setup?.Invoke(peer);
        }
        var book = PeerBook.Open(":memory:");
        var manager = new PeerManager(new SpvGatewayOptions { PeerTarget = Math.Max(1, peers.Length) }, book, net, net, new FakeTimeProvider());
        await manager.MaintainAsync(default);
        var store = HeaderStore.Open(":memory:", "mainnet");
        new HeaderSync(store, Checkpoints.Mainnet, hasher: null).Ingest(Enumerable.Range(24563101, 40).Select(RealHeaders.At));
        return (new BlockFetcher(manager, store, DigiByteNetworkSet.Instance.Mainnet), net, manager, book);
    }

    [Fact]
    public async Task A_valid_block_is_returned()
    {
        var (fetcher, _, _, _) = await RigAsync((PeerRecord.NodeNetwork, null));

        var block = await fetcher.FetchAsync(Sample.Height, default);

        Assert.NotNull(block);
        Assert.Equal(Sample.Hash, block!.GetHash());
    }

    [Fact]
    public async Task Limited_peers_are_never_asked_for_blocks()
    {
        var (fetcher, net, _, _) = await RigAsync((Limited, null));

        var block = await fetcher.FetchAsync(Sample.Height, default);

        Assert.Null(block);
        Assert.All(net.Peers, p => Assert.Empty(p.BlockRequests));
    }

    [Fact]
    public async Task A_peer_that_lacks_the_block_is_skipped_and_the_next_one_is_used()
    {
        var (fetcher, net, _, _) = await RigAsync((PeerRecord.NodeNetwork, p => p.Blocks.Clear()), (PeerRecord.NodeNetwork, null));

        var block = await fetcher.FetchAsync(Sample.Height, default);

        Assert.NotNull(block);
        // The requests are spread over the peers, so the one without the block may or may not be asked first; the good one answered.
        Assert.Contains(net.Peers, p => p.BlockRequests.Count == 1 && p.Blocks.ContainsKey(Sample.Hash));
    }

    [Fact]
    public async Task A_block_whose_transactions_were_changed_is_refused_and_every_peer_that_sent_it_is_banned()
    {
        var tampered = (byte[])Sample.Raw.Clone();
        tampered[^1] ^= 0x01; // the lock time of the last transaction, which is part of its id
        var (fetcher, net, manager, book) = await RigAsync(
            (PeerRecord.NodeNetwork, p => p.Blocks[Sample.Hash] = tampered), (PeerRecord.NodeNetwork, p => p.Blocks[Sample.Hash] = tampered));

        var block = await fetcher.FetchAsync(Sample.Height, default);

        Assert.Null(block);
        Assert.All(net.Peers, p => Assert.NotNull(book.Get(p.Endpoint)!.BannedUntil));
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task A_peer_that_sends_a_bad_block_does_not_stop_a_good_peer_from_being_used()
    {
        var tampered = (byte[])Sample.Raw.Clone();
        tampered[^1] ^= 0x01;
        var (fetcher, net, _, book) = await RigAsync((PeerRecord.NodeNetwork, p => p.Blocks[Sample.Hash] = tampered), (PeerRecord.NodeNetwork, null));

        var block = await fetcher.FetchAsync(Sample.Height, default);

        Assert.NotNull(block);
        var bad = net.Peers.First();
        if (bad.BlockRequests.Count > 0)
            Assert.NotNull(book.Get(bad.Endpoint)!.BannedUntil);
    }

    [Fact]
    public async Task A_different_block_than_the_one_asked_for_is_refused()
    {
        var other = RealBlocks.All.First(b => b.Hash != Sample.Hash);
        var (fetcher, _, _, _) = await RigAsync((PeerRecord.NodeNetwork, p => p.Blocks[Sample.Hash] = other.Raw));

        Assert.Null(await fetcher.FetchAsync(Sample.Height, default));
    }

    [Fact]
    public async Task Garbage_bytes_are_refused_without_throwing()
    {
        var (fetcher, _, _, _) = await RigAsync((PeerRecord.NodeNetwork, p => p.Blocks[Sample.Hash] = [1, 2, 3]));

        Assert.Null(await fetcher.FetchAsync(Sample.Height, default));
    }

    [Fact]
    public async Task A_height_the_gateway_has_no_header_for_gives_nothing_and_asks_no_peer()
    {
        var (fetcher, net, _, _) = await RigAsync((PeerRecord.NodeNetwork, null));

        Assert.Null(await fetcher.FetchAsync(30_000_000, default));
        Assert.All(net.Peers, p => Assert.Empty(p.BlockRequests));
    }
}

public class BlockFetcherScoringTests
{
    [Fact]
    public async Task A_peer_that_keeps_saying_it_has_no_block_is_asked_less_and_less_while_a_good_peer_serves_everything()
    {
        var net = new FakePeerNetwork();
        var good = net.Add("21.11.1.1");
        var bad = net.Add("22.12.1.1");
        foreach (var b in RealBlocks.All)
            good.Blocks[b.Hash] = b.Raw; // the bad peer has none
        var manager = new DigiByte.SpvGateway.Peers.PeerManager(new SpvGatewayOptions { PeerTarget = 2 }, DigiByte.SpvGateway.Peers.PeerBook.Open(":memory:"), net, net, new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        await manager.MaintainAsync(default);
        var store = DigiByte.SpvGateway.Sync.HeaderStore.Open(":memory:", "mainnet");
        new DigiByte.SpvGateway.Sync.HeaderSync(store, DigiByte.Crypto.Headers.Checkpoints.Mainnet, hasher: null).Ingest(GatewayWorld.Heights.Select(RealHeaders.At));
        var fetcher = new DigiByte.SpvGateway.Sync.BlockFetcher(manager, store, DigiByte.Crypto.Networks.DigiByteNetworkSet.Instance.Mainnet);

        foreach (var height in GatewayWorld.Heights)
            Assert.NotNull(await fetcher.FetchAsync(height, default));

        // It was tried at the start, then learned from: far fewer requests than blocks.
        Assert.True(bad.BlockRequests.Count < GatewayWorld.Heights.Count / 2, $"the bad peer was asked {bad.BlockRequests.Count} times");
    }
}
