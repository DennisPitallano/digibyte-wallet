using System.Buffers.Binary;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// ECHO-512 as in DigiByte Core's <c>crypto/echo.cpp</c>: a wide-pipe AES-based design. The state is sixteen 128-bit words (the eight
/// words of the chaining value and eight words of the 128-byte message block); ten rounds each apply two AES rounds to every word,
/// the first keyed by a 128-bit bit counter that grows after every word, then ECHO's own row shifts and column mixing.
/// </summary>
internal static class Echo512
{
    private const ulong HighBits = 0x8080808080808080UL;
    private const ulong LowBits = 0x7F7F7F7F7F7F7F7FUL;

    /// <summary>The 64-byte ECHO-512 digest of the message.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        // Chaining value: eight 128-bit words, each holding the output length in bits (512) in its low half.
        var v = new ulong[16];
        for (var i = 0; i < 8; i++)
            v[2 * i] = 512;

        uint c0 = 0, c1 = 0, c2 = 0, c3 = 0;
        var buf = new byte[128];
        var offset = 0;

        while (message.Length - offset >= 128)
        {
            message.Slice(offset, 128).CopyTo(buf);
            offset += 128;
            IncrementCounter(ref c0, ref c1, ref c2, ref c3, 1024);
            Compress(v, buf, c0, c1, c2, c3);
        }
        var ptr = message.Length - offset;
        message[offset..].CopyTo(buf);

        var elen = (uint)(ptr << 3);
        IncrementCounter(ref c0, ref c1, ref c2, ref c3, elen);
        var counterBytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes.AsSpan(0), c0);
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes.AsSpan(4), c1);
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes.AsSpan(8), c2);
        BinaryPrimitives.WriteUInt32LittleEndian(counterBytes.AsSpan(12), c3);
        // A block that holds no message bit, only the first padding bit, is compressed with a zero counter.
        if (elen == 0)
            c0 = c1 = c2 = c3 = 0;

        buf[ptr++] = 0x80;
        Array.Clear(buf, ptr, 128 - ptr);
        if (ptr > 128 - 18)
        {
            Compress(v, buf, c0, c1, c2, c3);
            c0 = c1 = c2 = c3 = 0;
            Array.Clear(buf);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(128 - 18), 512);
        counterBytes.CopyTo(buf, 128 - 16);
        Compress(v, buf, c0, c1, c2, c3);

        var digest = new byte[64];
        for (var k = 0; k < 8; k++)
            BinaryPrimitives.WriteUInt64LittleEndian(digest.AsSpan(8 * k, 8), v[k]);
        return digest;
    }

    private static void IncrementCounter(ref uint c0, ref uint c1, ref uint c2, ref uint c3, uint value)
    {
        c0 += value;
        if (c0 < value)
        {
            if (++c1 == 0 && ++c2 == 0)
                c3++;
        }
    }

    private static void Compress(ulong[] v, byte[] buf, uint k0, uint k1, uint k2, uint k3)
    {
        // w holds sixteen 128-bit words as 32 64-bit halves: word n is w[2n] (low) and w[2n + 1] (high).
        var w = new ulong[32];
        Array.Copy(v, w, 16);
        for (var u = 0; u < 8; u++)
        {
            w[2 * (u + 8)] = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(16 * u, 8));
            w[2 * (u + 8) + 1] = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(16 * u + 8, 8));
        }

        for (var round = 0; round < 10; round++)
        {
            SubWords(w, ref k0, ref k1, ref k2, ref k3);
            ShiftRows(w);
            MixColumns(w);
        }

        for (var u = 0; u < 16; u++)
            v[u] ^= BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(8 * u, 8)) ^ w[u] ^ w[u + 16];
    }

    private static void SubWords(ulong[] w, ref uint k0, ref uint k1, ref uint k2, ref uint k3)
    {
        for (var n = 0; n < 16; n++)
        {
            var wl = w[2 * n];
            var wh = w[2 * n + 1];
            var x0 = (uint)wl;
            var x1 = (uint)(wl >> 32);
            var x2 = (uint)wh;
            var x3 = (uint)(wh >> 32);

            AesRound.Round(ref x0, ref x1, ref x2, ref x3, k0, k1, k2, k3);
            AesRound.Round(ref x0, ref x1, ref x2, ref x3);

            w[2 * n] = x0 | ((ulong)x1 << 32);
            w[2 * n + 1] = x2 | ((ulong)x3 << 32);

            if (++k0 == 0 && ++k1 == 0 && ++k2 == 0)
                k3++;
        }
    }

    private static void ShiftRows(ulong[] w)
    {
        RotateRow(w, 1, 5, 9, 13);
        Swap(w, 2, 10);
        Swap(w, 6, 14);
        RotateRow(w, 15, 11, 7, 3);
    }

    // The words at a, b, c, d move one place: a gets b, b gets c, c gets d, d gets the old a.
    private static void RotateRow(ulong[] w, int a, int b, int c, int d)
    {
        for (var half = 0; half < 2; half++)
        {
            var tmp = w[2 * a + half];
            w[2 * a + half] = w[2 * b + half];
            w[2 * b + half] = w[2 * c + half];
            w[2 * c + half] = w[2 * d + half];
            w[2 * d + half] = tmp;
        }
    }

    private static void Swap(ulong[] w, int a, int b)
    {
        for (var half = 0; half < 2; half++)
            (w[2 * a + half], w[2 * b + half]) = (w[2 * b + half], w[2 * a + half]);
    }

    private static void MixColumns(ulong[] w)
    {
        MixColumn(w, 0, 1, 2, 3);
        MixColumn(w, 4, 5, 6, 7);
        MixColumn(w, 8, 9, 10, 11);
        MixColumn(w, 12, 13, 14, 15);
    }

    // AES MixColumns over eight byte lanes at once, with the 128-bit words as the four "bytes" of the column.
    private static void MixColumn(ulong[] w, int ia, int ib, int ic, int id)
    {
        for (var n = 0; n < 2; n++)
        {
            var a = w[2 * ia + n];
            var b = w[2 * ib + n];
            var c = w[2 * ic + n];
            var d = w[2 * id + n];
            var ab = a ^ b;
            var bc = b ^ c;
            var cd = c ^ d;
            var abx = (((ab & HighBits) >> 7) * 27UL) ^ ((ab & LowBits) << 1);
            var bcx = (((bc & HighBits) >> 7) * 27UL) ^ ((bc & LowBits) << 1);
            var cdx = (((cd & HighBits) >> 7) * 27UL) ^ ((cd & LowBits) << 1);
            w[2 * ia + n] = abx ^ bc ^ d;
            w[2 * ib + n] = bcx ^ a ^ cd;
            w[2 * ic + n] = cdx ^ ab ^ d;
            w[2 * id + n] = abx ^ bcx ^ cdx ^ ab ^ c;
        }
    }
}
