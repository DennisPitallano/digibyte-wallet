using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Storage;

/// <summary>Which kind of credential protects a secret. Values match the envelope header byte.</summary>
public enum CredentialKind
{
    Pin = 1,
    Passphrase = 2,
}

/// <summary>Why a decryption failed. Only <see cref="WrongCredential"/> counts toward the attempt throttle.</summary>
public enum CryptoFailure
{
    WrongCredential,
    Unreadable,
    UnsupportedVersion,
    ResourceExhausted,
    /// <summary>This browser or device cannot run the memory-hard protection at all (no WebAssembly or worker support).</summary>
    Unsupported,
}

public sealed class CryptoResult<T>
{
    private CryptoResult(T? value, CryptoFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public bool Success => Failure is null;
    public T? Value { get; }
    public CryptoFailure? Failure { get; }

    /// <summary>True only for a failed authentication, which is what the throttle should count (FR-017).</summary>
    public bool CountsAsWrongCredential => Failure == CryptoFailure.WrongCredential;

    public static CryptoResult<T> Ok(T value) => new(value, null);
    public static CryptoResult<T> Fail(CryptoFailure failure) => new(default, failure);
}

/// <summary>
/// Abstraction for encryption operations. Implemented via browser SubtleCrypto and a worker-hosted
/// Argon2id in WASM. The string-based typed methods use the versioned <c>dgb2</c> envelope and report why a
/// decryption failed. The byte-array methods are the legacy AES surface, kept for reading old values, for
/// the legacy write path used on devices that cannot run Argon2id (FR-021), and for the DigiDollar store.
/// </summary>
public interface ICryptoService
{
    // Legacy surface.
    Task<byte[]> EncryptAsync(byte[] plaintext, string pin);
    Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin);

    /// <summary>Decrypts a stored value (legacy Base64 or a <c>dgb2$</c> envelope) with a typed result.</summary>
    Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential);

    /// <summary>Encrypts into a <c>dgb2$</c> envelope and returns the stored string form.</summary>
    Task<CryptoResult<string>> EncryptAsync(byte[] plaintext, string credential, CredentialKind kind);
}
