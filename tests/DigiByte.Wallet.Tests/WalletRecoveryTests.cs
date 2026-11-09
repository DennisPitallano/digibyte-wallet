using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class WalletRecoveryTests
{
    private const string OldPin = MigrationHarness.Pin;
    private const string NewPin = "654321";
    private const string Passphrase = "correct horse battery staple 42";
    private static string Phrase => MigrationHarness.Mnemonic;

    private static string L => MigrationHarness.LegacyKey();
    private static string V => MigrationHarness.V2Key();
    private static string R => MigrationHarness.RecordKey();

    [Fact]
    public async Task Reset_on_a_legacy_only_wallet_writes_v2_and_deletes_the_legacy_copy()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.True(await h.Service.ResetPinAsync("w1", Phrase, NewPin));

        Assert.Equal(EnvelopeStatus.Ok, EnvelopeCodec.Parse(await h.Raw(V)).Status);
        Assert.Null(await h.Raw(L));
        Assert.Null(await h.Raw(R));
        Assert.False(await h.Service.UnlockWalletAsync("w1", OldPin));
        Assert.True(await h.Service.UnlockWalletAsync("w1", NewPin));
    }

    [Fact]
    public async Task Reset_on_a_migrated_wallet_replaces_the_stale_v2_value_and_the_old_pin_stops_working()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", OldPin); // migrates
        var staleV2 = await h.Raw(V);

        Assert.True(await h.Service.ResetPinAsync("w1", Phrase, NewPin));

        Assert.NotEqual(staleV2, await h.Raw(V));
        Assert.Null(await h.Raw(L)); // the old, weakly protected copy goes immediately
        Assert.Null(await h.Raw(R));
        Assert.False(await h.Service.UnlockWalletAsync("w1", OldPin));
        Assert.True(await h.Service.UnlockWalletAsync("w1", NewPin));
        Assert.Equal(Phrase, string.Join(' ', (await h.Service.GetSeedPhraseAsync("w1", NewPin))!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a valid recovery phrase at all")]
    [InlineData("abandon abandon abandon")]
    public async Task Invalid_or_malformed_phrase_changes_nothing_in_storage(string phrase)
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", OldPin);
        var before = h.Storage.Snapshot();

        Assert.False(await h.Service.ResetPinAsync("w1", phrase, NewPin));

        Assert.Equal(before, h.Storage.Snapshot());
        Assert.True(await h.Service.UnlockWalletAsync("w1", OldPin));
    }

    [Fact]
    public async Task Failed_encryption_changes_nothing_and_does_not_report_success()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var before = h.Storage.Snapshot();
        h.Crypto.FailEncryptWith = CryptoFailure.ResourceExhausted;

        Assert.False(await h.Service.ResetPinAsync("w1", Phrase, NewPin));

        Assert.Equal(before, h.Storage.Snapshot());
    }

    [Fact]
    public async Task Unverifiable_new_value_changes_nothing()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var before = h.Storage.Snapshot();
        h.Crypto.CorruptNextEncrypt = true; // the new envelope opens to different plaintext

        Assert.False(await h.Service.ResetPinAsync("w1", Phrase, NewPin));

        Assert.Equal(before, h.Storage.Snapshot());
    }

    [Fact]
    public async Task A_storage_failure_while_writing_leaves_the_old_credential_working()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        h.Storage.FailWritesToKeyContaining = "wallet_seed_v2_";

        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.ResetPinAsync("w1", Phrase, NewPin));

        h.Storage.FailWritesToKeyContaining = null;
        Assert.True(await h.Service.UnlockWalletAsync("w1", OldPin));
        Assert.False(await h.Service.UnlockWalletAsync("w1", NewPin));
    }

    [Fact]
    public async Task A_passphrase_wallet_can_switch_to_a_pin_and_back()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.True(await h.Service.ResetPinAsync("w1", Phrase, Passphrase, CredentialKind.Passphrase));
        Assert.Equal("passphrase", (await h.Service.GetWalletAsync("w1"))!.CredentialType);
        Assert.Equal(CredentialKind.Passphrase, EnvelopeCodec.Parse(await h.Raw(V)).Envelope!.Kind);
        Assert.True(await h.Service.UnlockWalletAsync("w1", Passphrase));
        Assert.False(await h.Service.UnlockWalletAsync("w1", OldPin));

        Assert.True(await h.Service.ResetPinAsync("w1", Phrase, NewPin, CredentialKind.Pin));
        Assert.Equal("pin", (await h.Service.GetWalletAsync("w1"))!.CredentialType);
        Assert.Equal(CredentialKind.Pin, EnvelopeCodec.Parse(await h.Raw(V)).Envelope!.Kind);
        Assert.True(await h.Service.UnlockWalletAsync("w1", NewPin));
        Assert.False(await h.Service.UnlockWalletAsync("w1", Passphrase));
    }

    [Fact]
    public async Task A_passphrase_wallet_can_choose_a_different_passphrase()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.ResetPinAsync("w1", Phrase, Passphrase, CredentialKind.Passphrase);

        Assert.True(await h.Service.ResetPinAsync("w1", Phrase, "another passphrase that is long", CredentialKind.Passphrase));

        Assert.False(await h.Service.UnlockWalletAsync("w1", Passphrase));
        Assert.True(await h.Service.UnlockWalletAsync("w1", "another passphrase that is long"));
    }

    [Fact]
    public async Task Imported_key_wallet_recovers_by_re_importing_the_key()
    {
        var h = new MigrationHarness();
        var wif = new Key().GetWif(DigiByteNetwork.Mainnet).ToString();
        await h.AddLegacyWalletAsync(walletType: "privatekey", secret: wif);

        Assert.False(await h.Service.ResetPinAsync("w1", "not a key", NewPin));
        Assert.True(await h.Service.ResetPinAsync("w1", wif, NewPin));

        Assert.True(await h.Service.UnlockWalletAsync("w1", NewPin));
        Assert.False(await h.Service.UnlockWalletAsync("w1", OldPin));
    }

    [Fact]
    public async Task Watch_only_wallet_recovers_by_re_importing_the_xpub()
    {
        var h = new MigrationHarness();
        var xpub = new HdKeyDerivation(new Mnemonic(Wordlist.English, WordCount.Twelve), network: DigiByteNetwork.Mainnet)
            .GetAccountExtPubKey().ToString(DigiByteNetwork.Mainnet);
        await h.AddLegacyWalletAsync(walletType: "xpub", secret: xpub);

        Assert.False(await h.Service.ResetPinAsync("w1", "notanxpub", NewPin));
        Assert.True(await h.Service.ResetPinAsync("w1", xpub, NewPin));

        Assert.True(await h.Service.UnlockWalletAsync("w1", NewPin));
    }

    [Fact]
    public async Task Unknown_wallet_changes_nothing()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var before = h.Storage.Snapshot();

        Assert.False(await h.Service.ResetPinAsync("missing", Phrase, NewPin));

        Assert.Equal(before, h.Storage.Snapshot());
    }
}
