using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Tests;

public class WalletMigrationConcurrencyTests
{
    [Fact]
    public async Task Two_simultaneous_unlocks_produce_one_valid_v2_value_and_the_wallet_still_unlocks()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        var results = await Task.WhenAll(
            h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin),
            h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin));

        Assert.All(results, Assert.True);
        var v2 = await h.Raw(MigrationHarness.V2Key());
        Assert.Equal(EnvelopeStatus.Ok, EnvelopeCodec.Parse(v2).Status);
        Assert.NotNull(await h.Raw(MigrationHarness.RecordKey()));
        Assert.Equal(1, h.Storage.Writes.Count(k => k == MigrationHarness.V2Key())); // the second one saw the record and skipped
        Assert.True(await h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin));
    }

    [Fact]
    public async Task Many_simultaneous_unlocks_never_leave_an_unlockable_wallet()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin)));

        Assert.All(results, Assert.True);
        Assert.True(await h.Service.VerifyPinAsync("w1", MigrationHarness.Pin));
    }

    [Fact]
    public async Task Migration_is_idempotent_a_second_unlock_does_not_rewrite_v2()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin);
        var v2 = await h.Raw(MigrationHarness.V2Key());

        await h.Service.UnlockWalletAsync("w1", MigrationHarness.Pin);

        Assert.Equal(v2, await h.Raw(MigrationHarness.V2Key()));
        Assert.Equal(1, h.Crypto.TypedEncryptCalls);
    }
}
