using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class WalletMigrationTests
{
    private const string Pin = MigrationHarness.Pin;
    private static string L(string id = MigrationHarness.WalletId) => MigrationHarness.LegacyKey(id);
    private static string V(string id = MigrationHarness.WalletId) => MigrationHarness.V2Key(id);
    private static string R(string id = MigrationHarness.WalletId) => MigrationHarness.RecordKey(id);

    [Fact]
    public async Task Legacy_wallet_unlocks_and_is_migrated_to_v2_with_a_record()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var legacyBefore = await h.Raw(L());

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        var v2 = await h.Raw(V());
        Assert.NotNull(v2);
        Assert.Equal(EnvelopeStatus.Ok, EnvelopeCodec.Parse(v2).Status);
        Assert.NotNull(await h.Raw(R()));
        Assert.Equal(legacyBefore, await h.Raw(L())); // byte-identical, never modified by migration
    }

    [Fact]
    public async Task Upgrade_notice_is_offered_once_after_a_migration_and_never_otherwise()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.False(h.Service.TakeMigrationNotice("w1"));
        await h.Service.UnlockWalletAsync("w1", Pin);
        Assert.True(h.Service.TakeMigrationNotice("w1"));
        Assert.False(h.Service.TakeMigrationNotice("w1"));

        await h.Service.UnlockWalletAsync("w1", Pin); // already migrated: nothing to announce
        Assert.False(h.Service.TakeMigrationNotice("w1"));
    }

    [Fact]
    public async Task Migrated_wallet_still_unlocks_with_the_same_pin_and_returns_the_same_phrase()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", Pin);

        var phrase = await h.Service.GetSeedPhraseAsync("w1", Pin);

        Assert.Equal(MigrationHarness.Mnemonic, string.Join(' ', phrase!));
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));
        Assert.False(await h.Service.UnlockWalletAsync("w1", "000000"));
    }

    [Fact]
    public async Task Verify_pin_and_decrypted_seed_paths_also_migrate()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.True(await h.Service.VerifyPinAsync("w1", Pin));

        Assert.NotNull(await h.Raw(V()));
    }

    [Fact]
    public async Task Wrong_pin_never_migrates()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.False(await h.Service.UnlockWalletAsync("w1", "000000"));

        Assert.Null(await h.Raw(V()));
        Assert.Null(await h.Raw(R()));
    }

    [Fact]
    public async Task Verification_mismatch_deletes_v2_and_leaves_legacy_usable()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        h.Crypto.CorruptNextEncrypt = true; // the new envelope opens to different plaintext

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Null(await h.Raw(V()));
        Assert.Null(await h.Raw(R()));
        Assert.NotNull(await h.Raw(L()));
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin)); // and the retry then succeeds
        Assert.NotNull(await h.Raw(V()));
    }

    [Theory]
    [InlineData("lock-throws")]
    [InlineData("lock-unavailable")]
    [InlineData("encrypt-fails")]
    [InlineData("encrypt-exhausted")]
    [InlineData("write-v2-quota")]
    [InlineData("read-back-throws")]
    [InlineData("write-record-quota")]
    public async Task Fault_at_each_migration_step_leaves_the_wallet_unlockable_and_retries_next_time(string fault)
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        var legacyBefore = await h.Raw(L());

        switch (fault)
        {
            case "lock-throws": h.Lock.ThrowOnAcquire = true; break;
            case "lock-unavailable": h.Lock.Unavailable = true; break;
            case "encrypt-fails": h.Crypto.FailEncryptWith = CryptoFailure.Unreadable; break;
            case "encrypt-exhausted": h.Crypto.FailEncryptWith = CryptoFailure.ResourceExhausted; break;
            case "write-v2-quota": h.Storage.FailWritesToKeyContaining = "wallet_seed_v2_"; break;
            case "read-back-throws": h.Crypto.ThrowOnTypedDecryptAfter = 0; break;
            case "write-record-quota": h.Storage.FailWritesToKeyContaining = "wallet_migration_"; break;
        }

        // The unlock itself must still succeed: migration problems are silent and never block it.
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Equal(legacyBefore, await h.Raw(L()));
        Assert.Null(await h.Raw(R())); // a record only exists for a verified migration
        // Whatever v2 was left behind is not trusted without a record: legacy stays authoritative.
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));
        Assert.Equal(MigrationHarness.Mnemonic, string.Join(' ', (await h.Service.GetSeedPhraseAsync("w1", Pin))!));

        // Clear the fault: the next unlock migrates.
        h.Lock.ThrowOnAcquire = false;
        h.Lock.Unavailable = false;
        h.Crypto.FailEncryptWith = null;
        h.Crypto.ThrowOnTypedDecryptAfter = null;
        h.Storage.FailWritesToKeyContaining = null;
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));
        Assert.NotNull(await h.Raw(V()));
        Assert.NotNull(await h.Raw(R()));
    }

    [Fact]
    public async Task Every_wallet_migrates_independently_with_mixed_formats()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync("a");
        await h.AddLegacyWalletAsync("b");

        Assert.True(await h.Service.UnlockWalletAsync("a", Pin));

        Assert.NotNull(await h.Raw(V("a")));
        Assert.Null(await h.Raw(V("b"))); // untouched until it is unlocked
        Assert.True(await h.Service.UnlockWalletAsync("b", Pin)); // still usable in legacy form
        Assert.NotNull(await h.Raw(V("b")));
    }

    [Fact]
    public async Task Xpub_wallet_migrates()
    {
        var h = new MigrationHarness();
        var xpub = new HdKeyDerivation(new Mnemonic(Wordlist.English, WordCount.Twelve), network: DigiByteNetwork.Mainnet)
            .GetAccountExtPubKey().ToString(DigiByteNetwork.Mainnet);
        await h.AddLegacyWalletAsync(walletType: "xpub", secret: xpub);

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.NotNull(await h.Raw(V()));
        Assert.Equal("xpub", h.Service.ActiveWallet?.WalletType);
    }

    [Fact]
    public async Task Imported_key_wallet_migrates()
    {
        var h = new MigrationHarness();
        var wif = new Key().GetWif(DigiByteNetwork.Mainnet).ToString();
        await h.AddLegacyWalletAsync(walletType: "privatekey", secret: wif);

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.NotNull(await h.Raw(V()));
        Assert.Equal("privatekey", h.Service.ActiveWallet?.WalletType);
    }

    [Fact]
    public async Task Passphrase_wallet_migrates_with_a_passphrase_envelope()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync(pin: "a long passphrase credential");
        var info = await h.Service.GetWalletAsync("w1");
        info!.CredentialType = "passphrase";
        await h.Storage.SetAsync("wallet_info_w1", System.Text.Json.JsonSerializer.Serialize(info));

        Assert.True(await h.Service.UnlockWalletAsync("w1", "a long passphrase credential"));

        var parsed = EnvelopeCodec.Parse(await h.Raw(V()));
        Assert.Equal(CredentialKind.Passphrase, parsed.Envelope!.Kind);
    }

    [Fact]
    public async Task A_wallet_with_a_v2_value_and_no_legacy_copy_unlocks_from_v2()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", Pin);
        await h.Storage.RemoveAsync(L());

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));
        Assert.False(await h.Service.UnlockWalletAsync("w1", "000000"));
    }

    [Fact]
    public async Task Unsupported_newer_format_is_refused_without_changing_anything()
    {
        var h = new MigrationHarness();
        await h.Storage.SetAsync(V(), "dgb3$AAAAAAAAAAAAAAAA");
        await h.Storage.SetAsync("wallet_info_w1",
            System.Text.Json.JsonSerializer.Serialize(new DigiByte.Wallet.Models.WalletInfo { Id = "w1", Name = "N", CreatedAt = DateTime.UtcNow }));
        var before = h.Storage.Snapshot();

        Assert.False(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Equal(before, h.Storage.Snapshot());
    }
}
