using NBitcoin;
using NBitcoin.Crypto;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// Scrypt blocks: <c>scrypt_1024_1_1_256</c> over the 80-byte header (N=1024, r=1, p=1, 32-byte output), with the header as both the
/// password and the salt, as in DigiByte Core and Litecoin.
/// </summary>
public sealed class ScryptHasher : IPowHasher
{
    public bool Supports(PowAlgorithm algorithm) => algorithm == PowAlgorithm.Scrypt;

    public uint256 Hash(BlockHeader80 header)
    {
        if (!Supports(header.Algorithm))
            throw new NotSupportedException($"{nameof(ScryptHasher)} does not hash {header.Algorithm} blocks.");
        var raw = header.ToBytes();
        var derived = SCrypt.ComputeDerivedKey(raw, raw, 1024, 1, 1, null, 32);
        return new uint256(derived);
    }
}
