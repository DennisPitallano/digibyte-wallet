using System.Security.Cryptography;
using System.Text;
using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Biometric key protection (FR-018 to FR-018b, SC-011): where the browser supports the WebAuthn PRF extension the key
/// that protects the biometric seed copies is derived at unlock and never stored; where it does not, enrolment needs
/// the user's consent and the warning is shown once.
/// </summary>
public class BiometricPrfTests
{
    private const string Wallet1 = "wallet-1";
    private const string Wallet2 = "wallet-2";
    private static readonly byte[] Seed1 = Encoding.UTF8.GetBytes("seed one words");
    private static readonly byte[] Seed2 = Encoding.UTF8.GetBytes("seed two words");

    private static (BiometricService Service, FaultyStorage Storage, ScriptedWebAuthn Js) Create(Action<ScriptedWebAuthn>? configure = null)
    {
        var storage = new FaultyStorage();
        var js = new ScriptedWebAuthn();
        configure?.Invoke(js);
        return (new BiometricService(js, storage), storage, js);
    }

    private static async Task<bool> ContainsAnywhereAsync(FaultyStorage storage, string needle) =>
        storage.Snapshot().Any(kv => kv.Key.Contains(needle, StringComparison.Ordinal) || kv.Value.Contains(needle, StringComparison.Ordinal));

    // ---------- enrolment ----------

    [Fact]
    public async Task Enrolling_with_PRF_stores_no_key_and_marks_the_wallet_set_as_prf()
    {
        var (svc, storage, js) = Create();

        var outcome = await svc.EnrollAsync(Wallet1, "My Wallet", Seed1);

        Assert.Equal(BiometricEnrollOutcome.Enrolled, outcome);
        var data = storage.Snapshot();
        Assert.False(data.ContainsKey("bio_key"));
        Assert.Equal("enrolled-prf", data["bio_prf_state"]);
        Assert.Equal("true", data["bio_enabled"]);
        Assert.Equal(js.NewCredentialId, data["bio_cred"]);
        Assert.True(data.ContainsKey("bio_seed_" + Wallet1));
        Assert.False(await ContainsAnywhereAsync(storage, js.DerivedKey)); // the derived key itself is never written
        Assert.False(data.ContainsKey("bio_weak_warned"));
    }

    [Fact]
    public async Task The_seed_copy_only_opens_with_the_prf_derived_key()
    {
        var (svc, storage, js) = Create();
        await svc.EnrollAsync(Wallet1, "My Wallet", Seed1);

        Assert.Equal(Seed1, await svc.UnlockAsync(Wallet1));
        Assert.Contains("dgbWebAuthn.deriveKeyPrf", js.Calls);
        Assert.DoesNotContain("dgbWebAuthn.authenticate", js.Calls);

        // Copied storage on another profile: no biometric check means no key, so no seed (SC-011).
        js.DeriveFails = true;
        Assert.Null(await svc.UnlockAsync(Wallet1));
    }

    [Fact]
    public async Task Without_PRF_enrolment_needs_consent_and_stores_nothing_until_given()
    {
        var (svc, storage, js) = Create(j => j.PrfSupported = false);

        var outcome = await svc.EnrollAsync(Wallet1, "My Wallet", Seed1);

        Assert.Equal(BiometricEnrollOutcome.NeedsWeakConsent, outcome);
        Assert.Empty(storage.Snapshot());
    }

    [Fact]
    public async Task Without_PRF_and_with_consent_the_weak_path_is_used_and_the_warning_is_recorded()
    {
        var (svc, storage, js) = Create(j => j.PrfSupported = false);

        var outcome = await svc.EnrollAsync(Wallet1, "My Wallet", Seed1, acceptWeak: true);

        Assert.Equal(BiometricEnrollOutcome.Enrolled, outcome);
        var data = storage.Snapshot();
        Assert.Equal("enrolled-weak", data["bio_prf_state"]);
        Assert.Equal("true", data["bio_weak_warned"]);
        Assert.True(data.ContainsKey("bio_key"));
        Assert.Equal(Seed1, await svc.UnlockAsync(Wallet1));
        Assert.Contains("dgbWebAuthn.authenticate", js.Calls);
    }

    [Fact]
    public async Task A_cancelled_or_failed_enrolment_changes_nothing()
    {
        var (svc, storage, _) = Create(j => j.EnrollReturnsNull = true);

        Assert.Equal(BiometricEnrollOutcome.Failed, await svc.EnrollAsync(Wallet1, "My Wallet", Seed1));
        Assert.Empty(storage.Snapshot());
    }

    [Fact]
    public async Task If_the_key_cannot_be_derived_after_creating_the_credential_nothing_is_stored()
    {
        var (svc, storage, _) = Create(j => j.DeriveFails = true);

        Assert.Equal(BiometricEnrollOutcome.Failed, await svc.EnrollAsync(Wallet1, "My Wallet", Seed1));
        Assert.Empty(storage.Snapshot());
    }

