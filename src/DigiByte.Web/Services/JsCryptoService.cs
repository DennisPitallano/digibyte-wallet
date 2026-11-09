using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// ICryptoService implementation using browser SubtleCrypto (and a worker-hosted Argon2id) via JS interop.
/// The byte-array methods are the transitional legacy surface; the string-based typed methods use the
/// versioned <c>dgb2</c> envelope and report why a decryption failed.
/// </summary>
public class JsCryptoService : ICryptoService
{
    private readonly IJSRuntime _js;

    public JsCryptoService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<byte[]> EncryptAsync(byte[] plaintext, string pin)
    {
        var plaintextBase64 = Convert.ToBase64String(plaintext);
        var resultBase64 = await _js.InvokeAsync<string>("dgbCrypto.encrypt", plaintextBase64, pin);
        return Convert.FromBase64String(resultBase64);
    }

    public async Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin)
    {
        var ciphertextBase64 = Convert.ToBase64String(ciphertext);
        var resultBase64 = await _js.InvokeAsync<string?>("dgbCrypto.decrypt", ciphertextBase64, pin);
        return resultBase64 != null ? Convert.FromBase64String(resultBase64) : null;
    }

    public async Task<CryptoResult<string>> EncryptAsync(byte[] plaintext, string credential, CredentialKind kind)
    {
        var kindName = kind == CredentialKind.Passphrase ? "passphrase" : "pin";
        var r = await _js.InvokeAsync<JsEncryptResult>(
            "dgbCrypto.encryptV2", Convert.ToBase64String(plaintext), credential, kindName, null);
        return r.Ok && r.Envelope is not null
            ? CryptoResult<string>.Ok(r.Envelope)
            : CryptoResult<string>.Fail(MapFailure(r.Reason));
    }

    public async Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential)
    {
        var r = await _js.InvokeAsync<JsDecryptResult>("dgbCrypto.decryptV2", stored, credential);
        return r.Ok && r.PlaintextBase64 is not null
            ? CryptoResult<byte[]>.Ok(Convert.FromBase64String(r.PlaintextBase64))
            : CryptoResult<byte[]>.Fail(MapFailure(r.Reason));
    }

    private static CryptoFailure MapFailure(string? reason) => reason switch
    {
        "WrongCredential" => CryptoFailure.WrongCredential,
        "UnsupportedVersion" => CryptoFailure.UnsupportedVersion,
        "ResourceExhausted" => CryptoFailure.ResourceExhausted,
        "Unsupported" => CryptoFailure.Unsupported, // this browser cannot run Argon2id at all
        _ => CryptoFailure.Unreadable,
    };

    private sealed class JsEncryptResult
    {
        public bool Ok { get; set; }
        public string? Envelope { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class JsDecryptResult
    {
        public bool Ok { get; set; }
        public string? PlaintextBase64 { get; set; }
        public string? Reason { get; set; }
    }
}
