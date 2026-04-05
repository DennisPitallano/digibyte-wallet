using System.Text;
using System.Text.Json;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class WalletServiceWatchOnlyTests
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;
    private const string Pin = "123456";

    private static (WalletService Service, string Xpub) CreateServiceWithXpub()
    {
        // Generate a real mnemonic + xpub for testing
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpub = hd.GetAccountExtPubKey().ToString(Network);

        var storage = new InMemorySecureStorage();
        var walletStore = new WalletKeyStore(storage);
        var crypto = new FakeCryptoService();
        var blockchain = new FakeBlockchainService();
        var txTracker = new TransactionTracker(storage);

        var service = new WalletService(walletStore, crypto, blockchain, txTracker);
        service.SetNetworkMode("mainnet");
        return (service, xpub);
    }

    [Fact]
    public async Task CreateWatchOnlyWalletAsync_SetsTypeXpub()
    {
        var (service, xpub) = CreateServiceWithXpub();

        var wallet = await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        Assert.Equal("xpub", wallet.WalletType);
        Assert.Equal("Test Watch", wallet.Name);
        Assert.True(service.IsUnlocked);
    }

    [Fact]
    public async Task CreateWatchOnlyWalletAsync_InvalidXpub_Throws()
    {
        var (service, _) = CreateServiceWithXpub();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateWatchOnlyWalletAsync("Bad", "notanxpub", Pin));
    }

    [Fact]
    public async Task UnlockWalletAsync_Xpub_RestoresWatchOnly()
    {
        var (service, xpub) = CreateServiceWithXpub();
        var wallet = await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        // Lock then unlock
        await service.LockWalletAsync();
        Assert.False(service.IsUnlocked);

        var unlocked = await service.UnlockWalletAsync(wallet.Id, Pin);

        Assert.True(unlocked);
        Assert.True(service.IsUnlocked);
        Assert.Equal("xpub", service.ActiveWallet?.WalletType);
    }

    [Fact]
    public async Task GetReceivingAddressAsync_Xpub_ReturnsDgbAddress()
    {
        var (service, xpub) = CreateServiceWithXpub();
        await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        var address = await service.GetReceivingAddressAsync();

        Assert.NotNull(address);
        Assert.StartsWith("dgb1", address); // SegWit
    }

    [Fact]
    public async Task GetReceivingAddressAsync_Xpub_Legacy_ReturnsDAddress()
    {
        var (service, xpub) = CreateServiceWithXpub();
        await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        var address = await service.GetReceivingAddressAsync("legacy");

        Assert.NotNull(address);
        Assert.StartsWith("D", address); // Legacy
    }

    [Fact]
    public async Task GetAllAddresses_Xpub_Returns60Addresses()
    {
        var (service, xpub) = CreateServiceWithXpub();
        await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        // 20 receiving × 2 formats + 10 change × 2 formats = 60
        var addresses = service.GetAllAddresses();

        Assert.Equal(60, addresses.Count);
        Assert.All(addresses, a => Assert.False(string.IsNullOrEmpty(a)));
    }

    [Fact]
    public async Task GetAllAddresses_Xpub_MatchesFullWallet()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpub = hd.GetAccountExtPubKey().ToString(Network);

        // Create full HD wallet service
        var storage1 = new InMemorySecureStorage();
        var ws1 = new WalletService(
            new WalletKeyStore(storage1), new FakeCryptoService(),
            new FakeBlockchainService(), new TransactionTracker(storage1));
        ws1.SetNetworkMode("mainnet");
        await ws1.CreateWalletAsync("Full", string.Join(' ', mnemonic.Words), Pin);

        // Create watch-only wallet service
        var storage2 = new InMemorySecureStorage();
        var ws2 = new WalletService(
            new WalletKeyStore(storage2), new FakeCryptoService(),
            new FakeBlockchainService(), new TransactionTracker(storage2));
        ws2.SetNetworkMode("mainnet");
        await ws2.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);

        var fullAddresses = ws1.GetAllAddresses();
        var watchAddresses = ws2.GetAllAddresses();

        Assert.Equal(fullAddresses, watchAddresses);
    }

    [Fact]
    public async Task SendAsync_Xpub_Throws()
    {
        var (service, xpub) = CreateServiceWithXpub();
        await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("DFakeAddress123", 1.0m));
        Assert.Contains("Watch-only", ex.Message);
    }

    [Fact]
    public async Task GetBalanceAsync_Xpub_Works()
    {
        var (service, xpub) = CreateServiceWithXpub();
        await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        // FakeBlockchainService returns 0 — just verify it doesn't throw
        var balance = await service.GetBalanceAsync();
        Assert.NotNull(balance);
        Assert.Equal(0, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task GetReceivingAddressesAsync_Xpub_Works()
    {
        var (service, xpub) = CreateServiceWithXpub();
        await service.CreateWatchOnlyWalletAsync("Test Watch", xpub, Pin);

        var result = await service.GetReceivingAddressesAsync(5);

        Assert.Equal(5, result.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i, result[i].Index);
    }

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
    }

    /// <summary>
    /// Fake crypto that just passes data through (base64), not truly encrypted.
    /// </summary>
    private class FakeCryptoService : ICryptoService
    {
        public Task<byte[]> EncryptAsync(byte[] plaintext, string pin) =>
            Task.FromResult(plaintext);

        public Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) =>
            Task.FromResult<byte[]?>(ciphertext);
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
