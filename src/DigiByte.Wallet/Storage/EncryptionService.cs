namespace DigiByte.Wallet.Storage;

/// <summary>
/// Abstraction for encryption operations. Implemented via browser SubtleCrypto in WASM.
/// </summary>
public interface ICryptoService
{
    Task<byte[]> EncryptAsync(byte[] plaintext, string pin);
    Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin);
}
