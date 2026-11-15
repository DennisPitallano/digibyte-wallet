using System.Buffers.Binary;
using System.Numerics;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// Skein-512 with a 512-bit output (Skein 1.3), the first stage of DigiByte's Skein proof of work.
/// Skein turns the Threefish-512 block cipher into a hash with Unique Block Iteration (UBI): a configuration block, the message,
/// then an output block, each chained through the cipher.
/// </summary>
internal static class Skein512
{
    private const ulong C240 = 0x1BD11BDAA9FC1A22UL;

    private const ulong TypeConfig = 4UL;
    private const ulong TypeMessage = 48UL;
    private const ulong TypeOutput = 63UL;
    private const ulong FirstFlag = 1UL << 62;
    private const ulong FinalFlag = 1UL << 63;

    // Threefish-512 rotation constants: one row per round modulo 8, one column per word pair.
    private static readonly int[][] Rotation =
    [
        [46, 36, 19, 37],
        [33, 27, 14, 42],
        [17, 49, 36, 39],
        [44, 9, 54, 56],
        [39, 30, 34, 24],
        [13, 50, 10, 17],
        [25, 29, 39, 43],
        [8, 35, 56, 22],
    ];

    private static readonly int[] Permutation = [2, 1, 4, 7, 6, 5, 0, 3];

    /// <summary>The 64-byte Skein-512-512 digest of the message.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        // Configuration block: schema "SHA3", version 1, output length 512 bits, no tree parameters.
        var config = new byte[32];
        "SHA3"u8.CopyTo(config);
        BinaryPrimitives.WriteUInt16LittleEndian(config.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(config.AsSpan(8), 512);

        var chain = new ulong[8];
        chain = Ubi(chain, config, TypeConfig);
        chain = Ubi(chain, message, TypeMessage);
        chain = Ubi(chain, new byte[8], TypeOutput);

        var digest = new byte[64];
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(digest.AsSpan(i * 8), chain[i]);
        return digest;
    }

    private static ulong[] Ubi(ulong[] chain, ReadOnlySpan<byte> message, ulong type)
    {
        var blocks = Math.Max(1, (message.Length + 63) / 64);
        var block = new ulong[8];
        var padded = new byte[64];
        for (var b = 0; b < blocks; b++)
        {
            var start = b * 64;
            var count = Math.Min(64, message.Length - start);
            padded.AsSpan().Clear();
            if (count > 0)
                message.Slice(start, count).CopyTo(padded);
            for (var i = 0; i < 8; i++)
                block[i] = BinaryPrimitives.ReadUInt64LittleEndian(padded.AsSpan(i * 8));

            var position = (ulong)(start + count);
            var flags = (b == 0 ? FirstFlag : 0UL) | (b == blocks - 1 ? FinalFlag : 0UL);
            var encrypted = Threefish(chain, position, (type << 56) | flags, block);
            var next = new ulong[8];
            for (var i = 0; i < 8; i++)
                next[i] = encrypted[i] ^ block[i];
            chain = next;
        }
        return chain;
    }

    private static ulong[] Threefish(ulong[] key, ulong tweak0, ulong tweak1, ulong[] plain)
    {
        var k = new ulong[9];
        ulong parity = C240;
        for (var i = 0; i < 8; i++)
        {
            k[i] = key[i];
            parity ^= key[i];
        }
        k[8] = parity;
        ulong[] t = [tweak0, tweak1, tweak0 ^ tweak1];

        var v = (ulong[])plain.Clone();
        var mixed = new ulong[8];
        for (var round = 0; round < 72; round++)
        {
            if (round % 4 == 0)
                AddSubkey(v, k, t, round / 4);

            var rot = Rotation[round % 8];
            for (var j = 0; j < 4; j++)
            {
                var a = v[2 * j] + v[2 * j + 1];
                mixed[2 * j] = a;
                mixed[2 * j + 1] = BitOperations.RotateLeft(v[2 * j + 1], rot[j]) ^ a;
            }
            for (var i = 0; i < 8; i++)
                v[i] = mixed[Permutation[i]];
        }
        AddSubkey(v, k, t, 18);
        return v;
    }

    private static void AddSubkey(ulong[] v, ulong[] k, ulong[] t, int s)
    {
        for (var i = 0; i < 8; i++)
            v[i] += k[(s + i) % 9];
        v[5] += t[s % 3];
        v[6] += t[(s + 1) % 3];
        v[7] += (ulong)s;
    }
}
