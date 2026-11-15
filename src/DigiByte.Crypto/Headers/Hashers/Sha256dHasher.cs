using NBitcoin;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>SHA256D blocks: the proof-of-work hash is the block hash itself (double SHA-256 of the header).</summary>
public sealed class Sha256dHasher : IPowHasher
{
    public bool Supports(PowAlgorithm algorithm) => algorithm == PowAlgorithm.Sha256d;

    public uint256 Hash(BlockHeader80 header)
    {
        if (!Supports(header.Algorithm))
            throw new NotSupportedException($"{nameof(Sha256dHasher)} does not hash {header.Algorithm} blocks.");
        return header.Hash;
    }
}
