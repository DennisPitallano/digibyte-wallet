namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// One AES round (SubBytes, ShiftRows, MixColumns) on a 128-bit state held as four little-endian 32-bit columns, as the AES-based
/// hash functions in DigiByte Core use it (<c>aes_helper.h</c> with <c>AES_BIG_ENDIAN</c> set to 0). The lookup tables are computed
/// from the AES S-box when first used.
/// </summary>
internal static class AesRound
{
    private static readonly uint[] T0 = new uint[256];
    private static readonly uint[] T1 = new uint[256];
    private static readonly uint[] T2 = new uint[256];
    private static readonly uint[] T3 = new uint[256];

    static AesRound()
    {
        var sbox = BuildSbox();
        for (var i = 0; i < 256; i++)
        {
            var s = (uint)sbox[i];
            var s2 = Xtime(s);
            var s3 = s2 ^ s;
            // Byte 0 is the lowest byte: the contribution of one input byte to the four output bytes of its column.
            T0[i] = s2 | (s << 8) | (s << 16) | (s3 << 24);
            T1[i] = s3 | (s2 << 8) | (s << 16) | (s << 24);
            T2[i] = s | (s3 << 8) | (s2 << 16) | (s << 24);
            T3[i] = s | (s << 8) | (s3 << 16) | (s2 << 24);
        }
    }

    private static uint Xtime(uint b) => ((b << 1) ^ ((b & 0x80) != 0 ? 0x11Bu : 0u)) & 0xFF;

    private static byte[] BuildSbox()
    {
        // The AES S-box: multiplicative inverse in GF(2^8), then an affine transform.
        var sbox = new byte[256];
        byte p = 1, q = 1;
        do
        {
            // p walks through the field by multiplying by 3, q by the inverse of 3.
            p = (byte)(p ^ (p << 1) ^ ((p & 0x80) != 0 ? 0x1B : 0));
            q ^= (byte)(q << 1);
            q ^= (byte)(q << 2);
            q ^= (byte)(q << 4);
            if ((q & 0x80) != 0)
                q ^= 0x09;
            var x = (byte)(q ^ RotateLeft8(q, 1) ^ RotateLeft8(q, 2) ^ RotateLeft8(q, 3) ^ RotateLeft8(q, 4));
            sbox[p] = (byte)(x ^ 0x63);
        } while (p != 1);
        sbox[0] = 0x63;
        return sbox;
    }

    private static byte RotateLeft8(byte b, int n) => (byte)((b << n) | (b >> (8 - n)));

    /// <summary>One AES round with no round key: columns <paramref name="x0"/> to <paramref name="x3"/> in, the new columns out.</summary>
    public static void Round(ref uint x0, ref uint x1, ref uint x2, ref uint x3) => Round(ref x0, ref x1, ref x2, ref x3, 0, 0, 0, 0);

    /// <summary>One AES round followed by an XOR with a round key given as four columns.</summary>
    public static void Round(ref uint x0, ref uint x1, ref uint x2, ref uint x3, uint k0, uint k1, uint k2, uint k3)
    {
        var y0 = T0[x0 & 0xFF] ^ T1[(x1 >> 8) & 0xFF] ^ T2[(x2 >> 16) & 0xFF] ^ T3[x3 >> 24] ^ k0;
        var y1 = T0[x1 & 0xFF] ^ T1[(x2 >> 8) & 0xFF] ^ T2[(x3 >> 16) & 0xFF] ^ T3[x0 >> 24] ^ k1;
        var y2 = T0[x2 & 0xFF] ^ T1[(x3 >> 8) & 0xFF] ^ T2[(x0 >> 16) & 0xFF] ^ T3[x1 >> 24] ^ k2;
        var y3 = T0[x3 & 0xFF] ^ T1[(x0 >> 8) & 0xFF] ^ T2[(x1 >> 16) & 0xFF] ^ T3[x2 >> 24] ^ k3;
        x0 = y0;
        x1 = y1;
        x2 = y2;
        x3 = y3;
    }
}
