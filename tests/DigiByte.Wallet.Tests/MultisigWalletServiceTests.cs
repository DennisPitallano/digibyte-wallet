using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class MultisigWalletServiceTests
{
    private static Key[] GenerateKeys(int count)
    {
        var keys = new Key[count];
        for (int i = 0; i < count; i++)
            keys[i] = new Key();
        return keys;
    }

    private static MultisigWalletService CreateService(
        FakeSecureStorage? storage = null,
        FakeBlockchain? blockchain = null)
    {
        return new MultisigWalletService(
            storage ?? new FakeSecureStorage(),
            blockchain ?? new FakeBlockchain(),
            new FakeCryptoService());
    }

    // ─── CreateMultisigWalletAsync ───

    [Fact]
    public async Task CreateMultisigWallet_ReturnsValidConfig()
    {
        var keys = GenerateKeys(3);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = i == 0,
        }).ToList();

        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Test Wallet", 2, coSigners);

        Assert.Equal(2, config.RequiredSignatures);
        Assert.Equal(3, config.TotalSigners);
        Assert.Equal(3, config.CoSigners.Count);
        Assert.NotEmpty(config.RedeemScriptHex);
        Assert.NotEmpty(config.Address);
        Assert.Equal(0, config.OwnKeyIndex);
        Assert.Equal("2-of-3 Multisig", config.Label);
    }

    [Fact]
    public async Task CreateMultisigWallet_PersistsToStorage()
    {
        var storage = new FakeSecureStorage();
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = i == 0,
        }).ToList();

        var sut = CreateService(storage);
        var config = await sut.CreateMultisigWalletAsync("Persisted", 2, coSigners);

        // Verify it was stored
        Assert.True(await storage.ContainsKeyAsync($"multisig_config_{config.WalletId}"));
    }

    [Fact]
    public async Task CreateMultisigWallet_P2WSH_UsesNativeSegwit()
    {
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = false,
        }).ToList();

        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Native", 2, coSigners, addressType: "p2wsh");

        Assert.Equal("p2wsh", config.AddressType);
        Assert.StartsWith("dgb1", config.Address);
    }

    [Fact]
    public async Task CreateMultisigWallet_NoLocalKey_OwnKeyIndexIsNegative()
    {
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = false,
        }).ToList();

        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("WatchOnly", 2, coSigners);

        Assert.Equal(-1, config.OwnKeyIndex);
    }

    // ─── ImportMultisigWalletAsync ───

    [Fact]
    public async Task ImportMultisigWallet_FromRedeemScript_ReturnsCorrectConfig()
    {
        var keys = GenerateKeys(3);
        var msService = new MultisigService(DigiByteNetwork.Mainnet);
        var script = msService.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var sut = CreateService();
        var config = await sut.ImportMultisigWalletAsync("Imported", script.ToHex(), ownKeyIndex: 1);

        Assert.Equal(2, config.RequiredSignatures);
        Assert.Equal(3, config.TotalSigners);
        Assert.Equal(1, config.OwnKeyIndex);
        Assert.True(config.CoSigners[1].IsLocal);
        Assert.False(config.CoSigners[0].IsLocal);
    }

    [Fact]
    public async Task ImportMultisigWallet_InvalidScript_Throws()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ImportMultisigWalletAsync("Bad", "deadbeef", ownKeyIndex: 0));
    }

    [Fact]
    public async Task ImportMultisigWallet_WatchOnly_OwnKeyNegative()
    {
        var keys = GenerateKeys(2);
        var msService = new MultisigService(DigiByteNetwork.Mainnet);
        var script = msService.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var sut = CreateService();
        var config = await sut.ImportMultisigWalletAsync("Watch", script.ToHex());

        Assert.Equal(-1, config.OwnKeyIndex);
        Assert.True(config.CoSigners.All(cs => !cs.IsLocal));
    }

    // ─── ListWalletsAsync / GetConfigAsync / DeleteWalletAsync ───

    [Fact]
    public async Task ListWallets_EmptyInitially()
    {
        var sut = CreateService();
        var list = await sut.ListWalletsAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListWallets_AfterCreate_ContainsWallet()
    {
        var storage = new FakeSecureStorage();
        var sut = CreateService(storage);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        await sut.CreateMultisigWalletAsync("MyMultisig", 2, coSigners);
        var list = await sut.ListWalletsAsync();

        Assert.Single(list);
        Assert.Equal("MyMultisig", list[0].Name);
    }

    [Fact]
    public async Task GetConfig_ReturnsStoredConfig()
    {
        var storage = new FakeSecureStorage();
        var sut = CreateService(storage);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var created = await sut.CreateMultisigWalletAsync("Test", 2, coSigners);
        var loaded = await sut.GetConfigAsync(created.WalletId);

        Assert.NotNull(loaded);
        Assert.Equal(created.WalletId, loaded.WalletId);
        Assert.Equal(created.Address, loaded.Address);
        Assert.Equal(created.RequiredSignatures, loaded.RequiredSignatures);
    }

    [Fact]
    public async Task GetConfig_NonExistent_ReturnsNull()
    {
        var sut = CreateService();
        var result = await sut.GetConfigAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteWallet_RemovesFromList()
    {
        var storage = new FakeSecureStorage();
        var sut = CreateService(storage);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var config = await sut.CreateMultisigWalletAsync("ToDelete", 2, coSigners);
        await sut.DeleteWalletAsync(config.WalletId);

        var list = await sut.ListWalletsAsync();
        Assert.Empty(list);

        var loaded = await sut.GetConfigAsync(config.WalletId);
        Assert.Null(loaded);
    }

    // ─── GetBalanceAsync ───

    [Fact]
    public async Task GetBalance_DelegatesToBlockchain()
    {
        var blockchain = new FakeBlockchain(balance: 500_000_000);
        var sut = CreateService(blockchain: blockchain);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var config = await sut.CreateMultisigWalletAsync("Bal", 2, coSigners);
        var balance = await sut.GetBalanceAsync(config);

        Assert.Equal(500_000_000, balance);
    }

    // ─── PendingTransactions ───

    [Fact]
    public async Task GetPendingTransactions_EmptyInitially()
    {
        var sut = CreateService();
        var list = await sut.GetPendingTransactionsAsync("some-wallet-id");
        Assert.Empty(list);
    }

    // ─── SetNetworkMode ───

    [Fact]
    public async Task SetNetworkMode_Testnet_AffectsAddresses()
    {
        var sut = CreateService();
        sut.SetNetworkMode("testnet");

        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var config = await sut.CreateMultisigWalletAsync("Testnet", 2, coSigners, addressType: "p2wsh");

        Assert.StartsWith("dgbt1", config.Address);
    }

    // ─── Test helpers ───

    private class FakeSecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _store = [];

        public Task<string?> GetAsync(string key)
            => Task.FromResult(_store.TryGetValue(key, out var val) ? val : null);

        public Task SetAsync(string key, string value)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ContainsKeyAsync(string key)
            => Task.FromResult(_store.ContainsKey(key));

        public Task ClearAsync()
        {
            _store.Clear();
            return Task.CompletedTask;
        }
    }

    private class FakeBlockchain : IBlockchainService
    {
        private readonly long _balance;
        private readonly List<UtxoInfo> _utxos;
        private readonly string _broadcastResult;

        public FakeBlockchain(long balance = 0, List<UtxoInfo>? utxos = null, string broadcastResult = "fake-txid")
        {
            _balance = balance;
            _utxos = utxos ?? [];
            _broadcastResult = broadcastResult;
        }

        public Task<long> GetBalanceAsync(string address) => Task.FromResult(_balance);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(_balance);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(_utxos);
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(_utxos);
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult(_broadcastResult);
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(0.001m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }

    private class FakeCryptoService : ICryptoService
    {
        public Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
    }
}
