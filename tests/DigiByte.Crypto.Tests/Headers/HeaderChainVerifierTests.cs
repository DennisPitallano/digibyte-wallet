using DigiByte.Crypto.Headers;
using NBitcoin;

namespace DigiByte.Crypto.Tests.Headers;

/// <summary>
/// The wallet and the gateway follow the header chain from a built-in checkpoint
/// (specs/011-p2p-spv-gateway, tasks T013 and T020). Every header after the checkpoint must link, use an allowed
/// algorithm and carry exactly the difficulty DigiByte's per-algorithm V4 retarget requires.
/// </summary>
public class HeaderChainVerifierTests
{
    private const int CheckpointHeight = 24563100;

    private static HeaderChain NewChain(IPowHasher? hasher = null) => new(Checkpoints.Mainnet, DigiByteConsensus.Mainnet, hasher);

    private static IEnumerable<BlockHeader80> After(int fromHeight, int toHeight) =>
        HeaderFixtures.Run.Where(r => r.Height >= fromHeight && r.Height <= toHeight).Select(r => BlockHeader80.Parse(r.Raw));

    [Fact]
    public void The_built_in_checkpoint_matches_the_real_chain_and_has_a_long_enough_window()
    {
        var cp = Checkpoints.Mainnet;

        Assert.Equal(CheckpointHeight, cp.Height);
        Assert.Equal(HeaderFixtures.At(CheckpointHeight).Id, cp.Hash.ToString());
        Assert.True(cp.Headers.Count >= 61, "needs 50 blocks back plus 11 for the median time");
        foreach (var h in cp.Headers.Select((hdr, i) => (hdr, height: cp.FirstHeight + i)))
            Assert.Equal(HeaderFixtures.At(h.height).Id, h.hdr.Hash.ToString());
    }

    [Fact]
    public void Accepts_every_real_header_after_the_checkpoint_including_all_five_algorithms()
    {
        var chain = NewChain();
        var seen = new HashSet<PowAlgorithm>();

        foreach (var header in After(CheckpointHeight + 1, int.MaxValue))
        {
            var result = chain.TryAppend(header);
            Assert.True(result.Accepted, $"height {chain.TipHeight + 1}: {result.Reason}");
            seen.Add(header.Algorithm);
        }

        Assert.Equal(HeaderFixtures.Run[^1].Height, chain.TipHeight);
        Assert.Equal(HeaderFixtures.Run[^1].Id, chain.TipHash.ToString());
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void Rejects_a_header_that_does_not_link_to_the_tip()
    {
        var chain = NewChain();
        var skipped = HeaderFixtures.Header(CheckpointHeight + 2);

        var result = chain.TryAppend(skipped);

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.NotLinked, result.Reason);
        Assert.Equal(CheckpointHeight, chain.TipHeight);
    }

