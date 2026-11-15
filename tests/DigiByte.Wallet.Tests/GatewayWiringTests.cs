using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>The pieces that connect the gateway source to the wallet: the scope it answers for and the background bootstrap (T073, T078).</summary>
public class GatewayWiringTests
{
    private const int Cp = 24563100;
    private const string Pin = "123456";

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

    private sealed class Chain : IBlockchainService
    {
        public int Height { get; set; } = Cp + 150;
        public bool HeightFails { get; set; }
        public bool UtxosFail { get; set; }
        public int UtxoCalls { get; private set; }
        public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => GetUtxosAsync([address]);
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
        {
            UtxoCalls++;
            if (UtxosFail)
                throw new InvalidOperationException("explorer down");
            return Task.FromResult(new List<UtxoInfo> { new() { TxId = new string('b', 64), OutputIndex = 0, AmountSatoshis = 500, ScriptPubKey = "0014" + new string('a', 40), Confirmations = 5 } });
        }
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("tx");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => HeightFails ? throw new InvalidOperationException("down") : Task.FromResult(Height);
    }

    private static readonly Lazy<Dictionary<int, byte[]>> HeaderBytes = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static TransactionInfo Tx(char id, int confirmations) => new()
    {
        TxId = new string(id, 64), Confirmations = confirmations, Timestamp = DateTime.UtcNow, FeeSatoshis = 1,
        Inputs = [new TxInput { Address = "DIn", AmountSatoshis = 10 }], Outputs = [new TxOutput { Address = "DOut", AmountSatoshis = 9, Index = 0 }],
    };

    private sealed record Rig(GatewayBootstrapCoordinator Coordinator, WalletSyncStateStore States, Chain Chain, FakeTime Time, bool[] Enabled);

    private static async Task<Rig> RigAsync()
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        await headers.AppendRangeAsync(Enumerable.Range(Cp + 1, 200).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        var states = new WalletSyncStateStore(storage);
        var chain = new Chain();
        var time = new FakeTime();
        var enabled = new[] { true };
        var coordinator = new GatewayBootstrapCoordinator(new GatewayStateBootstrap(states, headers), chain, () => enabled[0], time, TimeSpan.FromMinutes(5));
        return new Rig(coordinator, states, chain, time, enabled);
    }

    // ---- the background bootstrap ----

    [Fact]
    public async Task An_explorer_served_refresh_creates_the_record_with_the_unspent_outputs_read_in_the_background()
    {
        var rig = await RigAsync();

        var outcome = await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 150, [Tx('a', 10)], servedByGateway: false);

