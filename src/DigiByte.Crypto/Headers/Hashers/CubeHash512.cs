using System.Buffers.Binary;
using System.Numerics;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// CubeHash-512 as built into DigiByte Core (<c>crypto/cubehash.cpp</c>): CubeHash with 16 rounds per 32-byte block and 10 times as
/// many rounds to finish, started from the precomputed 512-bit initial state. The state is 32 words; every round is the ten steps from
/// the CubeHash specification, written out here without the register renaming the reference uses.
/// </summary>
internal static class CubeHash512
{
    private static readonly uint[] InitialState =
    [
        0x2AEA2A61, 0x50F494D4, 0x2D538B8B, 0x4167D83E, 0x3FEE2313, 0xC701CF8C, 0xCC39968E, 0x50AC5695,
        0x4D42C787, 0xA647A8B3, 0x97CF0BEF, 0x825B4537, 0xEEF864D2, 0xF22090C4, 0xD0E5CD33, 0xA23911AE,
        0xFCD398D9, 0x148FE485, 0x1B017BEF, 0xB6444532, 0x6A536159, 0x2FF5781C, 0x91FA7934, 0x0DBADEA9,
        0xD65C8A2B, 0xA5A70E75, 0xB1C62456, 0xBC796576, 0x1921C8F7, 0xE7989AF1, 0x7795D246, 0xD43E3B44,
    ];

    /// <summary>The 64-byte CubeHash-512 digest of the message.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        var x = (uint[])InitialState.Clone();

        var offset = 0;
        while (message.Length - offset >= 32)
        {
            XorBlock(x, message.Slice(offset, 32));
            SixteenRounds(x);
            offset += 32;
        }

        // Final block: the remaining bytes, then a 1 bit (0x80), zero padded.
        Span<byte> last = stackalloc byte[32];
        last.Clear();
        message[offset..].CopyTo(last);
        last[message.Length - offset] = 0x80;
        XorBlock(x, last);

        for (var i = 0; i < 11; i++)
        {
            SixteenRounds(x);
            if (i == 0)
                x[31] ^= 1;
        }

        var digest = new byte[64];
        for (var i = 0; i < 16; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(4 * i, 4), x[i]);
        return digest;
    }

    private static void XorBlock(uint[] x, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < 8; i++)
            x[i] ^= BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(4 * i, 4));
    }

    private static void SixteenRounds(uint[] x)
    {
        for (var round = 0; round < 16; round++)
            Round(x);
    }

    private static void Round(uint[] x)
    {
        for (var i = 0; i < 16; i++) x[i + 16] += x[i];
        for (var i = 0; i < 16; i++) x[i] = BitOperations.RotateLeft(x[i], 7);
        for (var i = 0; i < 8; i++) (x[i], x[i + 8]) = (x[i + 8], x[i]);
        for (var i = 0; i < 16; i++) x[i] ^= x[i + 16];
        foreach (var i in new[] { 0, 1, 4, 5, 8, 9, 12, 13 }) (x[i + 16], x[i + 18]) = (x[i + 18], x[i + 16]);
        for (var i = 0; i < 16; i++) x[i + 16] += x[i];
        for (var i = 0; i < 16; i++) x[i] = BitOperations.RotateLeft(x[i], 11);
        foreach (var i in new[] { 0, 1, 2, 3, 8, 9, 10, 11 }) (x[i], x[i + 4]) = (x[i + 4], x[i]);
        for (var i = 0; i < 16; i++) x[i] ^= x[i + 16];
        for (var i = 0; i < 16; i += 2) (x[i + 16], x[i + 17]) = (x[i + 17], x[i + 16]);
    }
}
