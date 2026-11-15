using System.Numerics;
using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Sync;

public class HeaderStoreTests
{
    private const int Cp = 24563100;

    private static HeaderStore NewStore()
    {
        var store = HeaderStore.Open(":memory:", "mainnet");
        store.SeedCheckpoint(Checkpoints.Mainnet);
        return store;
    }

    [Fact]
    public void Seeding_the_checkpoint_makes_it_the_best_tip_and_is_repeatable()
    {
        using var store = NewStore();
        store.SeedCheckpoint(Checkpoints.Mainnet);

        Assert.Equal(Cp, store.BestTip()!.Height);
        Assert.Equal(Checkpoints.Mainnet.Hash, store.BestTip()!.Header.Hash);
        Assert.Equal(Checkpoints.Mainnet.FirstHeight, store.EarliestHeight());
        Assert.Equal(Checkpoints.Mainnet.Headers.Count, store.BestRange(0, 100_000_000).Count);
    }

    [Fact]
    public void A_header_is_stored_once_and_a_second_copy_is_reported_as_a_duplicate()
    {
        using var store = NewStore();
        var header = RealHeaders.At(Cp + 1);

        Assert.Equal(StoreAddResult.Added, store.TryAdd(header, Cp + 1, new BigInteger(10)));
        Assert.Equal(StoreAddResult.Duplicate, store.TryAdd(header, Cp + 1, new BigInteger(10)));
    }

    [Fact]
    public void A_header_whose_parent_is_not_stored_is_refused()
    {
        using var store = NewStore();

        Assert.Equal(StoreAddResult.ParentMissing, store.TryAdd(RealHeaders.At(Cp + 2), Cp + 2, BigInteger.One));
    }

    [Fact]
    public void A_header_must_sit_one_above_its_parent()
    {
        using var store = NewStore();

        Assert.Equal(StoreAddResult.HeightMismatch, store.TryAdd(RealHeaders.At(Cp + 1), Cp + 5, BigInteger.One));
    }

    [Fact]
    public void A_header_with_an_unknown_algorithm_is_refused()
    {
        using var store = NewStore();
        var raw = RealHeaders.At(Cp + 1).ToBytes();
        BitConverter.GetBytes((BitConverter.ToInt32(raw, 0) & ~PowAlgorithms.VersionAlgoMask) | (5 << 8)).CopyTo(raw, 0);

        Assert.Equal(StoreAddResult.UnknownAlgorithm, store.TryAdd(BlockHeader80.Parse(raw), Cp + 1, BigInteger.One));
    }

    [Fact]
    public void Chain_work_round_trips_for_large_values()
    {
        using var store = NewStore();
        var work = BigInteger.Pow(2, 100) + 12345;
        store.TryAdd(RealHeaders.At(Cp + 1), Cp + 1, work);

        Assert.Equal(work, store.Get(RealHeaders.At(Cp + 1).Hash)!.ChainWork);
    }

    [Fact]
    public void Switching_the_best_chain_moves_the_mark_in_one_step_and_keeps_the_losing_branch()
    {
        using var store = NewStore();
        var real = Enumerable.Range(1, 3).Select(i => RealHeaders.At(Cp + i)).ToList();
        var fork = RealHeaders.Fork(Cp, 4);
        for (var i = 0; i < real.Count; i++)
            store.TryAdd(real[i], Cp + 1 + i, new BigInteger(10 * (i + 1)));
        Assert.Equal(0, store.SetBestTip(real[^1].Hash));
        for (var i = 0; i < fork.Count; i++)
            store.TryAdd(fork[i], Cp + 1 + i, new BigInteger(11 * (i + 1)));

        var depth = store.SetBestTip(fork[^1].Hash);

        Assert.Equal(3, depth);
        Assert.Equal(fork[^1].Hash, store.BestTip()!.Header.Hash);
        for (var i = 0; i < fork.Count; i++)
            Assert.Equal(fork[i].Hash, store.GetBest(Cp + 1 + i)!.Header.Hash);
        foreach (var h in real)
            Assert.False(store.Get(h.Hash)!.IsBestChain);
        Assert.Equal([(Cp, 3)], store.Reorganizations());
    }

    [Fact]
    public void Extending_the_best_chain_is_not_a_reorganization()
    {
        using var store = NewStore();
        store.TryAdd(RealHeaders.At(Cp + 1), Cp + 1, new BigInteger(10));

        Assert.Equal(0, store.SetBestTip(RealHeaders.At(Cp + 1).Hash));
        Assert.Empty(store.Reorganizations());
    }

    [Fact]
    public void The_store_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hs-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = HeaderStore.Open(path, "mainnet"))
            {
                store.SeedCheckpoint(Checkpoints.Mainnet);
                store.TryAdd(RealHeaders.At(Cp + 1), Cp + 1, new BigInteger(10));
                store.SetBestTip(RealHeaders.At(Cp + 1).Hash);
            }
            using var reopened = HeaderStore.Open(path, "mainnet");

            Assert.Equal(Cp + 1, reopened.BestTip()!.Height);
            Assert.Equal(new BigInteger(10), reopened.BestTip()!.ChainWork);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f))
                    File.Delete(f);
        }
    }

    [Fact]
    public void Branch_loading_follows_previous_hashes_not_the_best_chain()
    {
        using var store = NewStore();
        var fork = RealHeaders.Fork(Cp, 3);
        for (var i = 0; i < fork.Count; i++)
            store.TryAdd(fork[i], Cp + 1 + i, new BigInteger(i + 1));

        var branch = store.BranchEndingAt(fork[^1].Hash, 5);

        Assert.Equal(5, branch.Count);
        Assert.Equal(fork[^1].Hash, branch[^1].Header.Hash);
        Assert.Equal(Cp - 1, branch[0].Height);
    }
}
