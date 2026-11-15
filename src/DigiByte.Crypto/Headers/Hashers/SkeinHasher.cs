using System.Security.Cryptography;
using NBitcoin;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// Skein blocks: Skein-512 (512-bit output) of the 80-byte header, then a single SHA-256 of that 64-byte result
/// (<c>HashSkein</c> in DigiByte Core's <c>crypto/hashskein.h</c>).
/// </summary>
public sealed class SkeinHasher : IPowHasher
{
    public bool Supports(PowAlgorithm algorithm) => algorithm == PowAlgorithm.Skein;

    public uint256 Hash(BlockHeader80 header)
    {
        if (!Supports(header.Algorithm))
            throw new NotSupportedException($"{nameof(SkeinHasher)} does not hash {header.Algorithm} blocks.");
        var skein = Skein512.Hash(header.ToBytes());
        return new uint256(SHA256.HashData(skein));
    }

    /// <summary>The raw Skein-512-512 digest, exposed for known-answer tests.</summary>
    internal static byte[] Skein512Digest(ReadOnlySpan<byte> message) => Skein512.Hash(message);
}
