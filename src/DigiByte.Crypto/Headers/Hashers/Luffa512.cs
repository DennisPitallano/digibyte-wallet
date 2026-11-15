using System.Buffers.Binary;
using System.Numerics;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// Luffa-512, the first of the five hash functions in DigiByte's Qubit proof of work. A port of the reference implementation in
/// DigiByte Core's <c>crypto/luffa.cpp</c> (the non-parallel path): five 256-bit chains, message injection, then eight rounds of
/// a permutation per chain, with the chains tweaked by different rotations.
/// </summary>
internal static class Luffa512
{
    private const int Chains = 5;
    private const int Words = 8;

    private static readonly uint[][] InitialValues =
    [
        [0x6d251e69, 0x44b051e0, 0x4eaa6fb4, 0xdbf78465, 0x6e292011, 0x90152df4, 0xee058139, 0xdef610bb],
        [0xc3b44b95, 0xd9d2f256, 0x70eee9a0, 0xde099fa3, 0x5d9b0557, 0x8fc944b3, 0xcf1ccf0e, 0x746cd581],
        [0xf7efc89d, 0x5dba5781, 0x04016ce5, 0xad659c05, 0x0306194f, 0x666d1836, 0x24aa230a, 0x8b264ae7],
        [0x858075d5, 0x36d79cce, 0xe571f7d7, 0x204b1f67, 0x35870c6a, 0x57e9e923, 0x14bcb808, 0x7cde72ce],
        [0x6c68e9be, 0x5ec41e22, 0xc825b7c7, 0xaffb4363, 0xf5df3999, 0x0fc688f1, 0xb07224cc, 0x03e86cea],
    ];

    // Round constants per chain: the constant mixed into word 0 and the one mixed into word 4, for each of the 8 rounds.
    private static readonly uint[][] RoundConstantsLow =
    [
        [0x303994a6, 0xc0e65299, 0x6cc33a12, 0xdc56983e, 0x1e00108f, 0x7800423d, 0x8f5b7882, 0x96e1db12],
        [0xb6de10ed, 0x70f47aae, 0x0707a3d4, 0x1c1e8f51, 0x707a3d45, 0xaeb28562, 0xbaca1589, 0x40a46f3e],
        [0xfc20d9d2, 0x34552e25, 0x7ad8818f, 0x8438764a, 0xbb6de032, 0xedb780c8, 0xd9847356, 0xa2c78434],
        [0xb213afa5, 0xc84ebe95, 0x4e608a22, 0x56d858fe, 0x343b138f, 0xd0ec4e3d, 0x2ceb4882, 0xb3ad2208],
        [0xf0d2e9e3, 0xac11d7fa, 0x1bcb66f2, 0x6f2d9bc9, 0x78602649, 0x8edae952, 0x3b6ba548, 0xedae9520],
    ];

    private static readonly uint[][] RoundConstantsHigh =
    [
        [0xe0337818, 0x441ba90d, 0x7f34d442, 0x9389217f, 0xe5a8bce6, 0x5274baf4, 0x26889ba7, 0x9a226e9d],
        [0x01685f3d, 0x05a17cf4, 0xbd09caca, 0xf4272b28, 0x144ae5cc, 0xfaa7ae2b, 0x2e48f1c1, 0xb923c704],
        [0xe25e72c1, 0xe623bb72, 0x5c58a4a4, 0x1e38e2e7, 0x78e38b9d, 0x27586719, 0x36eda57f, 0x703aace7],
        [0xe028c9bf, 0x44756f91, 0x7e8fce32, 0x956548be, 0xfe191be2, 0x3cb226e5, 0x5944a28e, 0xa1c4c355],
        [0x5090d577, 0x2d1925ab, 0xb46496ac, 0xd1925ab0, 0x29131ab6, 0x0fc053c3, 0x3f014f0c, 0xfc053c31],
    ];

    /// <summary>The 64-byte Luffa-512 digest of the message.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        var v = new uint[Chains][];
        for (var i = 0; i < Chains; i++)
            v[i] = (uint[])InitialValues[i].Clone();

        var block = new byte[32];
        var offset = 0;
        while (message.Length - offset >= 32)
        {
            message.Slice(offset, 32).CopyTo(block);
            InjectAndPermute(v, block);
            offset += 32;
        }

        // Pad: the remaining bytes, a 0x80 byte, then zeros.
        Array.Clear(block);
        message[offset..].CopyTo(block);
        block[message.Length - offset] = 0x80;
        InjectAndPermute(v, block);

