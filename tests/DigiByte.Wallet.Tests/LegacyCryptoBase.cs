using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Test double base: the legacy byte-array surface is abstract; the typed surface defaults to legacy Base64
/// (no Argon2 envelope), which is what the production interface no longer provides (T052).
/// </summary>
internal abstract class LegacyCryptoBase : ICryptoService
{
    public abstract Task<byte[]> EncryptAsync(byte[] plaintext, string pin);
    public abstract Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin);

    public virtual async Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(stored); }
        catch (FormatException) { return CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable); }
        var plain = await DecryptAsync(bytes, credential);
        return plain is null
            ? CryptoResult<byte[]>.Fail(CryptoFailure.WrongCredential)
            : CryptoResult<byte[]>.Ok(plain);
    }

    public virtual async Task<CryptoResult<string>> EncryptAsync(byte[] plaintext, string credential, CredentialKind kind)
        => CryptoResult<string>.Ok(Convert.ToBase64String(await EncryptAsync(plaintext, credential)));
}