        Assert.Equal(BootstrapOutcome.Recorded, outcome);
        var state = (await rig.States.GetAsync("w1"))!;
        Assert.Single(state.Transactions);
        Assert.Single(state.UnspentOutpoints);
        Assert.Equal(1, rig.Chain.UtxoCalls);
    }

    [Fact]
    public async Task Nothing_happens_when_the_feature_is_off()
    {
        var rig = await RigAsync();
        rig.Enabled[0] = false;

        Assert.Null(await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 150, [Tx('a', 10)], false));
        Assert.Null(await rig.Coordinator.ReadTipAsync());
        Assert.Equal(0, rig.Chain.UtxoCalls);
        Assert.Null(await rig.States.GetAsync("w1"));
    }

    [Fact]
    public async Task A_refresh_the_gateway_served_is_not_recorded_from_the_explorer()
    {
        var rig = await RigAsync();

        Assert.Null(await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 150, [Tx('a', 10)], servedByGateway: true));
        Assert.Equal(0, rig.Chain.UtxoCalls);
    }

    [Fact]
    public async Task Without_a_tip_read_before_the_snapshot_nothing_is_recorded()
    {
        var rig = await RigAsync();

        Assert.Null(await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], tipBefore: null, [Tx('a', 10)], false));
    }

    [Fact]
    public async Task It_runs_at_most_once_per_interval_per_wallet()
    {
        var rig = await RigAsync();
        await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 150, [Tx('a', 10)], false);

        Assert.Null(await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 151, [Tx('a', 11)], false));
        Assert.NotNull(await rig.Coordinator.OfferAndWaitAsync("w2", ["DAddr"], Cp + 151, [Tx('a', 11)], false)); // another wallet is separate

        rig.Time.Now += TimeSpan.FromMinutes(6);
        Assert.NotNull(await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 152, [Tx('a', 12)], false));
    }

    [Fact]
    public async Task A_failure_reading_the_unspent_outputs_is_swallowed_and_nothing_is_recorded()
    {
        var rig = await RigAsync();
        rig.Chain.UtxosFail = true;

        var outcome = await rig.Coordinator.OfferAndWaitAsync("w1", ["DAddr"], Cp + 150, [Tx('a', 10)], false);

        Assert.Equal(BootstrapOutcome.Failed, outcome);
        Assert.Null(await rig.States.GetAsync("w1"));
    }

    [Fact]
    public async Task A_failed_tip_read_gives_null_instead_of_an_error()
    {
        var rig = await RigAsync();
        rig.Chain.HeightFails = true;

        Assert.Null(await rig.Coordinator.ReadTipAsync());
    }

    [Fact]
    public async Task The_fire_and_forget_offer_returns_at_once_and_still_records()
    {
        var rig = await RigAsync();

        rig.Coordinator.Offer("w1", ["DAddr"], Cp + 150, [Tx('a', 10)], servedByGateway: false);

        for (var i = 0; i < 50 && await rig.States.GetAsync("w1") is null; i++)
            await Task.Delay(20);
        Assert.NotNull(await rig.States.GetAsync("w1"));
    }

    // ---- the wallet scope ----

    private static (WalletService Service, MemoryStorage Storage) NewWallet(string network = "mainnet")
    {
        var storage = new MemoryStorage();
        var service = new WalletService(new WalletKeyStore(storage), new FakeCrypto(), new Chain(), new TransactionTracker(storage));
        service.SetNetworkMode(network);
        return (service, storage);
    }

    private static string Mnemonic() => string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);

    [Fact]
    public async Task A_locked_or_missing_wallet_has_no_scope()
    {
        var (service, _) = NewWallet();

        Assert.Null(await service.GetScopeAsync());
    }

    [Fact]
    public async Task An_unlocked_mainnet_wallet_offers_its_id_its_addresses_and_which_of_them_are_change()
    {
        var (service, _) = NewWallet();
        var wallet = await service.CreateWalletAsync("W", Mnemonic(), Pin);

        var scope = await service.GetScopeAsync();

        Assert.NotNull(scope);
        Assert.Equal(wallet.Id, scope!.WalletId);
        Assert.NotEmpty(scope.Addresses);
        Assert.NotEmpty(scope.ChangeAddresses);
        Assert.All(scope.ChangeAddresses, c => Assert.Contains(c, scope.Addresses));
        Assert.True(scope.ChangeAddresses.Count < scope.Addresses.Count);
    }

    [Fact]
    public async Task A_testnet_wallet_has_no_scope_because_the_gateway_has_a_mainnet_checkpoint_only()
    {
        var (service, _) = NewWallet("testnet");
        await service.CreateWalletAsync("W", Mnemonic(), Pin);

        Assert.Null(await service.GetScopeAsync());
    }

    [Fact]
    public async Task The_lazy_provider_asks_for_the_scope_only_when_a_read_happens()
    {
        var created = false;
        var (service, _) = NewWallet();
        var lazy = new LazyGatewayScopeProvider(() => { created = true; return service; });

        Assert.False(created);
        await lazy.GetScopeAsync();
        Assert.True(created);
    }
}

/// <summary>The restore hooks on the wallet service do nothing unless the gateway feature is on (T095, T096).</summary>
public class GatewayRestoreHookTests
{
    private sealed class MemoryStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _data = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value) { _data[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _data.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_data.ContainsKey(key));
        public Task ClearAsync() { _data.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => Task.FromResult(_data.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private sealed class PassCrypto : LegacyCryptoBase
    {
        public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
    }

    [Fact]
    public async Task Without_the_gateway_the_restore_hook_returns_nothing_and_the_explorer_restore_is_the_only_path()
    {
        var storage = new MemoryStorage();
        var service = new WalletService(new WalletKeyStore(storage), new PassCrypto(), new FakeBlockchainForHooks(), new TransactionTracker(storage));
        service.SetNetworkMode("mainnet");
        await service.CreateWalletAsync("W", string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words), "123456");

        Assert.Null(await service.RestoreViaGatewayAsync(DateTimeOffset.UtcNow.AddDays(-30)));
        await service.RecordBirthHeightAsync(); // does nothing and does not throw
    }

    private sealed class FakeBlockchainForHooks : IBlockchainService
    {
        public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("tx");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }
}
