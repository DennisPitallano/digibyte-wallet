using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Per-wallet biometric (WebAuthn PRF) enrollment and unlock.
/// No secrets stored at rest — wrapping key is derived from the platform authenticator.
/// </summary>
public class BiometricService
{
    private readonly IJSRuntime _js;
    private readonly ISecureStorage _storage;

    private const string BioEnabledPrefix = "wallet_bio_enabled_";
    private const string BioCredPrefix = "wallet_bio_cred_";
    private const string BioWrapPrefix = "wallet_bio_wrap_";
    private const string BioSeedPrefix = "wallet_bio_seed_";
    private const string BioDismissedPrefix = "wallet_bio_dismissed_";
    private const string BioConfirmSendPrefix = "wallet_bio_confirm_send_";

    public BiometricService(IJSRuntime js, ISecureStorage storage)
    {
        _js = js;
        _storage = storage;
    }

    /// <summary>
    /// Check if the browser supports WebAuthn with the PRF extension.
    /// </summary>
    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("dgbWebAuthn.isSupported");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if biometric is enrolled for a specific wallet.
    /// </summary>
    public async Task<bool> IsEnabledAsync(string walletId)
    {
        var val = await _storage.GetAsync(BioEnabledPrefix + walletId);
        return val == "true";
    }

    /// <summary>
    /// Check if the user dismissed the enrollment prompt for this wallet.
    /// </summary>
    public async Task<bool> IsDismissedAsync(string walletId)
    {
        var val = await _storage.GetAsync(BioDismissedPrefix + walletId);
        return val == "true";
    }

    /// <summary>
    /// Mark the enrollment prompt as dismissed for this wallet.
    /// </summary>
    public async Task DismissPromptAsync(string walletId)
    {
        await _storage.SetAsync(BioDismissedPrefix + walletId, "true");
    }

    /// <summary>
    /// Enroll biometric for a wallet using the decrypted seed from a successful PIN unlock.
    /// </summary>
    public async Task<bool> EnrollAsync(string walletId, string walletName, byte[] decryptedSeed)
    {
        try
        {
            // Generate random 256-bit wrapping key
            var wrappingKey = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(wrappingKey);

            var wrappingKeyBase64 = Convert.ToBase64String(wrappingKey);
            var seedBase64 = Convert.ToBase64String(decryptedSeed);

            // Call WebAuthn enrollment — triggers platform authenticator (fingerprint/face)
            var result = await _js.InvokeAsync<BiometricEnrollResult>(
                "dgbWebAuthn.enroll", walletId, walletName, wrappingKeyBase64, seedBase64);

            if (result == null || string.IsNullOrEmpty(result.CredentialId))
                return false;

            // Persist all biometric data in IndexedDB
            await _storage.SetAsync(BioCredPrefix + walletId, result.CredentialId);
            await _storage.SetAsync(BioWrapPrefix + walletId, result.WrappedKey);
            await _storage.SetAsync(BioSeedPrefix + walletId, result.BioSeed);
            await _storage.SetAsync(BioEnabledPrefix + walletId, "true");

            // Clear any previous dismissal
            await _storage.RemoveAsync(BioDismissedPrefix + walletId);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Unlock a wallet using biometric authentication. Returns the decrypted seed or null.
    /// </summary>
    public async Task<byte[]?> UnlockAsync(string walletId)
    {
        try
        {
            var credentialId = await _storage.GetAsync(BioCredPrefix + walletId);
            var wrappedKey = await _storage.GetAsync(BioWrapPrefix + walletId);
            var bioSeed = await _storage.GetAsync(BioSeedPrefix + walletId);

            if (credentialId == null || wrappedKey == null || bioSeed == null)
                return null;

            var seedBase64 = await _js.InvokeAsync<string?>(
                "dgbWebAuthn.authenticate", walletId, credentialId, wrappedKey, bioSeed);

            return seedBase64 != null ? Convert.FromBase64String(seedBase64) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verify the user's identity with biometric (no decryption — just an assertion).
    /// Used for confirming sends.
    /// </summary>
    public async Task<bool> VerifyIdentityAsync(string walletId)
    {
        try
        {
            var credentialId = await _storage.GetAsync(BioCredPrefix + walletId);
            if (credentialId == null) return false;

            return await _js.InvokeAsync<bool>("dgbWebAuthn.verifyIdentity", credentialId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if "confirm sends with biometric" is enabled for this wallet.
    /// </summary>
    public async Task<bool> IsConfirmSendEnabledAsync(string walletId)
    {
        var val = await _storage.GetAsync(BioConfirmSendPrefix + walletId);
        return val == "true";
    }

    /// <summary>
    /// Enable or disable "confirm sends with biometric" for this wallet.
    /// </summary>
    public async Task SetConfirmSendAsync(string walletId, bool enabled)
    {
        if (enabled)
            await _storage.SetAsync(BioConfirmSendPrefix + walletId, "true");
        else
            await _storage.RemoveAsync(BioConfirmSendPrefix + walletId);
    }

    /// <summary>
    /// Disable biometric for a wallet — removes all bio-related keys.
    /// </summary>
    public async Task DisableAsync(string walletId)
    {
        await _storage.RemoveAsync(BioCredPrefix + walletId);
        await _storage.RemoveAsync(BioWrapPrefix + walletId);
        await _storage.RemoveAsync(BioSeedPrefix + walletId);
        await _storage.RemoveAsync(BioEnabledPrefix + walletId);
        await _storage.RemoveAsync(BioDismissedPrefix + walletId);
        await _storage.RemoveAsync(BioConfirmSendPrefix + walletId);
    }

    private class BiometricEnrollResult
    {
        public string CredentialId { get; set; } = "";
        public string WrappedKey { get; set; } = "";
        public string BioSeed { get; set; } = "";
    }
}
