using System.Numerics;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// The parts of DigiByte's consensus rules that header checking needs, copied from DigiByte Core's chain parameters
/// (<c>kernel/chainparams.cpp</c> and <c>pow.cpp</c>, develop branch). Only the current difficulty era (V4) is covered, because the
/// built-in checkpoint is after every earlier era change.
/// </summary>
public sealed record DigiByteConsensus
{
    /// <summary>Blocks per algorithm that the V4 retarget averages over (<c>nAveragingInterval</c>).</summary>
    public int AveragingInterval { get; init; } = 10;

    /// <summary>The target time for the averaging window in seconds: <c>nAveragingInterval * NUM_ALGOS * 15</c>.</summary>
    public long AveragingTargetTimespanV4 { get; init; } = 10 * 5 * 15;

    /// <summary>Smallest allowed timespan after damping: the target less 8%.</summary>
    public long MinActualTimespanV4 { get; init; } = 750 * (100 - 8) / 100;

    /// <summary>Largest allowed timespan after damping: the target plus 16%.</summary>
    public long MaxActualTimespanV4 { get; init; } = 750 * (100 + 16) / 100;

    /// <summary>Per-algorithm adjustment in percent (<c>nLocalTargetAdjustment</c>).</summary>
    public int LocalTargetAdjustment { get; init; } = 4;

    /// <summary>The easiest target any block may claim: all ones shifted right by 20 bits.</summary>
    public BigInteger PowLimit { get; init; } = (BigInteger.One << 236) - 1;

    /// <summary>Height at which the V4 retarget starts (<c>workComputationChangeTarget</c>).</summary>
    public int WorkComputationChangeTarget { get; init; } = 1_430_000;

    /// <summary>Height at which Odocrypt mining starts (<c>OdoHeight</c>).</summary>
    public int OdoHeight { get; init; } = 9_112_320;

    /// <summary>Height from which Groestl blocks are rejected (<c>nGroestlDeactivationHeight</c>).</summary>
    public int GroestlDeactivationHeight { get; init; } = 23_808_000;

    /// <summary>Number of previous block times in the median (<c>nMedianTimeSpan</c>).</summary>
    public int MedianTimeSpan { get; init; } = 11;

    /// <summary>Number of mining algorithms (<c>NUM_ALGOS</c>).</summary>
    public int NumAlgos { get; init; } = PowAlgorithms.ActiveCount;

    public static DigiByteConsensus Mainnet { get; } = new();

    /// <summary>The number of headers before the tip that the retarget needs: 50 back, plus 10 more for the median at that point.</summary>
    public int RequiredWindow => NumAlgos * AveragingInterval + MedianTimeSpan;

    /// <summary>True when an algorithm may mine a block at this height.</summary>
    public bool IsAlgorithmAllowed(PowAlgorithm algorithm, int height) => algorithm switch
    {
        PowAlgorithm.Sha256d or PowAlgorithm.Scrypt or PowAlgorithm.Skein or PowAlgorithm.Qubit => true,
        PowAlgorithm.Odo => height >= OdoHeight,
        PowAlgorithm.Groestl => height < GroestlDeactivationHeight,
        _ => false,
    };
}
