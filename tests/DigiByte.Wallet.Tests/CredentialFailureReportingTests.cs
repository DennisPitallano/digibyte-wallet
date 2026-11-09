using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>After an unlock or check fails, the wallet service says why, so a page can avoid calling an
/// environmental problem a wrong credential and avoid counting it as an attempt.</summary>
public class CredentialFailureReportingTests
{
    private const string Pin = MigrationHarness.Pin;

    [Fact]
    public async Task A_wrong_pin_is_reported_as_a_wrong_credential()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.False(await h.Service.UnlockWalletAsync("w1", "000000"));

        Assert.Equal(CryptoFailure.WrongCredential, h.Service.LastCredentialFailure);
    }

    [Fact]
    public async Task A_success_clears_the_failure()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();
        await h.Service.UnlockWalletAsync("w1", "000000");

        Assert.True(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Null(h.Service.LastCredentialFailure);
    }

    [Fact]
    public async Task A_newer_format_is_reported_as_unsupported_not_wrong()
    {
        var h = new MigrationHarness();
        await h.Storage.SetAsync(MigrationHarness.V2Key(), "dgb3$AAAAAAAAAAAAAAAA");
        await h.Storage.SetAsync("wallet_info_w1",
            System.Text.Json.JsonSerializer.Serialize(new DigiByte.Wallet.Models.WalletInfo { Id = "w1", Name = "N", CreatedAt = DateTime.UtcNow }));

        Assert.False(await h.Service.UnlockWalletAsync("w1", Pin));

        Assert.Equal(CryptoFailure.UnsupportedVersion, h.Service.LastCredentialFailure);
        Assert.False(CredentialMessages.CountsAsAttempt(h.Service.LastCredentialFailure));
    }

    [Fact]
    public async Task A_missing_wallet_is_reported_as_unreadable()
    {
        var h = new MigrationHarness();

        Assert.False(await h.Service.VerifyPinAsync("nope", Pin));

        Assert.Equal(CryptoFailure.Unreadable, h.Service.LastCredentialFailure);
    }

    [Fact]
    public async Task VerifyPin_and_GetDecryptedSeed_report_failures_too()
    {
        var h = new MigrationHarness();
        await h.AddLegacyWalletAsync();

        Assert.False(await h.Service.VerifyPinAsync("w1", "000000"));
        Assert.Equal(CryptoFailure.WrongCredential, h.Service.LastCredentialFailure);

        Assert.True(await h.Service.VerifyPinAsync("w1", Pin));
        Assert.Null(h.Service.LastCredentialFailure);

        Assert.Null(await h.Service.GetDecryptedSeedAsync("w1", "000000"));
        Assert.Equal(CryptoFailure.WrongCredential, h.Service.LastCredentialFailure);
    }
}
