using System.Buffers.Binary;
using System.Numerics;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// SIMD-512 as in DigiByte Core's <c>crypto/simd.cpp</c>: each 128-byte block is expanded with a 256-point number-theoretic transform
/// over the integers modulo 257, and the 32-word state is then mixed through four rounds of eight steps using that expansion as the
/// message words. The transform here follows the reference's recursive structure and its exact integer arithmetic, because the
/// final reduction depends on it.
/// </summary>
internal static class Simd512
{
    // For the last block the message words are read from these 8-word groups of the transformed block, scaled by 16.
    private static readonly int[] WordBlockOffsets =
    [
        4 << 4, 6 << 4, 0 << 4, 2 << 4, 7 << 4, 5 << 4, 3 << 4, 1 << 4,
        15 << 4, 11 << 4, 12 << 4, 8 << 4, 9 << 4, 13 << 4, 10 << 4, 14 << 4,
        17 << 4, 18 << 4, 23 << 4, 20 << 4, 22 << 4, 21 << 4, 16 << 4, 19 << 4,
        30 << 4, 24 << 4, 25 << 4, 31 << 4, 27 << 4, 29 << 4, 28 << 4, 26 << 4,
    ];

    private static readonly int[] StepPermutation = [1, 6, 2, 3, 5, 7, 4, 1, 6, 2, 3];

    /// <summary>The 64-byte SIMD-512 digest of the message.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        var state = (uint[])SimdTables.InitialValue.Clone();
        var buf = new byte[128];
        uint countLow = 0, countHigh = 0;
        var ptr = 0;

        var offset = 0;
        while (message.Length - offset > 0)
        {
            var clen = Math.Min(128 - ptr, message.Length - offset);
            message.Slice(offset, clen).CopyTo(buf.AsSpan(ptr));
            offset += clen;
            ptr += clen;
            if (ptr == 128)
            {
                Compress(state, buf, last: false);
                ptr = 0;
                countLow++;
                if (countLow == 0)
                    countHigh++;
            }
        }

