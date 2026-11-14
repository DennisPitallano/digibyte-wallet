using System.Buffers.Binary;
using System.Numerics;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// The Keccak-p[800, 12] permutation: Keccak-f[800] with only its last 12 of 22 rounds, as <c>KeccakP800_Permute_12rounds</c>
/// from the eXtended Keccak Code Package, which Odocrypt uses to finish its hash. The state is 25 lanes of 32 bits, 100 bytes.
/// </summary>
internal static class KeccakP800
{
    public const int StateBytes = 100;

    // Keccak round constants truncated to 32 bits; Keccak-f[800] uses the first 22, and the 12-round version the last 12 of those.
    private static readonly uint[] RoundConstants =
    [
        0x00000001, 0x00008082, 0x0000808A, 0x80008000, 0x0000808B, 0x80000001, 0x80008081, 0x00008009,
        0x0000008A, 0x00000088, 0x80008009, 0x8000000A, 0x8000808B, 0x0000008B, 0x00008089, 0x00008003,
        0x00008002, 0x00000080, 0x0000800A, 0x8000000A, 0x80008081, 0x00008080,
    ];

    // Rotation offsets for lane (x, y) at index x + 5 * y, reduced modulo the 32-bit lane width.
    private static readonly int[] RotationOffsets =
    [
        0, 1, 62, 28, 27,
        36, 44, 6, 55, 20,
        3, 10, 43, 25, 39,
        41, 45, 15, 21, 8,
        18, 2, 61, 56, 14,
    ];

    public static void Permute12Rounds(Span<byte> state)
    {
        Span<uint> lanes = stackalloc uint[25];
        for (var i = 0; i < 25; i++)
            lanes[i] = BinaryPrimitives.ReadUInt32LittleEndian(state.Slice(4 * i, 4));

        for (var round = 10; round < 22; round++)
            Round(lanes, RoundConstants[round]);

        for (var i = 0; i < 25; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(state.Slice(4 * i, 4), lanes[i]);
    }

    private static void Round(Span<uint> a, uint roundConstant)
    {
        Span<uint> c = stackalloc uint[5];
        Span<uint> d = stackalloc uint[5];
        Span<uint> b = stackalloc uint[25];

        // Theta
        for (var x = 0; x < 5; x++)
            c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
        for (var x = 0; x < 5; x++)
            d[x] = c[(x + 4) % 5] ^ BitOperations.RotateLeft(c[(x + 1) % 5], 1);
        for (var i = 0; i < 25; i++)
            a[i] ^= d[i % 5];

        // Rho and Pi
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                var rotation = RotationOffsets[x + 5 * y] % 32;
                b[y + 5 * ((2 * x + 3 * y) % 5)] = BitOperations.RotateLeft(a[x + 5 * y], rotation);
            }
        }

        // Chi
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
                a[x + 5 * y] = b[x + 5 * y] ^ (~b[(x + 1) % 5 + 5 * y] & b[(x + 2) % 5 + 5 * y]);
        }

        // Iota
        a[0] ^= roundConstant;
    }
}
