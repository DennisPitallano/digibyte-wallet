using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Matching;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Index;

/// <summary>The self-built match index (T046, T062) and the matcher on top of it (T063), using real mainnet blocks.</summary>
public class BlockIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static (int Height, uint256 Hash, Block Block) WithTransactions(int minTransactions)
    {
        var found = RealBlocks.All.Select(b => (b.Height, b.Hash, Block: RealBlocks.Parse(b.Raw))).Where(b => b.Block.Transactions.Count >= minTransactions).First();
        return found;
    }

    [Fact]
    public void The_real_blocks_include_one_with_several_transactions()
    {
        Assert.True(RealBlocks.All.Count > 20);
        Assert.Contains(RealBlocks.All, b => RealBlocks.Parse(b.Raw).Transactions.Count >= 3);
    }

    [Fact]
    public void Every_output_script_and_every_spent_outpoint_is_recorded_with_its_transaction_position()
    {
        var (height, hash, block) = WithTransactions(3);

        var entry = BlockIndexBuilder.Build(block, height);

        Assert.Equal(height, entry.Height);
        Assert.Equal(hash, entry.Hash);
        Assert.Equal(block.Transactions.Count, entry.TransactionCount);
        var expectedOutputs = block.Transactions.SelectMany((tx, i) => tx.Outputs.Select(o => (i, ScriptKey.Of(o.ScriptPubKey)))).ToList();
        Assert.Equal(expectedOutputs.Count, entry.Outputs.Count);
        foreach (var (index, key) in expectedOutputs)
            Assert.Contains(entry.Outputs, o => o.TxIndex == index && o.Key.SequenceEqual(key));

        // The coinbase spends nothing; every other input is recorded.
        var expectedSpends = block.Transactions.Skip(1).SelectMany((tx, i) => tx.Inputs.Select(input => (i + 1, OutpointKey.Of(input.PrevOut)))).ToList();
        Assert.Equal(expectedSpends.Count, entry.Spends.Count);
        Assert.All(expectedSpends, s => Assert.Contains(entry.Spends, o => o.TxIndex == s.Item1 && o.Key.SequenceEqual(s.Item2)));
    }

    [Fact]
    public void The_matcher_finds_the_transactions_that_pay_a_watched_script()
    {
        using var store = BlockIndexStore.Open(":memory:");
        var (height, _, block) = WithTransactions(3);
        store.Add(BlockIndexBuilder.Build(block, height), Now);
        var target = block.Transactions[1].Outputs[0].ScriptPubKey;
        var other = new Key().GetScriptPubKey(ScriptPubKeyType.Legacy);

        var hits = new FullBlockMatcher(store).Match([ScriptKey.Of(target)], [], height, height);
        var none = new FullBlockMatcher(store).Match([ScriptKey.Of(other)], [], height, height);

        Assert.Contains(hits, h => h.Height == height && h.TxIndex == 1);
        Assert.Empty(none);
    }

    [Fact]
    public void The_matcher_finds_a_transaction_that_spends_a_known_outpoint()
    {
        using var store = BlockIndexStore.Open(":memory:");
        var (height, _, block) = WithTransactions(3);
        store.Add(BlockIndexBuilder.Build(block, height), Now);
        var spent = block.Transactions[1].Inputs[0].PrevOut;

        var hits = new FullBlockMatcher(store).Match([], [OutpointKey.Of(spent)], height, height);

        Assert.Contains(hits, h => h.Height == height && h.TxIndex == 1);
    }

    [Fact]
    public void Only_blocks_inside_the_asked_range_are_matched()
    {
        using var store = BlockIndexStore.Open(":memory:");
        foreach (var raw in RealBlocks.All.Take(10))
            store.Add(BlockIndexBuilder.Build(RealBlocks.Parse(raw.Raw), raw.Height), Now);
        var first = RealBlocks.All[0];
        var coinbaseScript = RealBlocks.Parse(first.Raw).Transactions[0].Outputs[0].ScriptPubKey;

        var inRange = new FullBlockMatcher(store).Match([ScriptKey.Of(coinbaseScript)], [], first.Height, first.Height);
        var outOfRange = new FullBlockMatcher(store).Match([ScriptKey.Of(coinbaseScript)], [], first.Height + 1000, first.Height + 2000);

        Assert.Contains(inRange, h => h.Height == first.Height);
        Assert.Empty(outOfRange);
    }

    [Fact]
    public void The_index_reports_what_it_covers()
    {
        using var store = BlockIndexStore.Open(":memory:");
        Assert.Null(store.IndexedFromHeight());

        foreach (var raw in RealBlocks.All.Take(5))
            store.Add(BlockIndexBuilder.Build(RealBlocks.Parse(raw.Raw), raw.Height), Now);

        Assert.Equal(RealBlocks.All[0].Height, store.IndexedFromHeight());
        Assert.Equal(RealBlocks.All[4].Height, store.IndexedToHeight());
        Assert.True(store.Has(RealBlocks.All[2].Height, RealBlocks.All[2].Hash));
        Assert.False(store.Has(RealBlocks.All[2].Height, uint256.One));
    }

    [Fact]
    public void Adding_the_same_block_twice_changes_nothing()
    {
        using var store = BlockIndexStore.Open(":memory:");
        var (height, _, block) = WithTransactions(3);
        var entry = BlockIndexBuilder.Build(block, height);
        store.Add(entry, Now);
        var before = store.EntryCount();

        store.Add(entry, Now);

        Assert.Equal(before, store.EntryCount());
    }

    [Fact]
    public void Entries_older_than_the_scan_window_are_dropped()
    {
        using var store = BlockIndexStore.Open(":memory:");
        foreach (var raw in RealBlocks.All.Take(6))
            store.Add(BlockIndexBuilder.Build(RealBlocks.Parse(raw.Raw), raw.Height), Now);

        store.PruneBelow(RealBlocks.All[3].Height);

        Assert.Equal(RealBlocks.All[3].Height, store.IndexedFromHeight());
        Assert.False(store.Has(RealBlocks.All[0].Height, RealBlocks.All[0].Hash));
    }

    [Fact]
    public void A_block_that_leaves_the_best_chain_is_removed_from_the_index()
    {
        // Index the same height under a hash that is not the best chain hash, as if a reorganization had replaced it.
        using var index = BlockIndexStore.Open(":memory:");
        using var headers = HeaderStore.Open(":memory:", "mainnet");
        headers.SeedCheckpoint(Checkpoints.Mainnet);
        var sync = new HeaderSync(headers, Checkpoints.Mainnet, hasher: null);
        sync.Ingest(Enumerable.Range(24563101, 12).Select(RealHeaders.At));

        foreach (var raw in RealBlocks.All.Where(b => b.Height <= 24563112))
            index.Add(BlockIndexBuilder.Build(RealBlocks.Parse(raw.Raw), raw.Height), Now);
        var staleHeight = 24563105;
        var staleEntry = BlockIndexBuilder.Build(RealBlocks.Parse(RealBlocks.All.First(b => b.Height == staleHeight).Raw), staleHeight);
        index.Remove(staleHeight);
        index.Add(staleEntry with { Hash = uint256.One }, Now);

        var removed = index.RemoveNotOnBestChain(headers);

        Assert.Equal(1, removed);
        Assert.False(index.Has(staleHeight, uint256.One));
    }
}
