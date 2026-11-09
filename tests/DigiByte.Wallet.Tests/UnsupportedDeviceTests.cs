using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Devices and browsers that cannot run the memory-hard protection (FR-021, SC-013): a new wallet is created with the
/// older protection only after an explicit agreement, is marked as such, and upgrades by itself on the first unlock on
/// a capable device. Nothing is ever silently weakened and nothing is written below the floor.
/// </summary>
public class UnsupportedDeviceTests
{
    private const string Pin = MigrationHarness.Pin;
    private const string Mnemonic = MigrationHarness.Mnemonic;

    [Theory]
    [InlineData(CryptoFailure.Unsupported)]
    [InlineData(CryptoFailure.ResourceExhausted)]
    public async Task A_new_wallet_needs_an_explicit_agreement_when_the_protection_cannot_run(CryptoFailure why)
    {
        var h = new MigrationHarness();
        h.Crypto.FailEncryptWith = why;

        var thrown = await Assert.ThrowsAsync<LegacyProtectionConsentRequiredException>(() =>
            h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin));

        Assert.Equal(why, thrown.Reason);
        Assert.Empty(h.Storage.Snapshot()); // nothing is stored without the agreement
    }

    [Fact]
    public async Task With_the_agreement_the_wallet_is_created_with_legacy_protection_and_marked_as_such()
    {
        var h = new MigrationHarness();
        h.Crypto.FailEncryptWith = CryptoFailure.Unsupported;

        var wallet = await h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin, allowLegacyProtection: true);

        Assert.Equal("legacy", wallet.ProtectionLevel);
        Assert.NotNull(await h.Raw(MigrationHarness.LegacyKey(wallet.Id)));
        Assert.Null(await h.Raw(MigrationHarness.V2Key(wallet.Id)));
        var stored = await h.Service.GetWalletAsync(wallet.Id);
        Assert.Equal("legacy", stored!.ProtectionLevel);
        Assert.True(await h.Service.UnlockWalletAsync(wallet.Id, Pin)); // and it works
        Assert.Equal(Mnemonic, string.Join(' ', (await h.Service.GetSeedPhraseAsync(wallet.Id, Pin))!));
    }

    [Fact]
    public async Task A_supported_device_never_asks_for_the_agreement_and_is_standard()
    {
        var h = new MigrationHarness();

        var wallet = await h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin);

        Assert.Equal("standard", wallet.ProtectionLevel);
        Assert.NotNull(await h.Raw(MigrationHarness.V2Key(wallet.Id)));
    }

    [Fact]
    public async Task A_legacy_protection_wallet_upgrades_on_the_first_unlock_where_the_device_is_capable()
    {
        var h = new MigrationHarness();
        h.Crypto.FailEncryptWith = CryptoFailure.Unsupported;
        var wallet = await h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin, allowLegacyProtection: true);

        // Still on the incapable device: unlocking works and nothing is forced.
        Assert.True(await h.Service.UnlockWalletAsync(wallet.Id, Pin));
        Assert.Null(await h.Raw(MigrationHarness.V2Key(wallet.Id)));
        Assert.Equal("legacy", (await h.Service.GetWalletAsync(wallet.Id))!.ProtectionLevel);

        // The same storage opened on a capable device or browser.
        h.Crypto.FailEncryptWith = null;
        Assert.True(await h.Service.UnlockWalletAsync(wallet.Id, Pin));

        Assert.Equal(EnvelopeStatus.Ok, EnvelopeCodec.Parse(await h.Raw(MigrationHarness.V2Key(wallet.Id))).Status);
        Assert.Equal("standard", (await h.Service.GetWalletAsync(wallet.Id))!.ProtectionLevel);
        Assert.Equal("standard", h.Service.ActiveWallet!.ProtectionLevel);
    }

    [Fact]
    public async Task Migration_on_a_device_that_cannot_allocate_even_the_floor_leaves_the_wallet_as_it_is_and_says_why()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var legacy = await h.Raw(MigrationHarness.LegacyKey());
        h.Crypto.FailEncryptWith = CryptoFailure.ResourceExhausted;

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin)); // never blocks the unlock

        Assert.Equal(legacy, await h.Raw(MigrationHarness.LegacyKey()));
        Assert.Null(await h.Raw(MigrationHarness.V2Key()));
        Assert.Null(await h.Raw(MigrationHarness.RecordKey()));
        Assert.Equal(CryptoFailure.ResourceExhausted, h.Service.TakeMigrationDeferred("w1")); // so the page can explain
        Assert.Null(h.Service.TakeMigrationDeferred("w1")); // once
    }

    [Fact]
    public async Task An_unsupported_browser_is_reported_as_the_reason_for_a_deferred_migration()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        h.Crypto.FailEncryptWith = CryptoFailure.Unsupported;

        await h.Service.UnlockWalletAsync("w1", Pin);

        Assert.Equal(CryptoFailure.Unsupported, h.Service.TakeMigrationDeferred("w1"));
    }

    [Fact]
    public async Task No_deferred_note_when_migration_simply_succeeds()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        await h.Service.UnlockWalletAsync("w1", Pin);

        Assert.Null(h.Service.TakeMigrationDeferred("w1"));
    }

    [Fact]
    public async Task An_existing_standard_wallet_is_never_downgraded_when_the_device_cannot_run_the_protection()
    {
        var h = new MigrationHarness();
        var wallet = await h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin); // standard, v2 only
        var before = h.Storage.Snapshot();
        h.Crypto.FailEncryptWith = CryptoFailure.ResourceExhausted;

        var outcome = await h.Service.ChangeCredentialAsync(wallet.Id, Pin, "654321", CredentialKind.Pin);

        Assert.Equal(ChangeCredentialOutcome.Failed, outcome);
        Assert.Equal(before, h.Storage.Snapshot());
        Assert.Null(await h.Raw(MigrationHarness.LegacyKey(wallet.Id))); // no weaker copy was created
    }

    [Fact]
    public async Task A_wallet_that_already_agreed_to_legacy_protection_can_still_change_its_credential_there()
    {
        var h = new MigrationHarness();
        h.Crypto.FailEncryptWith = CryptoFailure.Unsupported;
        var wallet = await h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin, allowLegacyProtection: true);

        var outcome = await h.Service.ChangeCredentialAsync(wallet.Id, Pin, "654321", CredentialKind.Pin);

        Assert.Equal(ChangeCredentialOutcome.Changed, outcome);
        Assert.False(await h.Service.UnlockWalletAsync(wallet.Id, Pin));
        Assert.True(await h.Service.UnlockWalletAsync(wallet.Id, "654321"));
        Assert.Equal("legacy", (await h.Service.GetWalletAsync(wallet.Id))!.ProtectionLevel);
        Assert.Null(await h.Raw(MigrationHarness.V2Key(wallet.Id)));
    }

    [Fact]
    public async Task Recovery_on_a_standard_wallet_does_not_fall_back_to_legacy_on_an_incapable_device()
    {
        var h = new MigrationHarness();
        var wallet = await h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin);
        var before = h.Storage.Snapshot();
        h.Crypto.FailEncryptWith = CryptoFailure.Unsupported;

        Assert.False(await h.Service.ResetPinAsync(wallet.Id, Mnemonic, "654321"));

        Assert.Equal(before, h.Storage.Snapshot());
    }

    [Fact]
    public async Task A_crypto_service_that_returns_an_envelope_below_the_floor_is_never_stored()
    {
        var h = new MigrationHarness();
        h.Crypto.ReturnBelowFloorEnvelope = true;

        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.CreateWalletAsync("Fresh", Mnemonic, Pin));

        Assert.Empty(h.Storage.Snapshot());
    }

    [Fact]
    public async Task Migration_never_writes_an_envelope_below_the_floor()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        h.Crypto.ReturnBelowFloorEnvelope = true;

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Null(await h.Raw(MigrationHarness.V2Key()));
    }

    [Theory]
    [InlineData(CredentialKind.Pin, "PIN")]
    [InlineData(CredentialKind.Passphrase, "passphrase")]
    public void An_unsupported_browser_is_explained_and_not_counted(CredentialKind kind, string _)
    {
        var message = CredentialMessages.ForFailure(kind, CryptoFailure.Unsupported);

        Assert.Contains("browser", message);
        Assert.Contains("did not count", message);
        Assert.DoesNotContain("Incorrect", message);
        Assert.False(CredentialMessages.CountsAsAttempt(CryptoFailure.Unsupported));
    }

    [Theory]
    [InlineData(CryptoFailure.ResourceExhausted)]
    [InlineData(CryptoFailure.Unsupported)]
    public void The_deferred_migration_note_names_the_reason_calmly(CryptoFailure why)
    {
        var message = CredentialMessages.MigrationDeferred(why);

        Assert.Contains("works as before", message);
        Assert.Contains("try again", message);
        Assert.DoesNotContain("Incorrect", message);
    }
}
