using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.SpvGateway.Tests.Sync;

/// <summary>The index keeps up with the header chain (T062): oldest first, a limited number per round, never before the earliest header.</summary>
public class BlockIndexerTests
{
    private static readonly int First = GatewayWorld.Heights.First();
    private static readonly int Last = GatewayWorld.Heights.Last();

    private static async Task<(BlockIndexer Indexer, BlockIndexStore Index, GatewayFactory Factory, GatewayWorld World)> RigAsync(int scanWindowDays = 90)
    {
        var world = await GatewayWorld.CreateAsync();
        var factory = new GatewayFactory(new Dictionary<string, string?> { ["SpvGateway:ScanWindowDays"] = scanWindowDays.ToString() }, world.Register);
        factory.Sync.Ingest(GatewayWorld.Heights.Select(RealHeaders.At));
        return (factory.Services.GetRequiredService<BlockIndexer>(), factory.Services.GetRequiredService<BlockIndexStore>(), factory, world);
    }

    [Fact]
    public async Task Indexes_from_the_first_block_after_the_earliest_header_up_to_the_tip()
    {
        var (indexer, index, factory, _) = await RigAsync();
        using var _f = factory;

        var added = await indexer.IndexAsync(default);

        Assert.Equal(GatewayWorld.Heights.Count, added);
        Assert.Equal(First, index.IndexedFromHeight());
        Assert.Equal(Last, index.IndexedToHeight());
    }

    [Fact]
    public async Task A_round_adds_at_most_the_asked_number_of_blocks_and_the_next_round_continues()
    {
        var (indexer, index, factory, _) = await RigAsync();
        using var _f = factory;

        var first = await indexer.IndexAsync(default, maxBlocks: 10);
        var second = await indexer.IndexAsync(default, maxBlocks: 10);

        Assert.Equal(10, first);
        Assert.Equal(10, second);
        Assert.Equal(GatewayWorld.Heights[19], index.IndexedToHeight());
    }

    [Fact]
    public async Task Nothing_is_added_twice()
    {
        var (indexer, index, factory, _) = await RigAsync();
        using var _f = factory;
        await indexer.IndexAsync(default);
        var entries = index.EntryCount();

        var again = await indexer.IndexAsync(default);

        Assert.Equal(0, again);
        Assert.Equal(entries, index.EntryCount());
    }

    [Fact]
    public async Task When_no_peer_has_a_block_the_round_stops_there_and_the_next_one_resumes()
    {
        var (indexer, index, factory, world) = await RigAsync();
        using var _f = factory;
        var peer = world.Peers.Peers.Single();
        var missing = GatewayWorld.Heights[5];
        var held = peer.Blocks.Single(b => RealHeaders.At(missing).Hash == b.Key);
        peer.Blocks.Remove(held.Key);

        var firstRound = await indexer.IndexAsync(default);
        peer.Blocks[held.Key] = held.Value;
        var secondRound = await indexer.IndexAsync(default);

        Assert.Equal(5, firstRound);
        Assert.Equal(GatewayWorld.Heights.Count - 5, secondRound);
        Assert.Equal(Last, index.IndexedToHeight());
    }

    [Fact]
    public async Task A_block_that_is_no_longer_on_the_best_chain_is_dropped_from_the_index()
    {
        var (indexer, index, factory, _) = await RigAsync();
        using var _f = factory;
        await indexer.IndexAsync(default);
        var height = GatewayWorld.Heights[8];
        var stale = BlockIndexBuilder.Build(GatewayWorld.At(height).Block, height) with { Hash = NBitcoin.uint256.One };
        index.Add(stale, DateTimeOffset.UtcNow);

        await indexer.IndexAsync(default);

        // The stale entry is removed, and the real block is not re-added until the next round fills the gap.
        Assert.False(index.Has(height, NBitcoin.uint256.One));
    }

    [Theory]
    [InlineData(24563140, 24563100, 90, 24563101)]
    [InlineData(25_000_000, 24_000_000, 1, 25_000_000 - 5760 + 1)]
    [InlineData(25_000_000, 24_996_000, 1, 24_996_001)]
    public void The_window_starts_a_number_of_days_back_but_never_before_the_earliest_header(int tip, int earliest, int days, int expected)
    {
        var indexer = new BlockIndexer(new SpvGatewayOptions { ScanWindowDays = days }, null!, null!, null!);

        Assert.Equal(expected, indexer.WindowStart(tip, earliest));
    }
}
