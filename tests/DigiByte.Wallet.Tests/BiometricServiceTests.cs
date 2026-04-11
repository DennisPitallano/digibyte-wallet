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

    #region IsEnabledAsync (global)

    [Fact]
    public async Task IsEnabledAsync_ReturnsFalse_WhenNotEnrolled()
    {
        var svc = CreateService();
        Assert.False(await svc.IsEnabledAsync());
    }

    [Fact]
    public async Task IsEnabledAsync_ReturnsTrue_WhenEnabled()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_enabled", "true");
        var svc = CreateService(storage);

        Assert.True(await svc.IsEnabledAsync());
    }

    #endregion

    #region HasWalletSeedAsync

    [Fact]
    public async Task HasWalletSeedAsync_ReturnsFalse_WhenNoSeedStored()
    {
        var svc = CreateService();
        Assert.False(await svc.HasWalletSeedAsync(WalletId));
    }

    [Fact]
    public async Task HasWalletSeedAsync_ReturnsTrue_WhenSeedStored()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_seed_" + WalletId, "encryptedSeedData");
        var svc = CreateService(storage);

        Assert.True(await svc.HasWalletSeedAsync(WalletId));
    }

    [Fact]
    public async Task HasWalletSeedAsync_PerWallet_Independent()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_seed_" + WalletId, "encryptedSeedData");
        var svc = CreateService(storage);

        Assert.True(await svc.HasWalletSeedAsync(WalletId));
        Assert.False(await svc.HasWalletSeedAsync(WalletId2));
    }

    #endregion

    #region IsDismissedAsync / DismissPromptAsync (global)

    [Fact]
    public async Task IsDismissedAsync_ReturnsFalse_ByDefault()
    {
        var svc = CreateService();
        Assert.False(await svc.IsDismissedAsync());
    }

    [Fact]
    public async Task DismissPromptAsync_SetsDismissed()
    {
        var svc = CreateService();

        await svc.DismissPromptAsync();

        Assert.True(await svc.IsDismissedAsync());
    }

    #endregion

    #region IsConfirmSendEnabledAsync / SetConfirmSendAsync (global)

    [Fact]
    public async Task IsConfirmSendEnabledAsync_ReturnsFalse_ByDefault()
    {
        var svc = CreateService();
        Assert.False(await svc.IsConfirmSendEnabledAsync());
    }

    [Fact]
    public async Task SetConfirmSendAsync_Enable_SetsTrue()
    {
        var svc = CreateService();

        await svc.SetConfirmSendAsync(true);

        Assert.True(await svc.IsConfirmSendEnabledAsync());
    }

    [Fact]
    public async Task SetConfirmSendAsync_Disable_RemovesPreference()
    {
        var svc = CreateService();
        await svc.SetConfirmSendAsync(true);
        Assert.True(await svc.IsConfirmSendEnabledAsync());

        await svc.SetConfirmSendAsync(false);

        Assert.False(await svc.IsConfirmSendEnabledAsync());
    }

    #endregion

    #region DisableAsync (global)

    [Fact]
    public async Task DisableAsync_ClearsAllBiometricKeys()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_enabled", "true");
        await storage.SetAsync("bio_cred", "credentialData");
        await storage.SetAsync("bio_key", "bioKeyData");
        await storage.SetAsync("bio_dismissed", "true");
        await storage.SetAsync("bio_confirm_send", "true");
        await storage.SetAsync("bio_seed_" + WalletId, "encryptedSeed1");
        await storage.SetAsync("bio_seed_" + WalletId2, "encryptedSeed2");

        var svc = CreateService(storage);

        await svc.DisableAsync();

        Assert.False(await svc.IsEnabledAsync());
        Assert.False(await svc.IsDismissedAsync());
        Assert.False(await svc.IsConfirmSendEnabledAsync());
        Assert.Null(await storage.GetAsync("bio_cred"));
        Assert.Null(await storage.GetAsync("bio_key"));
        Assert.False(await svc.HasWalletSeedAsync(WalletId));
        Assert.False(await svc.HasWalletSeedAsync(WalletId2));
    }

    #endregion

    #region RemoveWalletSeedAsync

    [Fact]
    public async Task RemoveWalletSeedAsync_RemovesOnlyThatWallet()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_seed_" + WalletId, "seed1");
        await storage.SetAsync("bio_seed_" + WalletId2, "seed2");

        var svc = CreateService(storage);

        await svc.RemoveWalletSeedAsync(WalletId);

        Assert.False(await svc.HasWalletSeedAsync(WalletId));
        Assert.True(await svc.HasWalletSeedAsync(WalletId2));
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

    #region VerifyIdentityAsync (global)

    [Fact]
    public async Task VerifyIdentityAsync_ReturnsFalse_WhenNoCredential()
    {
        var svc = CreateService();

        Assert.False(await svc.VerifyIdentityAsync());
    }

    [Fact]
    public async Task VerifyIdentityAsync_ReturnsFalse_WhenJsThrows()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_cred", "someCredential");
        var js = new FakeJSRuntime { ThrowOnInvoke = true };
        var svc = CreateService(storage, js);

        Assert.False(await svc.VerifyIdentityAsync());
    }

    [Fact]
    public async Task VerifyIdentityAsync_ReturnsTrue_WhenJsVerifies()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_cred", "someCredential");
        var js = new FakeJSRuntime { VerifyResult = true };
        var svc = CreateService(storage, js);

        Assert.True(await svc.VerifyIdentityAsync());
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
        await storage.SetAsync("bio_cred", "cred");
        await storage.SetAsync("bio_key", "key");
        await storage.SetAsync("bio_seed_" + WalletId, "seed");
        var js = new FakeJSRuntime { ThrowOnInvoke = true };
        var svc = CreateService(storage, js);

        Assert.Null(await svc.UnlockAsync(WalletId));
    }

    [Fact]
    public async Task UnlockAsync_ReturnsNull_WhenSeedMissing()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_cred", "cred");
        await storage.SetAsync("bio_key", "key");
        // No seed stored for this wallet
        var svc = CreateService(storage);

        Assert.Null(await svc.UnlockAsync(WalletId));
    }

    #endregion

    #region AddWalletSeedAsync

    [Fact]
    public async Task AddWalletSeedAsync_ReturnsFalse_WhenNoBioKey()
    {
        var svc = CreateService();

        Assert.False(await svc.AddWalletSeedAsync(WalletId, new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task AddWalletSeedAsync_StoresSeed_WhenBioKeyExists()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("bio_key", "testBioKey");
        var js = new FakeJSRuntime { EncryptResult = "encryptedSeedData" };
        var svc = CreateService(storage, js);

        var result = await svc.AddWalletSeedAsync(WalletId, new byte[] { 1, 2, 3 });

        Assert.True(result);
        Assert.True(await svc.HasWalletSeedAsync(WalletId));
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
        public string? EncryptResult { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (ThrowOnInvoke)
                throw new JSException("JS interop not available");

            object result = identifier switch
            {
                "dgbWebAuthn.isSupported" => SupportedResult,
                "dgbWebAuthn.verifyIdentity" => VerifyResult,
                "dgbWebAuthn.authenticate" => (object)null!,
                "dgbWebAuthn.encryptSeed" => EncryptResult ?? "encrypted",
                "dgbWebAuthn.decryptSeed" => (object)null!,
                _ => default(TValue)!,
            };

            return ValueTask.FromResult((TValue)result);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    #endregion
}
