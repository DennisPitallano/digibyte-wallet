using System.Numerics;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// DigiByte's V4 difficulty rule (<c>GetNextWorkRequiredV4</c> in DigiByte Core's <c>pow.cpp</c>), used on every block from height
/// 1,430,000. Each of the five algorithms has its own difficulty. It is a port, not a design: the steps and the order of the
/// integer arithmetic follow the C++ so the result is bit for bit the same.
/// </summary>
public static class DigiByteRetarget
{
    /// <summary>
    /// The bits the next block must carry when it uses <paramref name="algorithm"/>, given headers that end at the current tip.
    /// Returns false when the headers do not reach back far enough: 50 blocks for the averaging window, 10 more for the median time,
    /// and the last block that used the same algorithm.
    /// </summary>
    /// <param name="headers">Consecutive headers, oldest first, ending at the tip.</param>
    /// <param name="firstHeight">Height of <c>headers[0]</c>.</param>
    public static bool TryNextRequiredBits(IReadOnlyList<BlockHeader80> headers, int firstHeight, PowAlgorithm algorithm,
        DigiByteConsensus consensus, out uint bits)
    {
        bits = 0;
        var tipIndex = headers.Count - 1;
        var tipHeight = firstHeight + tipIndex;

        // pindexFirst: NUM_ALGOS * nAveragingInterval blocks before the tip.
        var firstIndex = tipIndex - consensus.NumAlgos * consensus.AveragingInterval;
        if (firstIndex - (consensus.MedianTimeSpan - 1) < 0)
            return false;

        // pindexPrevAlgo: the most recent block that used the same algorithm.
        var prevAlgoIndex = -1;
        for (var i = tipIndex; i >= 0; i--)
        {
            if (headers[i].Algorithm == algorithm)
            {
                prevAlgoIndex = i;
                break;
            }
        }
        if (prevAlgoIndex < 0)
            return false;

        // Median times past of the tip and of pindexFirst, which damp time-warp attacks.
        var actual = MedianTimePast(headers, tipIndex, consensus.MedianTimeSpan) - MedianTimePast(headers, firstIndex, consensus.MedianTimeSpan);

        // Damp the measured timespan, then clamp it. Integer division truncates toward zero, as in C++.
        var target = consensus.AveragingTargetTimespanV4;
        actual = target + (actual - target) / 4;
        if (actual < consensus.MinActualTimespanV4)
            actual = consensus.MinActualTimespanV4;
        if (actual > consensus.MaxActualTimespanV4)
            actual = consensus.MaxActualTimespanV4;

        // Global retarget from the previous block of the same algorithm.
        var bnNew = CompactTarget.ToBigInteger(headers[prevAlgoIndex].Bits);
        bnNew *= actual;
        bnNew /= target;

        // Per-algorithm retarget: algorithms that have not mined recently get easier, ones that mined just now get harder.
        var prevAlgoHeight = firstHeight + prevAlgoIndex;
        var adjustments = prevAlgoHeight + consensus.NumAlgos - 1 - tipHeight;
        if (adjustments > 0)
        {
            for (var i = 0; i < adjustments; i++)
            {
                bnNew *= 100;
                bnNew /= 100 + consensus.LocalTargetAdjustment;
            }
        }
        else if (adjustments < 0)
        {
            for (var i = 0; i < -adjustments; i++)
            {
                bnNew *= 100 + consensus.LocalTargetAdjustment;
                bnNew /= 100;
                if (bnNew > consensus.PowLimit)
                {
                    bnNew = consensus.PowLimit;
                    break;
                }
            }
        }

        if (bnNew > consensus.PowLimit)
            bnNew = consensus.PowLimit;

        bits = CompactTarget.ToCompact(bnNew);
        return true;
    }

    /// <summary>The median of the block times of the header at <paramref name="index"/> and the <c>span - 1</c> before it.</summary>
    internal static long MedianTimePast(IReadOnlyList<BlockHeader80> headers, int index, int span)
    {
        var times = new long[span];
        for (var i = 0; i < span; i++)
            times[i] = headers[index - i].Time;
        Array.Sort(times);
        return times[span / 2];
    }
}
