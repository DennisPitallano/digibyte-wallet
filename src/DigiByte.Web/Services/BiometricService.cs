using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Global biometric (WebAuthn) enrollment and unlock.
/// A single credential is shared across all wallets. Each wallet's seed
/// is encrypted with a shared AES key; decryption requires a biometric assertion.
/// </summary>
public class BiometricService
{
    private readonly IJSRuntime _js;
    private readonly ISecureStorage _storage;

    // Global keys (single credential for all wallets)
    private const string BioEnabled = "bio_enabled";
    private const string BioCred = "bio_cred";
    private const string BioKey = "bio_key";
    private const string BioDismissed = "bio_dismissed";
    private const string BioConfirmSend = "bio_confirm_send";

    // Per-wallet encrypted seed
    private const string BioSeedPrefix = "bio_seed_";

    public BiometricService(IJSRuntime js, ISecureStorage storage)
    {
        _js = js;
        _storage = storage;
    }

    /// <summary>Check if the browser supports WebAuthn with a platform authenticator.</summary>
    public async Task<bool> IsSupportedAsync()
    {
        try { return await _js.InvokeAsync<bool>("dgbWebAuthn.isSupported"); }
        catch { return false; }
    }

    /// <summary>Check if biometric is enrolled (global — not per-wallet).</summary>
    public async Task<bool> IsEnabledAsync()
    {
        var val = await _storage.GetAsync(BioEnabled);
        return val == "true";
    }

    /// <summary>Check if a specific wallet has its seed enrolled for biometric unlock.</summary>
    public async Task<bool> HasWalletSeedAsync(string walletId)
    {
        var val = await _storage.GetAsync(BioSeedPrefix + walletId);
        return val != null;
    }

    /// <summary>Check if the user dismissed the enrollment prompt.</summary>
    public async Task<bool> IsDismissedAsync()
    {
        var val = await _storage.GetAsync(BioDismissed);
        return val == "true";
    }

    /// <summary>Mark the enrollment prompt as dismissed.</summary>
    public async Task DismissPromptAsync()
    {
        await _storage.SetAsync(BioDismissed, "true");
    }

    /// <summary>
    /// Enroll biometric: creates a WebAuthn credential and encrypts the first wallet's seed.
    /// </summary>
    public async Task<bool> EnrollAsync(string walletId, string walletName, byte[] decryptedSeed)
    {
        try
        {
            var result = await _js.InvokeAsync<BiometricEnrollResult>(
                "dgbWebAuthn.enroll", walletName);

            if (result == null || string.IsNullOrEmpty(result.CredentialId) || string.IsNullOrEmpty(result.BioKey))
                return false;

            // Store global credential and key
            await _storage.SetAsync(BioCred, result.CredentialId);
            await _storage.SetAsync(BioKey, result.BioKey);
            await _storage.SetAsync(BioEnabled, "true");

            // Encrypt this wallet's seed
            var seedBase64 = Convert.ToBase64String(decryptedSeed);
            var encryptedSeed = await _js.InvokeAsync<string>(
                "dgbWebAuthn.encryptSeed", result.BioKey, seedBase64);
            await _storage.SetAsync(BioSeedPrefix + walletId, encryptedSeed);

            // Clear any previous dismissal
            await _storage.RemoveAsync(BioDismissed);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Add a wallet's seed to biometric unlock (biometric must already be enrolled).
    /// No biometric prompt — uses the stored key directly.
    /// </summary>
    public async Task<bool> AddWalletSeedAsync(string walletId, byte[] decryptedSeed)
    {
        try
        {
            var bioKey = await _storage.GetAsync(BioKey);
            if (bioKey == null) return false;

            var seedBase64 = Convert.ToBase64String(decryptedSeed);
            var encryptedSeed = await _js.InvokeAsync<string>(
                "dgbWebAuthn.encryptSeed", bioKey, seedBase64);
            await _storage.SetAsync(BioSeedPrefix + walletId, encryptedSeed);

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
            var credentialId = await _storage.GetAsync(BioCred);
            var bioKey = await _storage.GetAsync(BioKey);
            var encryptedSeed = await _storage.GetAsync(BioSeedPrefix + walletId);

            if (credentialId == null || bioKey == null || encryptedSeed == null)
                return null;

            var seedBase64 = await _js.InvokeAsync<string?>(
                "dgbWebAuthn.authenticate", credentialId, bioKey, encryptedSeed);

            return seedBase64 != null ? Convert.FromBase64String(seedBase64) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verify the user's identity with biometric (no decryption — just an assertion).
    /// </summary>
    public async Task<bool> VerifyIdentityAsync()
    {
        try
        {
            var credentialId = await _storage.GetAsync(BioCred);
            if (credentialId == null) return false;

            return await _js.InvokeAsync<bool>("dgbWebAuthn.verifyIdentity", credentialId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Check if "confirm sends with biometric" is enabled.</summary>
    public async Task<bool> IsConfirmSendEnabledAsync()
    {
        var val = await _storage.GetAsync(BioConfirmSend);
        return val == "true";
    }

    /// <summary>Enable or disable "confirm sends with biometric".</summary>
    public async Task SetConfirmSendAsync(bool enabled)
    {
        if (enabled)
            await _storage.SetAsync(BioConfirmSend, "true");
        else
            await _storage.RemoveAsync(BioConfirmSend);
    }

    /// <summary>
    /// Remove a single wallet's biometric seed (e.g., when deleting a wallet).
    /// </summary>
    public async Task RemoveWalletSeedAsync(string walletId)
    {
        await _storage.RemoveAsync(BioSeedPrefix + walletId);
    }

    /// <summary>
    /// Disable biometric entirely — removes credential, key, all wallet seeds, and preferences.
    /// </summary>
    public async Task DisableAsync()
    {
        await _storage.RemoveAsync(BioCred);
        await _storage.RemoveAsync(BioKey);
        await _storage.RemoveAsync(BioEnabled);
        await _storage.RemoveAsync(BioDismissed);
        await _storage.RemoveAsync(BioConfirmSend);

        // Remove all per-wallet seeds
        var seedKeys = await _storage.GetKeysWithPrefixAsync(BioSeedPrefix);
        foreach (var key in seedKeys)
            await _storage.RemoveAsync(key);
    }

    private class BiometricEnrollResult
    {
        public string CredentialId { get; set; } = "";
        public string BioKey { get; set; } = "";
    }
}