        if (ptr > 0)
        {
            Array.Clear(buf, ptr, 128 - ptr);
            buf[ptr] = 0;
            Compress(state, buf, last: false);
        }
        Array.Clear(buf);
        // The count of 1024-bit blocks, shifted into bits, plus the bits in the last partial block, exactly as the reference writes it.
        var low = countLow << 10;
        var high = (countHigh << 10) + (low >> 22);
        low += (uint)(ptr << 3);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), low);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), high);
        Compress(state, buf, last: true);

        var digest = new byte[64];
        for (var i = 0; i < 16; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(4 * i, 4), state[i]);
        return digest;
    }

    private static int Reds1(int x) => (x & 0xFF) - (x >> 8);

    private static int Reds2(int x) => (x & 0xFFFF) + (x >> 16);

    // One layer of butterflies over a block of q: the pair (rb + u, rb + u + hk) is combined with a power of alpha.
    private static void FftLoop(int[] q, int rb, int hk, int step)
    {
        var m = q[rb];
        var n = q[rb + hk];
        q[rb] = m + n;
        q[rb + hk] = m - n;
        for (var u = 1; u < hk; u++)
        {
            m = q[rb + u];
            n = q[rb + u + hk];
            var t = Reds2(n * SimdTables.Alpha[u * step]);
            q[rb + u] = m + t;
            q[rb + u + hk] = m - t;
        }
    }

    // Eight-point transform of four inputs spaced xs apart, with the zero upper half folded in.
    private static void Fft8(byte[] x, int xb, int xs, Span<int> d)
    {
        int x0 = x[xb];
        int x1 = x[xb + xs];
        int x2 = x[xb + 2 * xs];
        int x3 = x[xb + 3 * xs];
        var a0 = x0 + x2;
        var a1 = x0 + (x2 << 4);
        var a2 = x0 - x2;
        var a3 = x0 - (x2 << 4);
        var b0 = x1 + x3;
        var b1 = Reds1((x1 << 2) + (x3 << 6));
        var b2 = (x1 << 4) - (x3 << 4);
        var b3 = Reds1((x1 << 6) + (x3 << 2));
        d[0] = a0 + b0;
        d[1] = a1 + b1;
        d[2] = a2 + b2;
        d[3] = a3 + b3;
        d[4] = a0 - b0;
        d[5] = a1 - b1;
        d[6] = a2 - b2;
        d[7] = a3 - b3;
    }

    private static void Fft16(byte[] x, int xb, int xs, int[] q, int rb)
    {
        Span<int> d1 = stackalloc int[8];
        Span<int> d2 = stackalloc int[8];
        Fft8(x, xb, xs << 1, d1);
        Fft8(x, xb + xs, xs << 1, d2);
        for (var k = 0; k < 8; k++)
        {
            q[rb + k] = d1[k] + (d2[k] << k);
            q[rb + 8 + k] = d1[k] - (d2[k] << k);
        }
    }

    private static void Fft32(byte[] x, int xb, int xs, int[] q, int rb)
    {
        Fft16(x, xb, xs << 1, q, rb);
        Fft16(x, xb + xs, xs << 1, q, rb + 16);
        FftLoop(q, rb, 16, 8);
    }

    private static void Fft64(byte[] x, int xb, int xs, int[] q, int rb)
    {
        Fft32(x, xb, xs << 1, q, rb);
        Fft32(x, xb + xs, xs << 1, q, rb + 32);
        FftLoop(q, rb, 32, 4);
    }

    private static void Fft256(byte[] x, int[] q)
    {
        Fft64(x, 0, 4, q, 0);
        Fft64(x, 2, 4, q, 64);
        FftLoop(q, 0, 64, 2);
        Fft64(x, 1, 4, q, 128);
        Fft64(x, 3, 4, q, 192);
        FftLoop(q, 128, 64, 2);
        FftLoop(q, 0, 128, 1);
    }

    private static uint Inner(int l, int h, int mm) => ((uint)(l * mm) & 0xFFFFu) + ((uint)(h * mm) << 16);

    private static uint If(uint x, uint y, uint z) => ((y ^ z) & x) ^ z;

    private static uint Maj(uint x, uint y, uint z) => (x & y) | ((x | y) & z);

    private static void Compress(uint[] chain, byte[] buf, bool last)
    {
        var q = new int[256];
        Fft256(buf, q);

        var offsets = last ? SimdTables.YoffFinal : SimdTables.YoffNormal;
        for (var i = 0; i < 256; i++)
        {
            var tq = q[i] + offsets[i];
            tq = Reds2(tq);
            tq = Reds1(tq);
            tq = Reds1(tq);
            q[i] = tq <= 128 ? tq : tq - 257;
        }

        var state = new uint[32];
        for (var i = 0; i < 32; i++)
            state[i] = chain[i] ^ BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4 * i, 4));

        var w = new uint[64];
        ReadWords(q, w, 0, 0, 1, 185);
        OneRound(state, w, 0, 3, 23, 17, 27);
        ReadWords(q, w, 8, 0, 1, 185);
        OneRound(state, w, 1, 28, 19, 22, 7);
        ReadWords(q, w, 16, -256, -128, 233);
        OneRound(state, w, 2, 29, 9, 15, 5);
        ReadWords(q, w, 24, -383, -255, 233);
        OneRound(state, w, 3, 4, 13, 10, 25);

        // Feed the old chaining value forward through four more steps.
        var oldWords = new uint[8];
        Step(state, Words(chain, 0, oldWords), If, 4, 13, 5);
        Step(state, Words(chain, 8, oldWords), If, 13, 10, 7);
        Step(state, Words(chain, 16, oldWords), If, 10, 25, 4);
        Step(state, Words(chain, 24, oldWords), If, 25, 4, 1);

        Array.Copy(state, chain, 32);
    }

    private static uint[] Words(uint[] source, int start, uint[] target)
    {
        Array.Copy(source, start, target, 0, 8);
        return target;
    }

    private static void ReadWords(int[] q, uint[] w, int sb, int o1, int o2, int mm)
    {
        for (var u = 0; u < 64; u += 8)
        {
            var v = WordBlockOffsets[(u >> 3) + sb];
            for (var k = 0; k < 8; k++)
                w[u + k] = Inner(q[v + 2 * k + o1], q[v + 2 * k + o2], mm);
        }
    }

    private static void OneRound(uint[] state, uint[] w, int isp, int p0, int p1, int p2, int p3)
    {
        Span<uint> words = stackalloc uint[8];
        for (var step = 0; step < 8; step++)
        {
            w.AsSpan(8 * step, 8).CopyTo(words);
            Func<uint, uint, uint, uint> fun = step < 4 ? If : Maj;
            var (r, s) = (step & 3) switch
            {
                0 => (p0, p1),
                1 => (p1, p2),
                2 => (p2, p3),
                _ => (p3, p0),
            };
            Step(state, words.ToArray(), fun, r, s, StepPermutation[isp + step]);
        }
    }

    // One step over the eight lanes: A, B, C and D are the four 8-word groups of the state.
    private static void Step(uint[] state, uint[] w, Func<uint, uint, uint, uint> fun, int r, int s, int pp)
    {
        Span<uint> ta = stackalloc uint[8];
        for (var n = 0; n < 8; n++)
            ta[n] = BitOperations.RotateLeft(state[n], r);

        for (var n = 0; n < 8; n++)
        {
            var a = state[n];
            var b = state[8 + n];
            var c = state[16 + n];
            var d = state[24 + n];
            var tt = d + w[n] + fun(a, b, c);
            state[n] = BitOperations.RotateLeft(tt, s) + ta[pp ^ n];
            state[24 + n] = c;
            state[16 + n] = b;
            state[8 + n] = ta[n];
        }
    }
}
