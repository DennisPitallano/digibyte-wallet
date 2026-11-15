using NBitcoin;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// Qubit blocks (<c>HashQubit</c> in DigiByte Core's <c>crypto/hashqubit.h</c>): the 80-byte header goes through Luffa-512,
/// CubeHash-512, SHAvite-3-512, SIMD-512 and ECHO-512 in turn, each taking the 64-byte result of the one before, and the
/// first 32 bytes of the last result are the hash.
/// </summary>
public sealed class QubitHasher : IPowHasher
{
    public bool Supports(PowAlgorithm algorithm) => algorithm == PowAlgorithm.Qubit;

    public uint256 Hash(BlockHeader80 header)
    {
        if (!Supports(header.Algorithm))
            throw new NotSupportedException($"{nameof(QubitHasher)} does not hash {header.Algorithm} blocks.");
        return new uint256(Chain(header.ToBytes())[4].AsSpan(0, 32).ToArray());
    }

    /// <summary>The five intermediate 64-byte results of the chain, for tests.</summary>
    internal static byte[][] Chain(ReadOnlySpan<byte> input)
    {
        var luffa = Luffa512.Hash(input);
        var cube = CubeHash512.Hash(luffa);
        var shavite = Shavite512.Hash(cube);
        var simd = Simd512.Hash(shavite);
        var echo = Echo512.Hash(simd);
        return [luffa, cube, shavite, simd, echo];
    }
}
