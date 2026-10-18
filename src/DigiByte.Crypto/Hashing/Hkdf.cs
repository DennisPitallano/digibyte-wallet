using NBitcoin.Crypto;

namespace DigiByte.Crypto.Hashing;

/// <summary>
/// HKDF-SHA256 (RFC 5869), single output block (up to 32 bytes). Built on NBitcoin's HMAC so it runs the same
/// in the browser (WASM) as everywhere else. Used to derive the DigiDollar storage key from the unlocked seed.
/// </summary>
public static class Hkdf
{
    private const int HashLength = 32;

    public static byte[] DeriveSha256(byte[] inputKeyMaterial, byte[] salt, byte[] info, int length)
    {
        ArgumentNullException.ThrowIfNull(inputKeyMaterial);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(info);
        if (inputKeyMaterial.Length == 0)
            throw new ArgumentException("Input key material must not be empty.", nameof(inputKeyMaterial));
        if (length < 1 || length > HashLength)
            throw new ArgumentOutOfRangeException(nameof(length), $"Length must be 1..{HashLength}.");

        // Extract: PRK = HMAC(salt or 32 zero bytes, IKM)
        var prk = Hashes.HMACSHA256(salt.Length == 0 ? new byte[HashLength] : salt, inputKeyMaterial);

        // Expand (first block only): T(1) = HMAC(PRK, info || 0x01)
        var block = new byte[info.Length + 1];
        Buffer.BlockCopy(info, 0, block, 0, info.Length);
        block[^1] = 0x01;
        var okm = Hashes.HMACSHA256(prk, block);
        return okm.AsSpan(0, length).ToArray();
    }
}
