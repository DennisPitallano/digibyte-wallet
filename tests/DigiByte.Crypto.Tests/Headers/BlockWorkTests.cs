using System.Numerics;
using DigiByte.Crypto.Headers;

namespace DigiByte.Crypto.Tests.Headers;

public class BlockWorkTests
{
    [Theory]
    [InlineData(32, 2)]
    [InlineData(243, 3)]
    [InlineData(3125, 5)]
    [InlineData(10_000_000_000, 100)]
    [InlineData(1, 1)]
    public void The_fifth_root_of_a_perfect_power_is_exact(long value, long root)
    {
        Assert.Equal(new BigInteger(root), BlockWork.ApproxNthRoot(value, 5));
    }

    [Fact]
    public void The_root_of_a_large_value_is_close_to_the_true_root_and_shifted_back_up()
    {
        // 2^200 has the fifth root 2^40.
        var value = BigInteger.One << 200;
        var root = BlockWork.ApproxNthRoot(value, 5);

        Assert.Equal(BigInteger.One << 40, root);
    }

    [Fact]
    public void The_root_never_exceeds_the_true_root()
    {
        var value = (BigInteger.One << 235) + 123456789;
        var root = BlockWork.ApproxNthRoot(value, 5);

        Assert.True(BigInteger.Pow(root, 5) <= value);
    }

    [Fact]
    public void Every_block_after_the_checkpoint_adds_positive_work_and_the_total_only_grows()
    {
        var chain = new HeaderChain(Checkpoints.Mainnet, DigiByteConsensus.Mainnet);
        var previous = BigInteger.Zero;

        foreach (var row in HeaderFixtures.Run.Where(r => r.Height > Checkpoints.Mainnet.Height))
        {
            Assert.True(chain.TryAppend(BlockHeader80.Parse(row.Raw)).Accepted);
            Assert.True(chain.WorkSinceCheckpoint > previous, $"height {row.Height}");
            previous = chain.WorkSinceCheckpoint;
        }
    }

    [Fact]
    public void The_work_of_one_block_is_in_the_range_the_difficulty_implies()
    {
        // At about difficulty 14.5 million the work per block is near 2^32 * difficulty * 128 per algorithm-average.
        var headers = Checkpoints.Mainnet.Headers;
        Assert.True(BlockWork.TryProofOfNextBlock(headers, Checkpoints.Mainnet.FirstHeight, DigiByteConsensus.Mainnet, out var work));

        Assert.True(work > BigInteger.Pow(2, 40));
        Assert.True(work < BigInteger.Pow(2, 80));
    }

    [Fact]
    public void Headers_that_do_not_reach_back_far_enough_give_no_work_value()
    {
        var headers = Checkpoints.Mainnet.Headers.Take(20).ToList();

        Assert.False(BlockWork.TryProofOfNextBlock(headers, Checkpoints.Mainnet.FirstHeight, DigiByteConsensus.Mainnet, out _));
    }
}
