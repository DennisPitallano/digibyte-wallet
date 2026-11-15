using System.Numerics;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// The work a block adds to the chain, as DigiByte Core counts it after height 1,430,000 (<c>GetBlockProofImpl</c> in <c>chain.cpp</c>):
/// the geometric mean of the targets every active algorithm would need for the next block, turned into work and scaled up by 128.
/// Using the mean over all algorithms, not the block's own target, keeps one algorithm from being gamed to win a fork.
/// This is a port and is not yet checked against a node's <c>chainwork</c> values (see research.md).
/// </summary>
public static class BlockWork
{
    private static readonly BigInteger TwoTo256 = BigInteger.One << 256;
    private static readonly PowAlgorithm[] ActiveAlgorithms =
        [PowAlgorithm.Sha256d, PowAlgorithm.Scrypt, PowAlgorithm.Skein, PowAlgorithm.Qubit, PowAlgorithm.Odo];

    /// <summary>
    /// The work of the block that follows <paramref name="headers"/> (which end at its parent). Returns false when the headers do not
    /// reach back far enough to compute every algorithm's target, or a target is invalid.
    /// </summary>
    public static bool TryProofOfNextBlock(IReadOnlyList<BlockHeader80> headers, int firstHeight, DigiByteConsensus consensus, out BigInteger work)
    {
        work = BigInteger.Zero;
        var average = BigInteger.One;
        foreach (var algorithm in ActiveAlgorithms)
        {
            if (!DigiByteRetarget.TryNextRequiredBits(headers, firstHeight, algorithm, consensus, out var bits))
                return false;
            if (!CompactTarget.TryToTarget(bits, consensus.PowLimit, out var target))
                return false;
            average = (average * ApproxNthRoot(target, consensus.NumAlgos)) & (TwoTo256 - 1);
        }

        // (~x / (x + 1)) + 1 on 256 bits, which is 2^256 / (x + 1) rounded the way the reference does.
        var inverted = TwoTo256 - 1 - average;
        var proof = inverted / (average + 1) + 1;
        work = proof << 7;
        return true;
    }

    /// <summary>
    /// <c>arith_uint256::ApproxNthRoot</c>: the integer n-th root of the top 64 bits of the value, found by binary search,
    /// shifted back up. Exact for values that are perfect n-th powers, approximate otherwise, and the same on every node.
    /// </summary>
    public static BigInteger ApproxNthRoot(BigInteger value, int n)
    {
        if (n <= 1 || n >= 64)
            throw new ArgumentOutOfRangeException(nameof(n));
        var length = value.IsZero ? 0 : (int)value.GetBitLength();
        var shifts = Math.Max(0, (length - 1) / n + 1 - 64 / n);

        var shifted = value >> (n * shifts);
        var head = (ulong)(shifted & ulong.MaxValue);

        ulong root = 0;
        for (var i = 64 / n - 1; i >= 0; i--)
        {
            var next = root | (1UL << i);
            var power = next;
            for (var j = 1; j < n; j++)
                power = unchecked(power * next);
            if (power <= head)
                root = next;
        }
        return new BigInteger(root) << shifts;
    }
}