    [Fact]
    public async Task Another_wallet_can_be_added_in_prf_mode_without_any_stored_key()
    {
        var (svc, storage, _) = Create();
        await svc.EnrollAsync(Wallet1, "My Wallet", Seed1);

        Assert.True(await svc.AddWalletSeedAsync(Wallet2, Seed2));

        Assert.False(storage.Snapshot().ContainsKey("bio_key"));
        Assert.Equal(Seed2, await svc.UnlockAsync(Wallet2));
    }

    // ---------- upgrading an existing enrolment ----------

    private static async Task<(BiometricService Service, FaultyStorage Storage, ScriptedWebAuthn Js)> LegacyEnrolmentAsync(Action<ScriptedWebAuthn>? configure = null)
    {
        var (svc, storage, js) = Create(configure);
        // How the app stored a biometric enrolment before this feature: a random key next to every ciphertext.
        await storage.SetAsync("bio_enabled", "true");
        await storage.SetAsync("bio_cred", "old-cred");
        await storage.SetAsync("bio_key", "old-bio-key");
        await storage.SetAsync("bio_seed_" + Wallet1, ScriptedWebAuthn.Seal("old-bio-key", Seed1));
        await storage.SetAsync("bio_seed_" + Wallet2, ScriptedWebAuthn.Seal("old-bio-key", Seed2));
        return (svc, storage, js);
    }

    [Fact]
    public async Task An_existing_enrolment_is_offered_the_upgrade_once()
    {
        var (svc, _, _) = await LegacyEnrolmentAsync();

        Assert.True(await svc.NeedsUpgradePromptAsync());

        await svc.DeclineUpgradeAsync();
        Assert.False(await svc.NeedsUpgradePromptAsync());
    }

    [Fact]
    public async Task Upgrading_re_protects_every_seed_copy_under_the_prf_key_and_removes_the_stored_key()
    {
        var (svc, storage, js) = await LegacyEnrolmentAsync();

        var outcome = await svc.UpgradeAsync("My Wallet");

        Assert.Equal(BiometricUpgradeOutcome.Upgraded, outcome);
        var data = storage.Snapshot();
        Assert.False(data.ContainsKey("bio_key"));
        Assert.Equal("enrolled-prf", data["bio_prf_state"]);
        Assert.Equal(js.NewCredentialId, data["bio_cred"]);
        Assert.Equal(Seed1, await svc.UnlockAsync(Wallet1));
        Assert.Equal(Seed2, await svc.UnlockAsync(Wallet2));
        Assert.False(await svc.NeedsUpgradePromptAsync());
        Assert.False(await ContainsAnywhereAsync(storage, "old-bio-key"));
    }

    [Fact]
    public async Task When_PRF_is_not_available_the_upgrade_changes_nothing_and_the_warning_is_due_once()
    {
        var (svc, storage, _) = await LegacyEnrolmentAsync(j => j.PrfSupported = false);
        var before = storage.Snapshot();

        var outcome = await svc.UpgradeAsync("My Wallet");

        Assert.Equal(BiometricUpgradeOutcome.NotSupported, outcome);
        Assert.True(await svc.NeedsWeakWarningAsync());
        var after = storage.Snapshot();
        Assert.Equal(before["bio_key"], after["bio_key"]);
        Assert.Equal(before["bio_cred"], after["bio_cred"]);
        Assert.Equal(before["bio_seed_" + Wallet1], after["bio_seed_" + Wallet1]);

        await svc.AcknowledgeWeakWarningAsync();
        Assert.False(await svc.NeedsWeakWarningAsync()); // shown once
        Assert.Equal(Seed1, await svc.UnlockAsync(Wallet1)); // and biometric keeps working the old way
    }

    [Fact]
    public async Task A_failed_upgrade_leaves_the_existing_enrolment_untouched()
    {
        var (svc, storage, _) = await LegacyEnrolmentAsync(j => j.DeriveFails = true);
        var before = storage.Snapshot();

        Assert.Equal(BiometricUpgradeOutcome.Failed, await svc.UpgradeAsync("My Wallet"));

        Assert.Equal(before, storage.Snapshot());
    }

    [Fact]
    public async Task A_storage_failure_while_upgrading_keeps_the_old_key_so_pin_unlock_and_re_enrolment_still_work()
    {
        var (svc, storage, _) = await LegacyEnrolmentAsync();
        storage.FailWritesToKeyContaining = "bio_prf_state";

        await Assert.ThrowsAnyAsync<Exception>(() => svc.UpgradeAsync("My Wallet"));

        Assert.True(storage.Snapshot().ContainsKey("bio_key")); // removed only after everything else succeeded
    }

    [Fact]
    public async Task Declining_the_upgrade_keeps_the_old_behaviour_and_asks_for_the_warning_once()
    {
        var (svc, _, js) = await LegacyEnrolmentAsync();

        await svc.DeclineUpgradeAsync();

        Assert.True(await svc.NeedsWeakWarningAsync());
        Assert.Equal(Seed1, await svc.UnlockAsync(Wallet1));
        Assert.Contains("dgbWebAuthn.authenticate", js.Calls);
    }

