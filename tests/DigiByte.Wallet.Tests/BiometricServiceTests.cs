using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

public class BiometricServiceTests
{
    private const string WalletId = "test-wallet-001";
    private const string WalletId2 = "test-wallet-002";

    private static BiometricService CreateService(InMemorySecureStorage? storage = null, FakeJSRuntime? js = null)
    {
        return new BiometricService(js ?? new FakeJSRuntime(), storage ?? new InMemorySecureStorage());
    }

    #region IsEnabledAsync

    [Fact]
    public async Task IsEnabledAsync_ReturnsFalse_WhenNotEnrolled()
    {
        var svc = CreateService();
        Assert.False(await svc.IsEnabledAsync(WalletId));
    }

    [Fact]
    public async Task IsEnabledAsync_ReturnsTrue_WhenEnabled()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("wallet_bio_enabled_" + WalletId, "true");
        var svc = CreateService(storage);

        Assert.True(await svc.IsEnabledAsync(WalletId));
    }

    [Fact]
    public async Task IsEnabledAsync_PerWallet_Independent()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("wallet_bio_enabled_" + WalletId, "true");
        var svc = CreateService(storage);

        Assert.True(await svc.IsEnabledAsync(WalletId));
        Assert.False(await svc.IsEnabledAsync(WalletId2));
    }

    #endregion

    #region IsDismissedAsync / DismissPromptAsync

    [Fact]
    public async Task IsDismissedAsync_ReturnsFalse_ByDefault()
    {
        var svc = CreateService();
        Assert.False(await svc.IsDismissedAsync(WalletId));
    }

    [Fact]
    public async Task DismissPromptAsync_SetsDismissed()
    {
        var svc = CreateService();

        await svc.DismissPromptAsync(WalletId);

        Assert.True(await svc.IsDismissedAsync(WalletId));
    }

    [Fact]
    public async Task DismissPromptAsync_PerWallet_Independent()
    {
        var svc = CreateService();

        await svc.DismissPromptAsync(WalletId);

        Assert.True(await svc.IsDismissedAsync(WalletId));
        Assert.False(await svc.IsDismissedAsync(WalletId2));
    }

    #endregion

    #region IsConfirmSendEnabledAsync / SetConfirmSendAsync

    [Fact]
    public async Task IsConfirmSendEnabledAsync_ReturnsFalse_ByDefault()
    {
        var svc = CreateService();
        Assert.False(await svc.IsConfirmSendEnabledAsync(WalletId));
    }

    [Fact]
    public async Task SetConfirmSendAsync_Enable_SetsTrue()
    {
        var svc = CreateService();

        await svc.SetConfirmSendAsync(WalletId, true);

        Assert.True(await svc.IsConfirmSendEnabledAsync(WalletId));
    }

    [Fact]
    public async Task SetConfirmSendAsync_Disable_RemovesPreference()
    {
        var svc = CreateService();
        await svc.SetConfirmSendAsync(WalletId, true);
        Assert.True(await svc.IsConfirmSendEnabledAsync(WalletId));

        await svc.SetConfirmSendAsync(WalletId, false);

        Assert.False(await svc.IsConfirmSendEnabledAsync(WalletId));
    }

    [Fact]
    public async Task SetConfirmSendAsync_PerWallet_Independent()
    {
        var svc = CreateService();

        await svc.SetConfirmSendAsync(WalletId, true);

        Assert.True(await svc.IsConfirmSendEnabledAsync(WalletId));
        Assert.False(await svc.IsConfirmSendEnabledAsync(WalletId2));
    }

    #endregion

    #region DisableAsync

    [Fact]
    public async Task DisableAsync_ClearsAllBiometricKeys()
    {
        var storage = new InMemorySecureStorage();
        // Simulate a fully enrolled wallet
        await storage.SetAsync("wallet_bio_enabled_" + WalletId, "true");
        await storage.SetAsync("wallet_bio_cred_" + WalletId, "credentialData");
        await storage.SetAsync("wallet_bio_wrap_" + WalletId, "wrappedKeyData");
        await storage.SetAsync("wallet_bio_seed_" + WalletId, "bioSeedData");
        await storage.SetAsync("wallet_bio_dismissed_" + WalletId, "true");
        await storage.SetAsync("wallet_bio_confirm_send_" + WalletId, "true");

        var svc = CreateService(storage);

        await svc.DisableAsync(WalletId);

        Assert.False(await svc.IsEnabledAsync(WalletId));
        Assert.False(await svc.IsDismissedAsync(WalletId));
        Assert.False(await svc.IsConfirmSendEnabledAsync(WalletId));
        Assert.Null(await storage.GetAsync("wallet_bio_cred_" + WalletId));
        Assert.Null(await storage.GetAsync("wallet_bio_wrap_" + WalletId));
        Assert.Null(await storage.GetAsync("wallet_bio_seed_" + WalletId));
    }

    [Fact]
    public async Task DisableAsync_DoesNotAffectOtherWallets()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("wallet_bio_enabled_" + WalletId, "true");
        await storage.SetAsync("wallet_bio_enabled_" + WalletId2, "true");
        await storage.SetAsync("wallet_bio_confirm_send_" + WalletId2, "true");

        var svc = CreateService(storage);

        await svc.DisableAsync(WalletId);

        Assert.False(await svc.IsEnabledAsync(WalletId));
        Assert.True(await svc.IsEnabledAsync(WalletId2));
        Assert.True(await svc.IsConfirmSendEnabledAsync(WalletId2));
    }

    #endregion

    #region IsSupportedAsync

    [Fact]
    public async Task IsSupportedAsync_ReturnsTrue_WhenJsReturnsTrue()
    {
        var js = new FakeJSRuntime { SupportedResult = true };
        var svc = CreateService(js: js);

        Assert.True(await svc.IsSupportedAsync());
    }

    [Fact]
    public async Task IsSupportedAsync_ReturnsFalse_WhenJsThrows()
    {
        var js = new FakeJSRuntime { ThrowOnInvoke = true };
        var svc = CreateService(js: js);

        Assert.False(await svc.IsSupportedAsync());
    }

    #endregion

    #region VerifyIdentityAsync

    [Fact]
    public async Task VerifyIdentityAsync_ReturnsFalse_WhenNoCredential()
    {
        var svc = CreateService();

        Assert.False(await svc.VerifyIdentityAsync(WalletId));
    }

    [Fact]
    public async Task VerifyIdentityAsync_ReturnsFalse_WhenJsThrows()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("wallet_bio_cred_" + WalletId, "someCredential");
        var js = new FakeJSRuntime { ThrowOnInvoke = true };
        var svc = CreateService(storage, js);

        Assert.False(await svc.VerifyIdentityAsync(WalletId));
    }

    [Fact]
    public async Task VerifyIdentityAsync_ReturnsTrue_WhenJsVerifies()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("wallet_bio_cred_" + WalletId, "someCredential");
        var js = new FakeJSRuntime { VerifyResult = true };
        var svc = CreateService(storage, js);

        Assert.True(await svc.VerifyIdentityAsync(WalletId));
    }

    #endregion

    #region UnlockAsync

    [Fact]
    public async Task UnlockAsync_ReturnsNull_WhenNoCredentialStored()
    {
        var svc = CreateService();

        Assert.Null(await svc.UnlockAsync(WalletId));
    }

    [Fact]
    public async Task UnlockAsync_ReturnsNull_WhenJsThrows()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("wallet_bio_cred_" + WalletId, "cred");
        await storage.SetAsync("wallet_bio_wrap_" + WalletId, "wrap");
        await storage.SetAsync("wallet_bio_seed_" + WalletId, "seed");
        var js = new FakeJSRuntime { ThrowOnInvoke = true };
        var svc = CreateService(storage, js);

        Assert.Null(await svc.UnlockAsync(WalletId));
    }

    #endregion

    #region Fakes

    private class InMemorySecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _store = new();

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_store.TryGetValue(key, out var val) ? val : null);

        public Task SetAsync(string key, string value) { _store[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task ClearAsync() { _store.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
            Task.FromResult(_store.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private class FakeJSRuntime : IJSRuntime
    {
        public bool SupportedResult { get; set; }
        public bool VerifyResult { get; set; }
        public bool ThrowOnInvoke { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (ThrowOnInvoke)
                throw new JSException("JS interop not available");

            object result = identifier switch
            {
                "dgbWebAuthn.isSupported" => SupportedResult,
                "dgbWebAuthn.verifyIdentity" => VerifyResult,
                "dgbWebAuthn.authenticate" => (object)null!,
                _ => default(TValue)!,
            };

            return ValueTask.FromResult((TValue)result);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    #endregion
}
