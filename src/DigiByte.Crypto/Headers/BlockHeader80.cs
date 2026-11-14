using System.Buffers.Binary;
using NBitcoin;
using NBitcoin.Crypto;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// An 80-byte DigiByte block header: version, previous block hash, merkle root, time, bits and nonce.
/// Immutable. The block hash is always the double SHA-256 of these 80 bytes, whatever algorithm mined the block;
/// the algorithm's own proof-of-work hash is a separate value (see <see cref="IPowHasher"/>).
/// </summary>
public sealed class BlockHeader80
{
    public const int Size = 80;

    private readonly byte[] _raw;
    private uint256? _hash;

    private BlockHeader80(byte[] raw)
    {
        _raw = raw;
        Version = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0, 4));
        PrevHash = new uint256(raw.AsSpan(4, 32).ToArray());
        MerkleRoot = new uint256(raw.AsSpan(36, 32).ToArray());
        Time = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(68, 4));
        Bits = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(72, 4));
        Nonce = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(76, 4));
        Algorithm = PowAlgorithms.TryFromVersion(Version, out var algo) ? algo : PowAlgorithm.Unknown;
    }

    public int Version { get; }
    public uint256 PrevHash { get; }
    public uint256 MerkleRoot { get; }
    public uint Time { get; }
    public uint Bits { get; }
    public uint Nonce { get; }

    /// <summary>The algorithm named by the version bits, or <see cref="PowAlgorithm.Unknown"/> for a value DigiByte does not define.</summary>
    public PowAlgorithm Algorithm { get; }

    /// <summary>The block hash (what explorers call the block id).</summary>
    public uint256 Hash => _hash ??= Hashes.DoubleSHA256(_raw);

    public static BlockHeader80 Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
            throw new FormatException($"A block header is exactly {Size} bytes, got {bytes.Length}.");
        return new BlockHeader80(bytes.ToArray());
    }

    public byte[] ToBytes() => (byte[])_raw.Clone();

    internal ReadOnlySpan<byte> RawSpan => _raw;
}
