using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>The sync state is removed wherever a wallet data is removed, and is not part of any export (FR-009a, T060, T071).</summary>
public class WalletSyncStateLifecycleTests
{
    private const string Pin = "123456";

    private sealed class Rig
    {
        public MemoryStorage Storage { get; } = new();
        public WalletSyncStateStore SyncStates { get; }
        public WalletService Service { get; }

        public Rig()
        {
            SyncStates = new WalletSyncStateStore(Storage);
            Service = new WalletService(new WalletKeyStore(Storage), new FakeCrypto(), new FakeBlockchain(), new TransactionTracker(Storage), syncStateStore: SyncStates);
            Service.SetNetworkMode("mainnet");
        }

        public Task SaveStateAsync(string walletId) =>
            SyncStates.SaveAsync(new WalletSyncState(walletId, 100, new string('a', 64), [], [], DateTimeOffset.UtcNow));
    }

    private static string Mnemonic() => string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);

    [Fact]
    public async Task Deleting_a_wallet_removes_its_sync_state_and_keeps_the_others()
    {
        var rig = new Rig();
        var keep = await rig.Service.CreateWalletAsync("Keep", Mnemonic(), Pin);
        var drop = await rig.Service.CreateWalletAsync("Drop", Mnemonic(), Pin);
        await rig.SaveStateAsync(keep.Id);
        await rig.SaveStateAsync(drop.Id);

        await rig.Service.DeleteWalletAsync(drop.Id);

        Assert.Null(await rig.SyncStates.GetAsync(drop.Id));
        Assert.NotNull(await rig.SyncStates.GetAsync(keep.Id));
    }

    [Fact]
    public async Task Deleting_the_active_wallet_through_the_parameterless_overload_removes_its_state()
    {
        var rig = new Rig();
        var wallet = await rig.Service.CreateWalletAsync("Only", Mnemonic(), Pin);
        await rig.SaveStateAsync(wallet.Id);

        await rig.Service.DeleteWalletAsync();

        Assert.Null(await rig.SyncStates.GetAsync(wallet.Id));
    }

    [Fact]
    public async Task Wiping_all_data_removes_every_sync_state()
    {
        var rig = new Rig();
        var a = await rig.Service.CreateWalletAsync("A", Mnemonic(), Pin);
        var b = await rig.Service.CreateWalletAsync("B", Mnemonic(), Pin);
        await rig.SaveStateAsync(a.Id);
        await rig.SaveStateAsync(b.Id);

        await rig.Service.WipeAllDataAsync();

        Assert.DoesNotContain(rig.Storage.Data.Keys, k => k.StartsWith(WalletSyncStateStore.KeyPrefix));
    }

    [Fact]
    public async Task Clearing_the_state_cache_removes_the_states_but_not_the_wallets()
    {
        var rig = new Rig();
        var wallet = await rig.Service.CreateWalletAsync("W", Mnemonic(), Pin);
        await rig.SaveStateAsync(wallet.Id);

        await rig.SyncStates.DeleteAllAsync();

        Assert.Null(await rig.SyncStates.GetAsync(wallet.Id));
        Assert.Single(await rig.Service.GetAllWalletsAsync());
    }

    [Fact]
    public async Task After_a_deletion_the_state_is_simply_absent_so_the_bootstrap_rule_rebuilds_it()
    {
        var rig = new Rig();
        var wallet = await rig.Service.CreateWalletAsync("W", Mnemonic(), Pin);
        await rig.SaveStateAsync(wallet.Id);
        await rig.SyncStates.DeleteAsync(wallet.Id);

        Assert.Null(await rig.SyncStates.GetAsync(wallet.Id));
        await rig.SaveStateAsync(wallet.Id); // the next explorer-served refresh writes it again
        Assert.NotNull(await rig.SyncStates.GetAsync(wallet.Id));
    }

    [Fact]
    public async Task A_wallet_service_without_a_sync_state_store_still_deletes_normally()
    {
        var storage = new MemoryStorage();
        var service = new WalletService(new WalletKeyStore(storage), new FakeCrypto(), new FakeBlockchain(), new TransactionTracker(storage));
        service.SetNetworkMode("mainnet");
        var wallet = await service.CreateWalletAsync("W", Mnemonic(), Pin);

        await service.DeleteWalletAsync(wallet.Id);

        Assert.Empty(await service.GetAllWalletsAsync());
    }

    private sealed class MemoryStorage : ISecureStorage
    {
        public Dictionary<string, string> Data { get; } = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value) { Data[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { Data.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(Data.ContainsKey(key));
        public Task ClearAsync() { Data.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => Task.FromResult(Data.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private sealed class FakeCrypto : LegacyCryptoBase
    {
        public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
    }

    private sealed class FakeBlockchain : IBlockchainService
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
