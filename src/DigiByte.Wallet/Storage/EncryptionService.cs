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
/// Abstraction for encryption operations. Implemented via browser SubtleCrypto in WASM.
/// The byte-array methods are the transitional legacy surface (removed in T052 except for the legacy
/// write path); the string-based typed methods are the new surface. The typed methods have default
/// implementations over the legacy ones so existing implementations keep working.
/// </summary>
public interface ICryptoService
{
    Task<byte[]> EncryptAsync(byte[] plaintext, string pin);
    Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin);

    /// <summary>Decrypts a stored value (legacy Base64 or a <c>dgb2$</c> envelope) with a typed result.</summary>
    async Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential)
    {
        var parsed = EnvelopeCodec.Parse(stored);
        switch (parsed.Status)
        {
            case EnvelopeStatus.UnsupportedVersion:
                return CryptoResult<byte[]>.Fail(CryptoFailure.UnsupportedVersion);
            case EnvelopeStatus.Unreadable:
            case EnvelopeStatus.Ok: // implementations without Argon2 cannot open a dgb2 envelope
                return CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable);
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(stored); }
        catch (FormatException) { return CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable); }

        var plain = await DecryptAsync(bytes, credential);
        return plain is null
            ? CryptoResult<byte[]>.Fail(CryptoFailure.WrongCredential)
            : CryptoResult<byte[]>.Ok(plain);
    }

    /// <summary>Encrypts and returns the stored string form. Default: legacy Base64 (the kind is ignored).</summary>
    async Task<CryptoResult<string>> EncryptAsync(byte[] plaintext, string credential, CredentialKind kind)
    {
        var cipher = await EncryptAsync(plaintext, credential);
        return CryptoResult<string>.Ok(Convert.ToBase64String(cipher));
    }
}
