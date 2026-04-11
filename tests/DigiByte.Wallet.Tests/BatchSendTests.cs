using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class BatchSendTests
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;
    private const string Pin = "123456";

    private static async Task<WalletService> CreateUnlockedWalletAsync(
        IBlockchainService? blockchain = null)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var storage = new InMemorySecureStorage();
        var walletStore = new WalletKeyStore(storage);
        var crypto = new FakeCryptoService();
        var bc = blockchain ?? new FakeBlockchainService();
        var txTracker = new TransactionTracker(storage);

        var service = new WalletService(walletStore, crypto, bc, txTracker);
        service.SetNetworkMode("mainnet");
        await service.CreateWalletAsync("Test", string.Join(' ', mnemonic.Words), Pin);
        return service;
    }

    private static string GenerateAddress()
    {
        var key = new Key();
        return key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network).ToString();
    }

    // ─── Validation ───

    [Fact]
    public async Task SendBatchAsync_EmptyList_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(new List<(string, decimal)>()));
        Assert.Contains("At least one recipient", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_NullList_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(null!));
    }

    [Fact]
    public async Task SendBatchAsync_Over20Recipients_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var recipients = Enumerable.Range(0, 21)
            .Select(_ => (GenerateAddress(), 1.0m))
            .ToList();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Maximum 20", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_DuplicateAddress_Throws()
    {
        var service = await CreateUnlockedWalletAsync();
        var addr = GenerateAddress();

        var recipients = new List<(string, decimal)>
        {
            (addr, 1.0m),
            (addr, 2.0m),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_InvalidAddress_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var recipients = new List<(string, decimal)>
        {
            ("notavalidaddress", 1.0m),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Invalid address", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_AmountBelowMinimum_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var recipients = new List<(string, decimal)>
        {
            (GenerateAddress(), 0.00001m),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("below minimum", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_WatchOnly_Throws()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpub = hd.GetAccountExtPubKey().ToString(Network);

        var storage = new InMemorySecureStorage();
        var service = new WalletService(
            new WalletKeyStore(storage), new FakeCryptoService(),
            new FakeBlockchainService(), new TransactionTracker(storage));
        service.SetNetworkMode("mainnet");
        await service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);

        var recipients = new List<(string, decimal)>
        {
            (GenerateAddress(), 1.0m),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Watch-only", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_InsufficientFunds_Throws()
    {
        // FakeBlockchainService returns empty UTXOs by default
        var service = await CreateUnlockedWalletAsync();

        var recipients = new List<(string, decimal)>
        {
            (GenerateAddress(), 1.0m),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("No UTXOs available", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_Exactly20Recipients_DoesNotThrowArgumentException()
    {
        // Should pass validation (but fail on insufficient funds)
        var service = await CreateUnlockedWalletAsync();

        var recipients = Enumerable.Range(0, 20)
            .Select(_ => (GenerateAddress(), 0.001m))
            .ToList();

        // Should get past the 20-recipient check and fail on UTXOs instead
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("No UTXOs available", ex.Message);
    }

    // ─── Test helpers ───

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
}
