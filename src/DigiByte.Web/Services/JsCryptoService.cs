using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// ICryptoService implementation using browser SubtleCrypto via JS interop.
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
}