    [Fact]
    public void Rejects_a_header_whose_previous_hash_is_wrong_by_one_byte()
    {
        var chain = NewChain();
        var raw = (byte[])HeaderFixtures.At(CheckpointHeight + 1).Raw.Clone();
        raw[4] ^= 0x01;

        var result = chain.TryAppend(BlockHeader80.Parse(raw));

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.NotLinked, result.Reason);
    }

    [Fact]
    public void Rejects_a_header_whose_difficulty_does_not_match_the_retarget_rule()
    {
        var chain = NewChain();
        var raw = (byte[])HeaderFixtures.At(CheckpointHeight + 1).Raw.Clone();
        // nBits sits at offset 72; make the target one step easier than the rule allows.
        raw[72] ^= 0x01;

        var result = chain.TryAppend(BlockHeader80.Parse(raw));

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.WrongDifficulty, result.Reason);
    }

    [Fact]
    public void Rejects_a_header_with_an_unknown_algorithm()
    {
        var chain = NewChain();
        var raw = (byte[])HeaderFixtures.At(CheckpointHeight + 1).Raw.Clone();
        var version = BitConverter.ToInt32(raw, 0);
        version = (version & ~PowAlgorithms.VersionAlgoMask) | (5 << 8);
        BitConverter.GetBytes(version).CopyTo(raw, 0);

        var result = chain.TryAppend(BlockHeader80.Parse(raw));

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.UnknownAlgorithm, result.Reason);
    }

    [Fact]
    public void Rejects_a_header_using_groestl_after_it_was_switched_off()
    {
        var chain = NewChain();
        var raw = (byte[])HeaderFixtures.At(CheckpointHeight + 1).Raw.Clone();
        var version = BitConverter.ToInt32(raw, 0);
        version = (version & ~PowAlgorithms.VersionAlgoMask) | (4 << 8);
        BitConverter.GetBytes(version).CopyTo(raw, 0);

        var result = chain.TryAppend(BlockHeader80.Parse(raw));

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.AlgorithmNotAllowed, result.Reason);
    }

    [Fact]
    public void Rejects_a_header_whose_time_is_not_after_the_median_of_the_previous_eleven()
    {
        var chain = NewChain();
        var raw = (byte[])HeaderFixtures.At(CheckpointHeight + 1).Raw.Clone();
        // nTime sits at offset 68; set it to a value well before the recent blocks.
        BitConverter.GetBytes(1_000_000_000u).CopyTo(raw, 68);

        var result = chain.TryAppend(BlockHeader80.Parse(raw));

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.TimeNotAfterMedian, result.Reason);
    }

    [Fact]
    public void A_header_that_arrives_after_a_rejected_one_is_judged_against_the_unchanged_tip()
    {
        var chain = NewChain();
        chain.TryAppend(HeaderFixtures.Header(CheckpointHeight + 5));

        var result = chain.TryAppend(HeaderFixtures.Header(CheckpointHeight + 1));

        Assert.True(result.Accepted);
        Assert.Equal(CheckpointHeight + 1, chain.TipHeight);
    }

    [Fact]
    public void With_every_hasher_each_real_header_after_the_checkpoint_has_its_proof_of_work_checked()
    {
        var chain = NewChain(DigiByte.Crypto.Headers.Hashers.PowHashers.Available);
        var seen = new HashSet<PowAlgorithm>();

        foreach (var header in After(CheckpointHeight + 1, int.MaxValue))
        {
            var result = chain.TryAppend(header);
            Assert.True(result.Accepted, $"height {chain.TipHeight + 1}: {result.Reason}");
            Assert.True(result.ProofOfWorkChecked, $"height {chain.TipHeight}: {header.Algorithm}");
            seen.Add(header.Algorithm);
        }

        Assert.Equal(5, seen.Count);
        Assert.Equal(0, chain.UncheckedProofOfWorkCount);
    }

    [Fact]
    public void A_header_with_a_changed_nonce_fails_the_proof_of_work_check()
    {
        var chain = NewChain(DigiByte.Crypto.Headers.Hashers.PowHashers.Available);
        var raw = (byte[])HeaderFixtures.At(CheckpointHeight + 1).Raw.Clone();
        raw[76] ^= 0xFF;

        var result = chain.TryAppend(BlockHeader80.Parse(raw));

        Assert.False(result.Accepted);
        Assert.Equal(HeaderRejection.ProofOfWorkFailed, result.Reason);
        Assert.Equal(CheckpointHeight, chain.TipHeight);
    }

    [Fact]
    public void Without_a_proof_of_work_hasher_the_result_says_proof_of_work_was_not_checked()
    {
        var chain = NewChain();

        var result = chain.TryAppend(HeaderFixtures.Header(CheckpointHeight + 1));

        Assert.True(result.Accepted);
        Assert.False(result.ProofOfWorkChecked);
    }

    [Fact]
    public void Compact_targets_convert_both_ways()
    {
        // 0x1e0fffff is a common compact form; the round trip must be exact for canonical values.
        foreach (var bits in new uint[] { 0x1e0fffff, 0x1d00ffff, 0x1b0404cb, 436283138u })
            Assert.Equal(bits, CompactTarget.ToCompact(CompactTarget.ToBigInteger(bits)));

        Assert.Equal(System.Numerics.BigInteger.Zero, CompactTarget.ToBigInteger(0));
    }
}