        var digest = new byte[64];
        Array.Clear(block);
        InjectAndPermute(v, block);
        WriteOutput(v, digest.AsSpan(0, 32));
        InjectAndPermute(v, block);
        WriteOutput(v, digest.AsSpan(32, 32));
        return digest;
    }

    private static void WriteOutput(uint[][] v, Span<byte> destination)
    {
        for (var j = 0; j < Words; j++)
        {
            var word = v[0][j] ^ v[1][j] ^ v[2][j] ^ v[3][j] ^ v[4][j];
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4 * j, 4), word);
        }
    }

    private static void InjectAndPermute(uint[][] v, byte[] block)
    {
        MessageInjection(v, block);
        Permute(v);
    }

    // Multiplication by 2 in the Luffa field: shift the eight words up and fold the top word back in.
    private static void Mul2(uint[] d, uint[] s)
    {
        var tmp = s[7];
        d[7] = s[6];
        d[6] = s[5];
        d[5] = s[4];
        d[4] = s[3] ^ tmp;
        d[3] = s[2] ^ tmp;
        d[2] = s[1];
        d[1] = s[0] ^ tmp;
        d[0] = tmp;
    }

    private static void Xor(uint[] d, uint[] s1, uint[] s2)
    {
        for (var i = 0; i < Words; i++)
            d[i] = s1[i] ^ s2[i];
    }

    private static void MessageInjection(uint[][] v, byte[] block)
    {
        var m = new uint[Words];
        for (var i = 0; i < Words; i++)
            m[i] = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(4 * i, 4));

        var a = new uint[Words];
        var b = new uint[Words];
        uint[] v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3], v4 = v[4];

        Xor(a, v0, v1);
        Xor(b, v2, v3);
        Xor(a, a, b);
        Xor(a, a, v4);
        Mul2(a, a);
        Xor(v0, a, v0);
        Xor(v1, a, v1);
        Xor(v2, a, v2);
        Xor(v3, a, v3);
        Xor(v4, a, v4);

        Mul2(b, v0);
        Xor(b, b, v1);
        Mul2(v1, v1);
        Xor(v1, v1, v2);
        Mul2(v2, v2);
        Xor(v2, v2, v3);
        Mul2(v3, v3);
        Xor(v3, v3, v4);
        Mul2(v4, v4);
        Xor(v4, v4, v0);
        Mul2(v0, b);
        Xor(v0, v0, v4);
        Mul2(v4, v4);
        Xor(v4, v4, v3);
        Mul2(v3, v3);
        Xor(v3, v3, v2);
        Mul2(v2, v2);
        Xor(v2, v2, v1);
        Mul2(v1, v1);
        Xor(v1, v1, b);

        Xor(v0, v0, m);
        Mul2(m, m);
        Xor(v1, v1, m);
        Mul2(m, m);
        Xor(v2, v2, m);
        Mul2(m, m);
        Xor(v3, v3, m);
        Mul2(m, m);
        Xor(v4, v4, m);
    }

    private static void Permute(uint[][] v)
    {
        // Tweak: the upper four words of chains 1 to 4 are rotated by 1 to 4 bits.
        for (var c = 1; c < Chains; c++)
            for (var j = 4; j < Words; j++)
                v[c][j] = BitOperations.RotateLeft(v[c][j], c);

        for (var c = 0; c < Chains; c++)
        {
            var w = v[c];
            for (var r = 0; r < 8; r++)
            {
                SubCrumb(ref w[0], ref w[1], ref w[2], ref w[3]);
                SubCrumb(ref w[5], ref w[6], ref w[7], ref w[4]);
                MixWord(ref w[0], ref w[4]);
                MixWord(ref w[1], ref w[5]);
                MixWord(ref w[2], ref w[6]);
                MixWord(ref w[3], ref w[7]);
                w[0] ^= RoundConstantsLow[c][r];
                w[4] ^= RoundConstantsHigh[c][r];
            }
        }
    }

    private static void SubCrumb(ref uint a0, ref uint a1, ref uint a2, ref uint a3)
    {
        var tmp = a0;
        a0 |= a1;
        a2 ^= a3;
        a1 = ~a1;
        a0 ^= a3;
        a3 &= tmp;
        a1 ^= a3;
        a3 ^= a2;
        a2 &= a0;
        a0 = ~a0;
        a2 ^= a1;
        a1 |= a3;
        tmp ^= a1;
        a3 ^= a2;
        a2 &= a1;
        a1 ^= a0;
        a0 = tmp;
    }

    private static void MixWord(ref uint u, ref uint v)
    {
        v ^= u;
        u = BitOperations.RotateLeft(u, 2) ^ v;
        v = BitOperations.RotateLeft(v, 14) ^ u;
        u = BitOperations.RotateLeft(u, 10) ^ v;
        v = BitOperations.RotateLeft(v, 1);
    }
}
