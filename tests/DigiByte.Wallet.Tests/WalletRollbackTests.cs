using System.Text.Json;

namespace DigiByte.Wallet.Tests;

public class WalletRollbackTests
{
    private const string Pin = MigrationHarness.Pin;
    private static string L => MigrationHarness.LegacyKey();
    private static string V => MigrationHarness.V2Key();
    private static string R => MigrationHarness.RecordKey();

    private static async Task<MigrationHarness> MigratedAsync()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", Pin);
        return h;
    }

    [Fact]
    public async Task Legacy_copy_is_kept_before_seven_days()
    {
        var h = await MigratedAsync();

        h.Clock.Advance(TimeSpan.FromDays(6) + TimeSpan.FromHours(23));
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.NotNull(await h.Raw(L));
    }

    [Fact]
    public async Task Legacy_copy_is_removed_on_the_first_v2_unlock_at_or_after_seven_days_and_stamped()
    {
        var h = await MigratedAsync();

        h.Clock.Advance(TimeSpan.FromDays(7));
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Null(await h.Raw(L));
        Assert.NotNull(await h.Raw(V));
        using var record = JsonDocument.Parse((await h.Raw(R))!);
        Assert.True(record.RootElement.TryGetProperty("LegacyRemovedAt", out var removed));
        Assert.NotEqual(JsonValueKind.Null, removed.ValueKind);
        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin)); // and it keeps working from v2 alone
    }

    [Fact]
    public async Task A_wrong_pin_after_seven_days_does_not_remove_the_legacy_copy()
    {
        var h = await MigratedAsync();

        h.Clock.Advance(TimeSpan.FromDays(10));
        Assert.False(await h.Service.UnlockWalletAsync("w1", "000000"));

        Assert.NotNull(await h.Raw(L));
    }

    [Fact]
    public async Task Legacy_changed_by_an_older_version_after_migration_becomes_authoritative_and_re_migrates()
    {
        var h = await MigratedAsync();
        var oldV2 = await h.Raw(V);
        // An older app version changes the PIN: it only knows (and rewrites) wallet_seed_{id}.
        const string newPin = "654321";
        await h.Storage.SetAsync(L, EnvelopeTestCrypto.LegacyString(MigrationHarness.Mnemonic, newPin));

        Assert.False(await h.Service.UnlockWalletAsync("w1", Pin)); // the old PIN no longer works anywhere
        Assert.True(await h.Service.UnlockWalletAsync("w1", newPin));

        var newV2 = await h.Raw(V);
        Assert.NotEqual(oldV2, newV2);
        Assert.True(await h.Service.UnlockWalletAsync("w1", newPin));
        Assert.False(await h.Service.UnlockWalletAsync("w1", Pin));
    }

    [Fact]
    public async Task Rollback_to_an_older_version_still_works_during_the_window()
    {
        var h = await MigratedAsync();

        // The older version reads only the untouched legacy value with the original PIN.
        var legacy = Convert.FromBase64String((await h.Raw(L))!);
        Assert.NotNull(EnvelopeTestCrypto.LegacyOpen(legacy, Pin));
    }
}
