namespace DigiByte.Crypto.Headers;

/// <summary>The proof-of-work algorithms a DigiByte block can use. Values match DigiByte Core's <c>ALGO_*</c> constants.</summary>
public enum PowAlgorithm
{
    Unknown = -1,
    Sha256d = 0,
    Scrypt = 1,
    /// <summary>Switched off at height 23,808,000; no block after the built-in checkpoint may use it.</summary>
    Groestl = 2,
    Skein = 3,
    Qubit = 4,
    Odo = 7,
}

/// <summary>
/// Reads the algorithm from a block's version field. DigiByte Core (<c>primitives/block.h</c>) uses the mask
/// <c>15 &lt;&lt; 8</c>: Scrypt 0, SHA256D 2, Groestl 4, Skein 6, Qubit 8 and Odocrypt 14, each shifted left by 8.
/// </summary>
public static class PowAlgorithms
{
    public const int VersionAlgoMask = 15 << 8;

    /// <summary>The number of algorithms that mine at the same time (<c>NUM_ALGOS</c>).</summary>
    public const int ActiveCount = 5;

    public static bool TryFromVersion(int version, out PowAlgorithm algorithm)
    {
        algorithm = (version & VersionAlgoMask) switch
        {
            0 << 8 => PowAlgorithm.Scrypt,
            2 << 8 => PowAlgorithm.Sha256d,
            4 << 8 => PowAlgorithm.Groestl,
            6 << 8 => PowAlgorithm.Skein,
            8 << 8 => PowAlgorithm.Qubit,
            14 << 8 => PowAlgorithm.Odo,
            _ => PowAlgorithm.Unknown,
        };
        return algorithm != PowAlgorithm.Unknown;
    }
}
