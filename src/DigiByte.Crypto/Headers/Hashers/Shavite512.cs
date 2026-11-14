using System.Buffers.Binary;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// SHAvite-3-512 (the specification variant, with little-endian AES columns) as used in DigiByte Core's <c>crypto/shavite.cpp</c>:
/// a Davies-Meyer style compression of 128-byte blocks with an AES-based key schedule that mixes in a 128-bit bit counter.
/// This follows the compact form of the reference code (the small-footprint variant), which gives the same results as the unrolled one.
/// </summary>
internal static class Shavite512
{
    private static readonly uint[] InitialValue =
    [
        0x72FCCDD8, 0x79CA4727, 0x128A077B, 0x40D55AEC, 0xD1901A06, 0x430AE307, 0xB29F5CD1, 0xDF07FBFC,
        0x8E45D73D, 0x681AB538, 0xBDE86578, 0xDD577E47, 0xE275EADE, 0x502D9FCD, 0xB9357178, 0x022A4B9A,
    ];

    private sealed class State
    {
        public readonly uint[] H = (uint[])InitialValue.Clone();
        public uint Count0, Count1, Count2, Count3;
    }

    /// <summary>The 64-byte SHAvite-3-512 digest of the message.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        var sc = new State();
        var buf = new byte[128];
        var ptr = 0;
        var offset = 0;

        // Core: full 128-byte blocks, adding 1024 to the bit counter each time.
        while (message.Length - offset >= 128 && ptr == 0)
        {
            message.Slice(offset, 128).CopyTo(buf);
            offset += 128;
            AddBits(sc, 1024);
            Compress(sc, buf);
        }
        message[offset..].CopyTo(buf);
        ptr = message.Length - offset;

        // Close.
        sc.Count0 += (uint)(ptr << 3);
        var count0 = sc.Count0;
        var count1 = sc.Count1;
        var count2 = sc.Count2;
        var count3 = sc.Count3;

        if (ptr == 0)
        {
            Array.Clear(buf);
            buf[0] = 0x80;
            sc.Count0 = sc.Count1 = sc.Count2 = sc.Count3 = 0;
        }
        else if (ptr < 110)
        {
            buf[ptr++] = 0x80;
            Array.Clear(buf, ptr, 110 - ptr);
        }
        else
        {
            buf[ptr++] = 0x80;
            Array.Clear(buf, ptr, 128 - ptr);
            Compress(sc, buf);
            Array.Clear(buf, 0, 110);
            sc.Count0 = sc.Count1 = sc.Count2 = sc.Count3 = 0;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(110), count0);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(114), count1);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(118), count2);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(122), count3);
        buf[126] = 0;      // output size in bits (512) as two little-endian bytes
        buf[127] = 2;
        Compress(sc, buf);

        var digest = new byte[64];
        for (var i = 0; i < 16; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(4 * i, 4), sc.H[i]);
        return digest;
    }

    private static void AddBits(State sc, uint bits)
    {
        sc.Count0 += bits;
        if (sc.Count0 == 0)
        {
            sc.Count1++;
            if (sc.Count1 == 0)
            {
                sc.Count2++;
                if (sc.Count2 == 0)
                    sc.Count3++;
            }
        }
    }

    private static void Compress(State sc, byte[] msg)
    {
        var rk = new uint[448];
        for (var i = 0; i < 32; i++)
            rk[i] = BinaryPrimitives.ReadUInt32LittleEndian(msg.AsSpan(4 * i, 4));

        var u = 32;
        for (; ; )
        {
            for (var s = 0; s < 4; s++)
            {
                ExpandStep(rk, u);
                if (u == 32)
                {
                    rk[32] ^= sc.Count0;
                    rk[33] ^= sc.Count1;
                    rk[34] ^= sc.Count2;
                    rk[35] ^= ~sc.Count3;
                }
                else if (u == 440)
                {
                    rk[440] ^= sc.Count1;
                    rk[441] ^= sc.Count0;
                    rk[442] ^= sc.Count3;
                    rk[443] ^= ~sc.Count2;
                }
                u += 4;

                ExpandStep(rk, u);
                if (u == 164)
                {
                    rk[164] ^= sc.Count3;
                    rk[165] ^= sc.Count2;
                    rk[166] ^= sc.Count1;
                    rk[167] ^= ~sc.Count0;
                }
                else if (u == 316)
                {
                    rk[316] ^= sc.Count2;
                    rk[317] ^= sc.Count3;
                    rk[318] ^= sc.Count0;
                    rk[319] ^= ~sc.Count1;
                }
                u += 4;
            }
            if (u == 448)
                break;
            for (var s = 0; s < 8; s++)
            {
                rk[u + 0] = rk[u - 32] ^ rk[u - 7];
                rk[u + 1] = rk[u - 31] ^ rk[u - 6];
                rk[u + 2] = rk[u - 30] ^ rk[u - 5];
                rk[u + 3] = rk[u - 29] ^ rk[u - 4];
                u += 4;
            }
        }

        var p = (uint[])sc.H.Clone();
        u = 0;
        for (var r = 0; r < 14; r++)
        {
            Element(p, 0, 4, rk, ref u);
            Element(p, 8, 12, rk, ref u);

            // Rotate the four-word groups: lane k takes the value of the lane before it.
            for (var k = 0; k < 4; k++)
            {
                var t = p[12 + k];
                p[12 + k] = p[8 + k];
                p[8 + k] = p[4 + k];
                p[4 + k] = p[k];
                p[k] = t;
            }
        }
        for (var i = 0; i < 16; i++)
            sc.H[i] ^= p[i];
    }

    // One AES-based step of the key expansion: a keyless AES round on four earlier words, then XOR with the words four back.
    private static void ExpandStep(uint[] rk, int u)
    {
        var x0 = rk[u - 31];
        var x1 = rk[u - 30];
        var x2 = rk[u - 29];
        var x3 = rk[u - 32];
        AesRound.Round(ref x0, ref x1, ref x2, ref x3);
        rk[u + 0] = x0 ^ rk[u - 4];
        rk[u + 1] = x1 ^ rk[u - 3];
        rk[u + 2] = x2 ^ rk[u - 2];
        rk[u + 3] = x3 ^ rk[u - 1];
    }

    // The round function applied to a group of four words: four keyed AES rounds over the words at <paramref name="right"/>,
    // whose result is XORed into the words at <paramref name="left"/>.
    private static void Element(uint[] p, int left, int right, uint[] rk, ref int u)
    {
        var x0 = p[right] ^ rk[u++];
        var x1 = p[right + 1] ^ rk[u++];
        var x2 = p[right + 2] ^ rk[u++];
        var x3 = p[right + 3] ^ rk[u++];
        AesRound.Round(ref x0, ref x1, ref x2, ref x3);
        x0 ^= rk[u++]; x1 ^= rk[u++]; x2 ^= rk[u++]; x3 ^= rk[u++];
        AesRound.Round(ref x0, ref x1, ref x2, ref x3);
        x0 ^= rk[u++]; x1 ^= rk[u++]; x2 ^= rk[u++]; x3 ^= rk[u++];
        AesRound.Round(ref x0, ref x1, ref x2, ref x3);
        x0 ^= rk[u++]; x1 ^= rk[u++]; x2 ^= rk[u++]; x3 ^= rk[u++];
        AesRound.Round(ref x0, ref x1, ref x2, ref x3);
        p[left] ^= x0;
        p[left + 1] ^= x1;
        p[left + 2] ^= x2;
        p[left + 3] ^= x3;
    }
}
