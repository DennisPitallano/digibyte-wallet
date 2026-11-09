using DigiByte.Wallet.Services;
using System.Reflection;

namespace DigiByte.Wallet.Tests;

public class UpgradeNudgeTests
{
    private const string Pin = MigrationHarness.Pin;

    [Fact]
    public async Task The_card_is_shown_for_a_pin_wallet_until_dismissed()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.True(await h.Service.ShouldShowPassphraseNudgeAsync("w1"));
    }

    [Fact]
    public async Task Dismissing_persists_and_the_card_does_not_reappear_on_later_unlocks_or_sessions()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        await h.Service.SetPassphraseNudgeDismissedAsync("w1", true);
        await h.Service.UnlockWalletAsync("w1", Pin);
        await h.Service.UnlockWalletAsync("w1", Pin);

        Assert.False(await h.Service.ShouldShowPassphraseNudgeAsync("w1"));

        // A new session: a fresh service over the same storage still remembers.
        var again = new WalletService(new DigiByte.Wallet.Storage.WalletKeyStore(h.Storage), h.Crypto, new MigrationStubBlockchain(),
            new TransactionTracker(h.Storage));
        Assert.False(await again.ShouldShowPassphraseNudgeAsync("w1"));
    }

    [Fact]
    public async Task Reopening_from_settings_shows_it_again()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.SetPassphraseNudgeDismissedAsync("w1", true);

        await h.Service.SetPassphraseNudgeDismissedAsync("w1", false);

        Assert.True(await h.Service.ShouldShowPassphraseNudgeAsync("w1"));
    }

    [Fact]
    public async Task A_passphrase_wallet_never_shows_the_card()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.ChangeCredentialAsync("w1", Pin, "violet kettle 7 orbit lantern", DigiByte.Wallet.Storage.CredentialKind.Passphrase);

        Assert.False(await h.Service.ShouldShowPassphraseNudgeAsync("w1"));
    }

    [Fact]
    public void There_is_no_balance_based_or_recurring_trigger()
    {
        // The only inputs to the decision are the wallet's own credential type and dismissed flag (clarification: gentle nudge).
        var method = typeof(WalletService).GetMethod("ShouldShowPassphraseNudgeAsync")!;
        Assert.Equal(new[] { typeof(string) }, method.GetParameters().Select(p => p.ParameterType).ToArray());
        Assert.DoesNotContain(typeof(WalletService).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            m => m.Name.Contains("Nudge", StringComparison.Ordinal) && m.GetParameters().Any(p => p.ParameterType == typeof(long) || p.ParameterType == typeof(decimal)));
    }
}
