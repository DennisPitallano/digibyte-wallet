using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class MultiWalletTests
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;
    private const string Pin = "123456";

    private static WalletService CreateService()
    {
        var storage = new InMemorySecureStorage();
        var walletStore = new WalletKeyStore(storage);
        var crypto = new FakeCryptoService();
        var blockchain = new FakeBlockchainService();
        var txTracker = new TransactionTracker(storage);
        var service = new WalletService(walletStore, crypto, blockchain, txTracker);
        service.SetNetworkMode("mainnet");
        return service;
    }

    private static string GenerateMnemonic() =>
        string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);

    #region Multiple Wallet Creation

    [Fact]
    public async Task CreateMultipleWallets_AllPersisted()
    {
        var service = CreateService();

        var w1 = await service.CreateWalletAsync("Wallet 1", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("Wallet 2", GenerateMnemonic(), Pin);
        var w3 = await service.CreateWalletAsync("Wallet 3", GenerateMnemonic(), Pin);

        var all = await service.GetAllWalletsAsync();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, w => w.Name == "Wallet 1");
        Assert.Contains(all, w => w.Name == "Wallet 2");
        Assert.Contains(all, w => w.Name == "Wallet 3");
    }

    [Fact]
    public async Task CreateWallet_AssignsUniqueIds()
    {
        var service = CreateService();

        var w1 = await service.CreateWalletAsync("A", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("B", GenerateMnemonic(), Pin);

        Assert.NotEqual(w1.Id, w2.Id);
    }

    [Fact]
    public async Task CreateWallet_AssignsAutoColor_WhenNullColor()
    {
        var service = CreateService();

        var w1 = await service.CreateWalletAsync("A", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("B", GenerateMnemonic(), Pin);

        Assert.NotNull(w1.Color);
        Assert.NotNull(w2.Color);
        Assert.Contains(w1.Color, WalletInfo.ColorPalette);
        Assert.Contains(w2.Color, WalletInfo.ColorPalette);
    }

    [Fact]
    public async Task CreateWallet_UsesProvidedColor()
    {
        var service = CreateService();

        var wallet = await service.CreateWalletAsync("Custom", GenerateMnemonic(), Pin, color: "#FF5733");

        Assert.Equal("#FF5733", wallet.Color);
    }

    [Fact]
    public async Task CreateWallet_MaxTen_ThrowsOnEleventh()
    {
        var service = CreateService();
        for (int i = 0; i < 10; i++)
            await service.CreateWalletAsync($"W{i}", GenerateMnemonic(), Pin);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateWalletAsync("W10", GenerateMnemonic(), Pin));
    }

    #endregion

    #region Wallet Switching

    [Fact]
    public async Task SwitchWalletAsync_ChangesActiveWallet()
    {
        var service = CreateService();
        var w1 = await service.CreateWalletAsync("First", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("Second", GenerateMnemonic(), Pin);

        // After creating w2, it should be active (last created)
        Assert.Equal(w2.Id, service.ActiveWallet?.Id);

        // Switch back to w1
        await service.SwitchWalletAsync(w1.Id);
        Assert.Equal(w1.Id, service.ActiveWallet?.Id);
        Assert.Equal("First", service.ActiveWallet?.Name);
    }

    [Fact]
    public async Task IsWalletUnlockedInSession_ReturnsTrueForCreated()
    {
        var service = CreateService();
        var w1 = await service.CreateWalletAsync("A", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("B", GenerateMnemonic(), Pin);

        Assert.True(service.IsWalletUnlockedInSession(w1.Id));
        Assert.True(service.IsWalletUnlockedInSession(w2.Id));
    }

    [Fact]
    public async Task SwitchWallet_BalanceIsPerWallet()
    {
        var service = CreateService();
        var w1 = await service.CreateWalletAsync("W1", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("W2", GenerateMnemonic(), Pin);

        // Each wallet should have its own address
        await service.SwitchWalletAsync(w1.Id);
        var addr1 = await service.GetReceivingAddressAsync();

        await service.SwitchWalletAsync(w2.Id);
        var addr2 = await service.GetReceivingAddressAsync();

        Assert.NotEqual(addr1, addr2);
    }

    #endregion

    #region Rename Wallet

    [Fact]
    public async Task RenameWalletAsync_UpdatesName()
    {
        var service = CreateService();
        var wallet = await service.CreateWalletAsync("Original", GenerateMnemonic(), Pin);

        await service.RenameWalletAsync(wallet.Id, "Renamed");

        var info = await service.GetWalletAsync(wallet.Id);
        Assert.Equal("Renamed", info?.Name);
    }

    [Fact]
    public async Task RenameWalletAsync_UpdatesActiveWalletName()
    {
        var service = CreateService();
        var wallet = await service.CreateWalletAsync("Old Name", GenerateMnemonic(), Pin);

        await service.RenameWalletAsync(wallet.Id, "New Name");

        Assert.Equal("New Name", service.ActiveWallet?.Name);
    }

    #endregion

    #region Change Wallet Color

    [Fact]
    public async Task ChangeWalletColorAsync_UpdatesColor()
    {
        var service = CreateService();
        var wallet = await service.CreateWalletAsync("Test", GenerateMnemonic(), Pin);

        await service.ChangeWalletColorAsync(wallet.Id, "#FF0000");

        var info = await service.GetWalletAsync(wallet.Id);
        Assert.Equal("#FF0000", info?.Color);
    }

    [Fact]
    public async Task ChangeWalletColorAsync_UpdatesActiveWalletColor()
    {
        var service = CreateService();
        var wallet = await service.CreateWalletAsync("Test", GenerateMnemonic(), Pin);

        await service.ChangeWalletColorAsync(wallet.Id, "#00FF00");

        Assert.Equal("#00FF00", service.ActiveWallet?.Color);
    }

    [Fact]
    public async Task ChangeWalletColorAsync_NonExistentWallet_DoesNotThrow()
    {
        var service = CreateService();
        await service.CreateWalletAsync("Test", GenerateMnemonic(), Pin);

        // Should not throw for missing wallet
        await service.ChangeWalletColorAsync("non-existent-id", "#FF0000");
    }

    #endregion

    #region Delete Wallet

    [Fact]
    public async Task DeleteWalletAsync_RemovesFromList()
    {
        var service = CreateService();
        var w1 = await service.CreateWalletAsync("Keep", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("Delete", GenerateMnemonic(), Pin);

        await service.DeleteWalletAsync(w2.Id);

        var all = await service.GetAllWalletsAsync();
        Assert.Single(all);
        Assert.Equal("Keep", all[0].Name);
    }

    [Fact]
    public async Task DeleteWalletAsync_SwitchesToRemainingWallet()
    {
        var service = CreateService();
        var w1 = await service.CreateWalletAsync("First", GenerateMnemonic(), Pin);
        var w2 = await service.CreateWalletAsync("Second", GenerateMnemonic(), Pin);

        // w2 is active, delete it
        await service.DeleteWalletAsync(w2.Id);

        // Should auto-switch to w1
        Assert.Equal(w1.Id, service.ActiveWallet?.Id);
    }

    #endregion

    #region WalletInfo Color Palette

    [Fact]
    public void ColorPalette_HasTenColors()
    {
        Assert.Equal(10, WalletInfo.ColorPalette.Length);
    }

    [Fact]
    public void ColorPalette_AllValidHexColors()
    {
        foreach (var color in WalletInfo.ColorPalette)
        {
            Assert.StartsWith("#", color);
            Assert.Equal(7, color.Length); // #RRGGBB
        }
    }

    [Fact]
    public void WalletInfo_DefaultColor_IsBlue()
    {
        var info = new WalletInfo { Id = "test", Name = "Test", CreatedAt = DateTime.UtcNow };
        Assert.Equal("#0066FF", info.Color);
    }

    #endregion

    #region Mixed Wallet Types

    [Fact]
    public async Task MultipleWalletTypes_CoExist()
    {
        var service = CreateService();

        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpub = hd.GetAccountExtPubKey().ToString(Network);

        var hdWallet = await service.CreateWalletAsync("HD Wallet", GenerateMnemonic(), Pin);
        var watchWallet = await service.CreateWatchOnlyWalletAsync("Watch Wallet", xpub, Pin);

        var all = await service.GetAllWalletsAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, w => w.WalletType == "hd");
        Assert.Contains(all, w => w.WalletType == "xpub");
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

    private class FakeCryptoService : ICryptoService
    {
        public Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
    }

    private class FakeBlockchainService : IBlockchainService
    {
        public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("faketxid");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }

    #endregion
}
