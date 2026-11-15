using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Headers.Hashers;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;

namespace DigiByte.SpvGateway.Tests.Sync;

public class HeaderSyncTests
{
    private const int Cp = 24563100;

    private static (HeaderStore Store, HeaderSync Sync) New(IPowHasher? hasher)
    {
        var store = HeaderStore.Open(":memory:", "mainnet");
        return (store, new HeaderSync(store, Checkpoints.Mainnet, hasher));
    }

    private static IEnumerable<BlockHeader80> Real(int fromOffset, int toOffset) =>
        Enumerable.Range(Cp + fromOffset, toOffset - fromOffset + 1).Select(RealHeaders.At);

    [Fact]
    public void Syncs_every_real_header_after_the_checkpoint_with_proof_of_work_checked()
    {
        var (store, sync) = New(PowHashers.Available);
        using var _ = store;
        var last = RealHeaders.Run[^1].Height;

        var result = sync.Ingest(Real(1, last - Cp));

        Assert.Null(result.Rejected);
        Assert.Equal(last - Cp, result.Accepted);
        Assert.Equal(last, result.TipHeight);
        Assert.Equal(0, result.ReorgDepth);
        Assert.Equal(RealHeaders.At(last).Hash, store.BestTip()!.Header.Hash);
        Assert.True(store.BestTip()!.ChainWork > 0);
    }

    [Fact]
    public void Chain_work_grows_with_every_header()
    {
        var (store, sync) = New(PowHashers.Available);
        using var _ = store;
        sync.Ingest(Real(1, 10));

        var previous = store.GetBest(Cp)!.ChainWork;
        for (var i = 1; i <= 10; i++)
        {
            var work = store.GetBest(Cp + i)!.ChainWork;
            Assert.True(work > previous);
            previous = work;
        }
    }

    [Fact]
    public void Repeating_a_batch_counts_duplicates_and_changes_nothing()
    {
        var (store, sync) = New(PowHashers.Available);
        using var _ = store;
        sync.Ingest(Real(1, 5));

        var again = sync.Ingest(Real(1, 5));

        Assert.Equal(0, again.Accepted);
        Assert.Equal(5, again.Duplicates);
        Assert.Equal(Cp + 5, again.TipHeight);
    }

    [Fact]
    public void A_header_with_a_broken_link_is_refused_and_nothing_after_it_is_taken()
    {
        var (store, sync) = New(PowHashers.Available);
        using var _ = store;

        var result = sync.Ingest(new[] { RealHeaders.At(Cp + 1), RealHeaders.At(Cp + 3), RealHeaders.At(Cp + 4) });

        Assert.Equal(HeaderRejection.NotLinked, result.Rejected);
        Assert.Equal(1, result.Accepted);
        Assert.Equal(Cp + 1, result.TipHeight);
    }

    [Fact]
    public void A_header_whose_hash_does_not_meet_its_target_is_refused()
    {
        var (store, sync) = New(PowHashers.Available);
        using var _ = store;
        var raw = RealHeaders.At(Cp + 1).ToBytes();
        raw[76] ^= 0xFF;

        var result = sync.Ingest(new[] { BlockHeader80.Parse(raw) });

        Assert.Equal(HeaderRejection.ProofOfWorkFailed, result.Rejected);
        Assert.Equal(0, result.Accepted);
        Assert.Equal(Cp, result.TipHeight);
    }

    [Fact]
    public void A_header_with_the_wrong_difficulty_is_refused()
    {
        var (store, sync) = New(PowHashers.Available);
        using var _ = store;
        var raw = RealHeaders.At(Cp + 1).ToBytes();
        raw[72] ^= 0x01;

        Assert.Equal(HeaderRejection.WrongDifficulty, sync.Ingest(new[] { BlockHeader80.Parse(raw) }).Rejected);
    }

    [Fact]
    public void A_six_block_reorganization_switches_the_best_chain_and_keeps_the_losing_branch()
    {
        // No hasher here: the fork is made by changing nonces, which only the proof-of-work check would catch.
        var (store, sync) = New(null);
        using var _ = store;
        sync.Ingest(Real(1, 6));
        var losing = Real(1, 6).ToList();
        var fork = RealHeaders.Fork(Cp, 7);

        var result = sync.Ingest(fork);

        Assert.Null(result.Rejected);
        Assert.Equal(6, result.ReorgDepth);
        Assert.Equal(Cp + 7, result.TipHeight);
        Assert.Equal(fork[^1].Hash, store.BestTip()!.Header.Hash);
        foreach (var h in losing)
        {
            var stored = store.Get(h.Hash);
            Assert.NotNull(stored);
            Assert.False(stored!.IsBestChain);
        }
        Assert.Equal([(Cp, 6)], store.Reorganizations());
    }

    [Fact]
    public void A_fork_with_less_work_does_not_take_over()
    {
        var (store, sync) = New(null);
        using var _ = store;
        sync.Ingest(Real(1, 6));

        var result = sync.Ingest(RealHeaders.Fork(Cp, 5));

        Assert.Equal(0, result.ReorgDepth);
        Assert.Equal(Cp + 6, result.TipHeight);
        Assert.Equal(RealHeaders.At(Cp + 6).Hash, store.BestTip()!.Header.Hash);
    }

    [Fact]
    public void A_fork_with_equal_work_does_not_replace_the_current_best_chain()
    {
        var (store, sync) = New(null);
        using var _ = store;
        sync.Ingest(Real(1, 6));

        var result = sync.Ingest(RealHeaders.Fork(Cp, 6));

        Assert.Equal(0, result.ReorgDepth);
        Assert.Equal(RealHeaders.At(Cp + 6).Hash, store.BestTip()!.Header.Hash);
    }
}
