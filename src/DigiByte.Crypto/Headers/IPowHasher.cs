using NBitcoin;

namespace DigiByte.Crypto.Headers;

/// <summary>Computes an algorithm's proof-of-work hash of a block header, the value that must be at or below the header's target.</summary>
public interface IPowHasher
{
    /// <summary>True when this hasher can compute the hash for the algorithm.</summary>
    bool Supports(PowAlgorithm algorithm);

    /// <summary>The proof-of-work hash. Throws <see cref="NotSupportedException"/> for an algorithm it does not support.</summary>
    uint256 Hash(BlockHeader80 header);
}
