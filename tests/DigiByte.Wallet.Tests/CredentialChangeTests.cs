using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Tests;

public class CredentialChangeTests
{
    private const string Pin = MigrationHarness.Pin;
    private const string Passphrase = "violet kettle 7 orbit lantern";
    private static string L => MigrationHarness.LegacyKey();
    private static string V => MigrationHarness.V2Key();
    private static string R => MigrationHarness.RecordKey();

    [Fact]
    public async Task Pin_to_passphrase_writes_v2_with_kind_passphrase_and_deletes_legacy_immediately()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        var outcome = await h.Service.ChangeCredentialAsync("w1", Pin, Passphrase, CredentialKind.Passphrase);

        Assert.Equal(ChangeCredentialOutcome.Changed, outcome);
        var env = EnvelopeCodec.Parse(await h.Raw(V)).Envelope!;
        Assert.Equal(CredentialKind.Passphrase, env.Kind);
        Assert.Null(await h.Raw(L)); // the PIN-protected copy goes at once, not after 7 days
        Assert.Null(await h.Raw(R));
        Assert.Equal("passphrase", (await h.Service.GetWalletAsync("w1"))!.CredentialType);
    }

    [Fact]
    public async Task After_the_change_the_pin_alone_no_longer_unlocks_the_wallet()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.ChangeCredentialAsync("w1", Pin, Passphrase, CredentialKind.Passphrase);

        Assert.False(await h.Service.UnlockWalletAsync("w1", Pin));
        Assert.False(await h.Service.VerifyPinAsync("w1", Pin));
        Assert.True(await h.Service.UnlockWalletAsync("w1", Passphrase));
        Assert.Equal(MigrationHarness.Mnemonic, string.Join(' ', (await h.Service.GetSeedPhraseAsync("w1", Passphrase))!));
    }

    [Fact]
    public async Task Works_on_an_already_migrated_wallet_and_replaces_its_v2_value()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", Pin);
        var oldV2 = await h.Raw(V);

        Assert.Equal(ChangeCredentialOutcome.Changed, await h.Service.ChangeCredentialAsync("w1", Pin, Passphrase, CredentialKind.Passphrase));

        Assert.NotEqual(oldV2, await h.Raw(V));
        Assert.False(await h.Service.UnlockWalletAsync("w1", Pin));
    }

    [Fact]
    public async Task Wrong_current_credential_changes_nothing()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var before = h.Storage.Snapshot();

        var outcome = await h.Service.ChangeCredentialAsync("w1", "000000", Passphrase, CredentialKind.Passphrase);

        Assert.Equal(ChangeCredentialOutcome.WrongCredential, outcome);
        Assert.Equal(before, h.Storage.Snapshot());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public async Task A_passphrase_that_breaks_the_rules_is_refused_before_anything_is_stored(string weak)
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var before = h.Storage.Snapshot();

        var outcome = await h.Service.ChangeCredentialAsync("w1", Pin, weak, CredentialKind.Passphrase);

        Assert.Equal(ChangeCredentialOutcome.WeakPassphrase, outcome);
        Assert.Equal(before, h.Storage.Snapshot());
    }

    [Fact]
    public async Task A_new_pin_must_be_six_digits()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.Equal(ChangeCredentialOutcome.Failed, await h.Service.ChangeCredentialAsync("w1", Pin, "12ab", CredentialKind.Pin));
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));
    }

    [Fact]
    public async Task Passphrase_back_to_pin_works()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.ChangeCredentialAsync("w1", Pin, Passphrase, CredentialKind.Passphrase);

        Assert.Equal(ChangeCredentialOutcome.Changed, await h.Service.ChangeCredentialAsync("w1", Passphrase, "654321", CredentialKind.Pin));

        Assert.Equal("pin", (await h.Service.GetWalletAsync("w1"))!.CredentialType);
        Assert.True(await h.Service.UnlockWalletAsync("w1", "654321"));
        Assert.False(await h.Service.UnlockWalletAsync("w1", Passphrase));
    }

    [Fact]
    public async Task Failed_encryption_changes_nothing()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var before = h.Storage.Snapshot();
        h.Crypto.FailEncryptWith = CryptoFailure.ResourceExhausted;

        Assert.Equal(ChangeCredentialOutcome.Failed, await h.Service.ChangeCredentialAsync("w1", Pin, Passphrase, CredentialKind.Passphrase));

        Assert.Equal(before, h.Storage.Snapshot());
        h.Crypto.FailEncryptWith = null;
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));
    }

    [Fact]
    public async Task A_new_wallet_can_be_created_with_a_passphrase_and_only_v2_is_written()
    {
        var h = new MigrationHarness();
        var wallet = await h.Service.CreateWalletAsync("Fresh", MigrationHarness.Mnemonic, Passphrase, kind: CredentialKind.Passphrase);

        Assert.Equal("passphrase", wallet.CredentialType);
        Assert.Equal("standard", wallet.ProtectionLevel);
        Assert.Null(await h.Raw(MigrationHarness.LegacyKey(wallet.Id)));
        var env = EnvelopeCodec.Parse(await h.Raw(MigrationHarness.V2Key(wallet.Id))).Envelope!;
        Assert.Equal(CredentialKind.Passphrase, env.Kind);
        Assert.True(await h.Service.UnlockWalletAsync(wallet.Id, Passphrase));
        Assert.False(await h.Service.UnlockWalletAsync(wallet.Id, Pin));
    }

    [Fact]
    public async Task Creating_a_wallet_with_a_weak_passphrase_throws_and_stores_nothing()
    {
        var h = new MigrationHarness();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Service.CreateWalletAsync("Fresh", MigrationHarness.Mnemonic, "short", kind: CredentialKind.Passphrase));

        Assert.Empty(h.Storage.Snapshot());
    }

    [Fact]
    public async Task A_new_pin_wallet_is_written_as_v2_only()
    {
        var h = new MigrationHarness();
        var wallet = await h.Service.CreateWalletAsync("Fresh", MigrationHarness.Mnemonic, Pin);

        Assert.Equal("pin", wallet.CredentialType);
        Assert.Null(await h.Raw(MigrationHarness.LegacyKey(wallet.Id)));
        Assert.Equal(EnvelopeStatus.Ok, EnvelopeCodec.Parse(await h.Raw(MigrationHarness.V2Key(wallet.Id))).Status);
    }
}
