using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>What happened when the user tried to turn biometric unlock on.</summary>
public enum BiometricEnrollOutcome
{
    Enrolled,
    /// <summary>This browser cannot protect the key with the authenticator (no PRF). Nothing was stored; ask the user.</summary>
    NeedsWeakConsent,
    Failed,
}

/// <summary>What happened when an existing enrolment was upgraded to the stronger protection.</summary>
public enum BiometricUpgradeOutcome
{
    Upgraded,
    /// <summary>This browser has no PRF. The enrolment is unchanged and keeps working as before.</summary>
    NotSupported,
    Failed,
}

/// <summary>
/// Global biometric (WebAuthn) enrollment and unlock.
/// A single credential is shared across all wallets. Each wallet's seed copy is encrypted with an AES key.
/// With the WebAuthn PRF extension that key is derived from the authenticator at every unlock and never stored
/// ("enrolled-prf"), so copying the browser's data does not expose the seed. Without PRF the old behaviour is kept
/// ("enrolled-weak"): a key stored next to the ciphertext, only behind the user's consent and a one-time warning.
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

    // Protection state: absent (enrolled before this feature), "enrolled-prf" or "enrolled-weak"
    private const string BioPrfState = "bio_prf_state";
    private const string StatePrf = "enrolled-prf";
    private const string StateWeak = "enrolled-weak";
    private const string BioWeakWarned = "bio_weak_warned";

    // Per-wallet encrypted seed
    private const string BioSeedPrefix = "bio_seed_";

    // A credential created while asking for the weak path, kept so consent does not need a second biometric prompt.
    private string? _pendingWeakCredentialId;

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

    /// <summary>True when the seed copies are protected by a key derived from the authenticator (nothing stored).</summary>
    public async Task<bool> IsPrfProtectedAsync() => await _storage.GetAsync(BioPrfState) == StatePrf;

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
    /// Enroll biometric and protect the first wallet's seed copy. With PRF the key is derived and never stored. Without
    /// it nothing is stored until the user has agreed to the weaker protection (<paramref name="acceptWeak"/>).
    /// </summary>
    public async Task<BiometricEnrollOutcome> EnrollAsync(string walletId, string walletName, byte[] decryptedSeed, bool acceptWeak = false)
    {
        try
        {
            string credentialId;
            bool prfSupported;
            if (acceptWeak && _pendingWeakCredentialId is not null)
            {
                credentialId = _pendingWeakCredentialId;
                prfSupported = false;
            }
            else
            {
                var created = await _js.InvokeAsync<PrfEnrollResult?>("dgbWebAuthn.enrollPrf", walletName);
                if (created is null || string.IsNullOrEmpty(created.CredentialId)) return BiometricEnrollOutcome.Failed;
                credentialId = created.CredentialId;
                prfSupported = created.PrfSupported;
            }

            var seedBase64 = Convert.ToBase64String(decryptedSeed);

            if (prfSupported)
            {
                var key = await _js.InvokeAsync<string?>("dgbWebAuthn.deriveKeyPrf", credentialId);
                if (string.IsNullOrEmpty(key)) return BiometricEnrollOutcome.Failed;

                var encryptedSeed = await _js.InvokeAsync<string>("dgbWebAuthn.encryptSeed", key, seedBase64);
                await _storage.SetAsync(BioSeedPrefix + walletId, encryptedSeed);
                await _storage.SetAsync(BioCred, credentialId);
                await _storage.SetAsync(BioPrfState, StatePrf);
                await _storage.SetAsync(BioEnabled, "true");
                await _storage.RemoveAsync(BioKey);
                await _storage.RemoveAsync(BioWeakWarned);
                await _storage.RemoveAsync(BioDismissed);
                _pendingWeakCredentialId = null;
                return BiometricEnrollOutcome.Enrolled;
            }

            if (!acceptWeak)
            {
                _pendingWeakCredentialId = credentialId;
                return BiometricEnrollOutcome.NeedsWeakConsent;
            }

            var weakKey = await _js.InvokeAsync<string>("dgbWebAuthn.newBioKey");
            var weakSeed = await _js.InvokeAsync<string>("dgbWebAuthn.encryptSeed", weakKey, seedBase64);
            await _storage.SetAsync(BioSeedPrefix + walletId, weakSeed);
            await _storage.SetAsync(BioCred, credentialId);
            await _storage.SetAsync(BioKey, weakKey);
            await _storage.SetAsync(BioPrfState, StateWeak);
            await _storage.SetAsync(BioWeakWarned, "true"); // the user has just agreed to the warning
            await _storage.SetAsync(BioEnabled, "true");
            await _storage.RemoveAsync(BioDismissed);
            _pendingWeakCredentialId = null;
            return BiometricEnrollOutcome.Enrolled;
        }
        catch
        {
            return BiometricEnrollOutcome.Failed;
        }
    }

    /// <summary>The user declined the weaker protection: forget the credential that was created for it.</summary>
    public void CancelPendingEnrollment() => _pendingWeakCredentialId = null;

    /// <summary>
    /// Add a wallet's seed to biometric unlock (biometric must already be enrolled). With PRF this needs a biometric
    /// check, because the key exists only for the moment it is derived.
    /// </summary>
    public async Task<bool> AddWalletSeedAsync(string walletId, byte[] decryptedSeed)
    {
        try
        {
            var seedBase64 = Convert.ToBase64String(decryptedSeed);

            if (await IsPrfProtectedAsync())
            {
                var credentialId = await _storage.GetAsync(BioCred);
                if (credentialId == null) return false;
                var key = await _js.InvokeAsync<string?>("dgbWebAuthn.deriveKeyPrf", credentialId);
                if (string.IsNullOrEmpty(key)) return false;

                var sealedSeed = await _js.InvokeAsync<string>("dgbWebAuthn.encryptSeed", key, seedBase64);
                await _storage.SetAsync(BioSeedPrefix + walletId, sealedSeed);
                return true;
            }

            var bioKey = await _storage.GetAsync(BioKey);
            if (bioKey == null) return false;

            var encryptedSeed = await _js.InvokeAsync<string>("dgbWebAuthn.encryptSeed", bioKey, seedBase64);
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
            var encryptedSeed = await _storage.GetAsync(BioSeedPrefix + walletId);
            if (credentialId == null || encryptedSeed == null) return null;

            string? seedBase64;
            if (await IsPrfProtectedAsync())
            {
                var key = await _js.InvokeAsync<string?>("dgbWebAuthn.deriveKeyPrf", credentialId);
                if (string.IsNullOrEmpty(key)) return null;
                seedBase64 = await _js.InvokeAsync<string?>("dgbWebAuthn.decryptSeed", key, encryptedSeed);
            }
            else
            {
                var bioKey = await _storage.GetAsync(BioKey);
                if (bioKey == null) return null;
                seedBase64 = await _js.InvokeAsync<string?>("dgbWebAuthn.authenticate", credentialId, bioKey, encryptedSeed);
            }

            return seedBase64 != null ? Convert.FromBase64String(seedBase64) : null;
        }
        catch
        {
            return null;
        }
    }

    // ---- existing enrolments (made before the key was protected by the authenticator) ----

    /// <summary>True once, for an enrolment made before this feature: offer the one-time upgrade.</summary>
    public async Task<bool> NeedsUpgradePromptAsync() =>
        await IsEnabledAsync() && await _storage.GetAsync(BioPrfState) == null;

    /// <summary>The user chose not to upgrade now. The old behaviour continues and the warning is due once.</summary>
    public async Task DeclineUpgradeAsync()
    {
        if (await IsEnabledAsync() && await _storage.GetAsync(BioPrfState) == null)
            await _storage.SetAsync(BioPrfState, StateWeak);
    }

    /// <summary>
    /// Re-enrol with the PRF extension and re-protect every wallet's seed copy under the derived key, then remove the
    /// stored key. The stored key is removed last, so a failure part-way leaves it available. Where PRF is not
    /// available nothing changes and the weaker state is recorded so the warning can be shown once.
    /// </summary>
    public async Task<BiometricUpgradeOutcome> UpgradeAsync(string walletName)
    {
        if (!await IsEnabledAsync() || await _storage.GetAsync(BioPrfState) != null)
            return BiometricUpgradeOutcome.Failed;

        string credentialId;
        string derivedKey;
        string oldKey;
        var reprotected = new Dictionary<string, string>();

        try
        {
            var created = await _js.InvokeAsync<PrfEnrollResult?>("dgbWebAuthn.enrollPrf", walletName);
            if (created is null || string.IsNullOrEmpty(created.CredentialId)) return BiometricUpgradeOutcome.Failed;

            if (!created.PrfSupported)
            {
                await _storage.SetAsync(BioPrfState, StateWeak);
                return BiometricUpgradeOutcome.NotSupported;
            }

            credentialId = created.CredentialId;
            var key = await _js.InvokeAsync<string?>("dgbWebAuthn.deriveKeyPrf", credentialId);
            if (string.IsNullOrEmpty(key)) return BiometricUpgradeOutcome.Failed;
            derivedKey = key;

            var stored = await _storage.GetAsync(BioKey);
            if (stored == null) return BiometricUpgradeOutcome.Failed;
            oldKey = stored;

            foreach (var storageKey in await _storage.GetKeysWithPrefixAsync(BioSeedPrefix))
            {
                var encrypted = await _storage.GetAsync(storageKey);
                if (encrypted == null) continue;
                var plain = await _js.InvokeAsync<string?>("dgbWebAuthn.decryptSeed", oldKey, encrypted);
                if (plain == null) continue; // cannot be opened with the stored key: it re-enrols after the next credential unlock
                reprotected[storageKey] = await _js.InvokeAsync<string>("dgbWebAuthn.encryptSeed", derivedKey, plain);
            }
        }
        catch
        {
            return BiometricUpgradeOutcome.Failed;
        }

        // Everything is ready in memory. Write the new copies, switch over, and drop the stored key last.
        foreach (var (storageKey, value) in reprotected)
            await _storage.SetAsync(storageKey, value);
        await _storage.SetAsync(BioCred, credentialId);
        await _storage.SetAsync(BioPrfState, StatePrf);
        await _storage.RemoveAsync(BioKey);
        await _storage.RemoveAsync(BioWeakWarned);
        return BiometricUpgradeOutcome.Upgraded;
    }

    /// <summary>
    /// True once for a user whose biometric unlock keeps the key next to the ciphertext (no PRF here, or they
    /// declined the upgrade): show the plain warning that biometrics lowers protection against copied browser data.
    /// </summary>
    public async Task<bool> NeedsWeakWarningAsync() =>
        await IsEnabledAsync()
        && await _storage.GetAsync(BioPrfState) == StateWeak
        && await _storage.GetAsync(BioWeakWarned) != "true";

    public Task AcknowledgeWeakWarningAsync() => _storage.SetAsync(BioWeakWarned, "true");

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
        await _storage.RemoveAsync(BioPrfState);
        await _storage.RemoveAsync(BioWeakWarned);
        _pendingWeakCredentialId = null;

        // Remove all per-wallet seeds
        var seedKeys = await _storage.GetKeysWithPrefixAsync(BioSeedPrefix);
        foreach (var key in seedKeys)
            await _storage.RemoveAsync(key);
    }

    private class PrfEnrollResult
    {
        public string CredentialId { get; set; } = "";
        public bool PrfSupported { get; set; }
    }
}
