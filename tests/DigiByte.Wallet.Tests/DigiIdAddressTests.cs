using System.Text.Json;
using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class DigiIdAddressTests
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;
    private const string Pin = "123456";

    private static WalletService CreateService()
    {
        var storage = new InMemorySecureStorage();
        var walletStore = new WalletKeyStore(storage);
        var service = new WalletService(walletStore, new FakeCryptoService(), new FakeBlockchainService(), new TransactionTracker(storage));
        service.SetNetworkMode("mainnet");
        return service;
    }

    private static string GenerateMnemonic() =>
        string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);

    private static async Task<WalletService> CreateUnlockedServiceAsync()
    {
        var service = CreateService();
        await service.CreateWalletAsync("Test", GenerateMnemonic(), Pin);
        return service;
    }

    [Fact]
    public async Task GetDigiIdAddress_SameDomain_ReturnsSameAddress()
    {
        var service = await CreateUnlockedServiceAsync();

        Assert.Equal(service.GetDigiIdAddress("example.com"), service.GetDigiIdAddress("example.com"));
    }

    [Fact]
    public async Task GetDigiIdAddress_DifferentDomains_ReturnDifferentAddresses()
    {
        var service = await CreateUnlockedServiceAsync();

        Assert.NotEqual(service.GetDigiIdAddress("example.com"), service.GetDigiIdAddress("other.org"));
    }

    [Fact]
    public async Task GetDigiIdAddress_EqualsAddressUsedBySignDigiIdAsync()
    {
        var service = await CreateUnlockedServiceAsync();
        var request = DigiIdService.ParseUri("digiid://example.com/callback?x=nonce123")!;

        var shown = service.GetDigiIdAddress(request.Domain);
        var signed = await service.SignDigiIdAsync(request);

        Assert.Equal(signed.Address, shown);
    }

    [Fact]
    public async Task GetDigiIdAddress_WalletLocked_Throws()
    {
        var service = await CreateUnlockedServiceAsync();
        await service.LockWalletAsync();

        Assert.Throws<InvalidOperationException>(() => service.GetDigiIdAddress("example.com"));
    }

    [Fact]
    public async Task GetDigiIdAddress_WatchOnlyWallet_Throws()
    {
        var service = CreateService();
        var hd = new HdKeyDerivation(new Mnemonic(Wordlist.English, WordCount.Twelve), network: Network);
        await service.CreateWatchOnlyWalletAsync("Watch", hd.GetAccountExtPubKey().ToString(Network), Pin);

        Assert.Throws<InvalidOperationException>(() => service.GetDigiIdAddress("example.com"));
    }

    [Fact]
    public void LoginRecord_LegacyJson_DeserializesWithNewFieldsNull()
    {
        var json = """{"Domain":"example.com","Address":"D123","Timestamp":"2026-10-01T12:00:00Z"}""";

        var record = JsonSerializer.Deserialize<DigiIdLoginRecord>(json)!;

        Assert.Equal("example.com", record.Domain);
        Assert.Equal("D123", record.Address);
        Assert.Null(record.CallbackUrl);
        Assert.Null(record.Uri);
        Assert.Null(record.Nonce);
        Assert.Null(record.Signature);
        Assert.Null(record.IsUnsecure);
    }

    [Fact]
    public void LoginRecord_FullRecord_RoundTrips()
    {
        var record = new DigiIdLoginRecord
        {
            Domain = "example.com",
            Address = "D123",
            Timestamp = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            CallbackUrl = "https://example.com/cb?x=n",
            Uri = "digiid://example.com/cb?x=n",
            Nonce = "n",
            Signature = "sig==",
            IsUnsecure = false,
        };

        var back = JsonSerializer.Deserialize<DigiIdLoginRecord>(JsonSerializer.Serialize(record))!;

        Assert.Equal(record, back);
    }

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