    [Fact]
    public async Task A_prf_enrolment_never_asks_for_the_upgrade_or_the_warning()
    {
        var (svc, _, _) = Create();
        await svc.EnrollAsync(Wallet1, "My Wallet", Seed1);

        Assert.False(await svc.NeedsUpgradePromptAsync());
        Assert.False(await svc.NeedsWeakWarningAsync());
    }

    [Fact]
    public async Task Nothing_is_offered_when_biometrics_is_not_enrolled()
    {
        var (svc, _, _) = Create();

        Assert.False(await svc.NeedsUpgradePromptAsync());
        Assert.False(await svc.NeedsWeakWarningAsync());
    }

    [Fact]
    public async Task Disabling_removes_the_prf_and_warning_state_too()
    {
        var (svc, storage, _) = Create(j => j.PrfSupported = false);
        await svc.EnrollAsync(Wallet1, "My Wallet", Seed1, acceptWeak: true);

        await svc.DisableAsync();

        Assert.Empty(storage.Snapshot());
    }

    // ---------- biometrics and the wallet credential ----------

    [Fact]
    public async Task Biometric_unlock_keeps_working_after_the_wallet_credential_is_changed()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var svc = new BiometricService(new ScriptedWebAuthn(), h.Storage);

        var seed = await h.Service.GetDecryptedSeedAsync("w1", MigrationHarness.Pin);
        Assert.Equal(BiometricEnrollOutcome.Enrolled, await svc.EnrollAsync("w1", "Legacy", seed!));

        await h.Service.ChangeCredentialAsync("w1", MigrationHarness.Pin, "violet kettle 7 orbit lantern", CredentialKind.Passphrase);

        var viaBiometric = await svc.UnlockAsync("w1");
        Assert.Equal(Encoding.UTF8.GetBytes(MigrationHarness.Mnemonic), viaBiometric);
    }

    [Fact]
    public async Task Biometric_unlock_survives_migration_of_the_wallet_to_the_new_format()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var svc = new BiometricService(new ScriptedWebAuthn(), h.Storage);
        var seed = await h.Service.GetDecryptedSeedAsync("w1", MigrationHarness.Pin);
        await svc.EnrollAsync("w1", "Legacy", seed!);

        await h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin); // migrates

        Assert.NotNull(await h.Raw(MigrationHarness.V2Key()));
        Assert.Equal(Encoding.UTF8.GetBytes(MigrationHarness.Mnemonic), await svc.UnlockAsync("w1"));
    }
}

/// <summary>A scripted stand-in for the browser's WebAuthn glue. Seed copies are "sealed" with a hash of the key, so the
/// tests can tell which key opened them without the key itself ever appearing in stored text.</summary>
internal sealed class ScriptedWebAuthn : IJSRuntime
{
    public bool Supported { get; set; } = true;
    public bool PrfSupported { get; set; } = true;
    public bool DeriveFails { get; set; }
    public bool EnrollReturnsNull { get; set; }
    public string NewCredentialId { get; } = "new-prf-cred";
    public string DerivedKey { get; } = "prf-derived-key-material";
    public List<string> Calls { get; } = new();

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

    public static string Seal(string key, byte[] seed) => $"sealed:{Hash(key)}:{Convert.ToBase64String(seed)}";

    private static string? Open(string key, string sealedText)
    {
        var prefix = $"sealed:{Hash(key)}:";
        return sealedText.StartsWith(prefix, StringComparison.Ordinal) ? sealedText[prefix.Length..] : null;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add(identifier);
        object? result = identifier switch
        {
            "dgbWebAuthn.isSupported" => Supported,
            "dgbWebAuthn.enrollPrf" => EnrollReturnsNull ? null : new Dictionary<string, object?>
            {
                ["credentialId"] = NewCredentialId,
                ["prfSupported"] = PrfSupported,
            },
            "dgbWebAuthn.deriveKeyPrf" => DeriveFails || !PrfSupported ? null : DerivedKey,
            "dgbWebAuthn.newBioKey" => "weak-random-key",
            "dgbWebAuthn.encryptSeed" => Seal((string)args![0]!, Convert.FromBase64String((string)args[1]!)),
            "dgbWebAuthn.decryptSeed" => Open((string)args![0]!, (string)args[1]!),
            "dgbWebAuthn.authenticate" => Open((string)args![1]!, (string)args[2]!),
            "dgbWebAuthn.verifyIdentity" => true,
            _ => default(TValue),
        };

        // The service reads the enrol result as a small DTO; round-trip our dictionary through JSON like interop does.
        if (result is Dictionary<string, object?> dict)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(dict);
            return ValueTask.FromResult(System.Text.Json.JsonSerializer.Deserialize<TValue>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
        }
        return ValueTask.FromResult((TValue)result!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
}
