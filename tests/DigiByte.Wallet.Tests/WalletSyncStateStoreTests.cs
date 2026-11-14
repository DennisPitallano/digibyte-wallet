using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>The wallet sync record: stored per wallet on the device, validated against the data-model rules, and gone when the wallet is (T057, T060, T070).</summary>
public class WalletSyncStateStoreTests
{
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

    private static string Hash(char c) => new(c, 64);

    private static WalletSyncState State(string walletId = "w1", int height = 1000, params SyncTransaction[] transactions) =>
        new(walletId, height, Hash('a'), transactions, [new SyncOutpoint(Hash('b'), 1, 500, "0014ab")], new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

    private static SyncTransaction Proven() => new(Hash('c'), 990, Hash('d'), "0100", SyncOrigin.GatewayProven);

    private static SyncTransaction FromExplorer() => new(Hash('e'), 980, Hash('f'), null, SyncOrigin.ExplorerSourced);

    [Fact]
    public async Task A_saved_state_comes_back_the_same()
    {
        var store = new WalletSyncStateStore(new MemoryStorage());
        var state = State(transactions: [Proven(), FromExplorer()]);

        await store.SaveAsync(state);
        var loaded = await store.GetAsync("w1");

        Assert.NotNull(loaded);
        Assert.Equal(1000, loaded!.LastSyncedHeight);
        Assert.Equal(Hash('a'), loaded.LastSyncedHash);
        Assert.Equal(2, loaded.Transactions.Count);
        Assert.Equal(SyncOrigin.GatewayProven, loaded.Transactions[0].Origin);
        Assert.Equal("0100", loaded.Transactions[0].Raw);
        Assert.Null(loaded.Transactions[1].Raw);
        Assert.Equal($"{Hash('b')}:1", loaded.UnspentOutpoints.Single().Key);
    }

    [Fact]
    public async Task A_wallet_without_a_state_gets_none()
    {
        Assert.Null(await new WalletSyncStateStore(new MemoryStorage()).GetAsync("nobody"));
    }

    [Fact]
    public async Task Each_wallet_has_its_own_state()
    {
        var store = new WalletSyncStateStore(new MemoryStorage());
        await store.SaveAsync(State("w1", 100));
        await store.SaveAsync(State("w2", 200));

        Assert.Equal(100, (await store.GetAsync("w1"))!.LastSyncedHeight);
        Assert.Equal(200, (await store.GetAsync("w2"))!.LastSyncedHeight);
    }

    [Fact]
    public async Task A_save_that_is_refused_leaves_the_earlier_state_exactly_as_it_was()
    {
        var store = new WalletSyncStateStore(new MemoryStorage());
        await store.SaveAsync(State(height: 1000, transactions: Proven()));

        await Assert.ThrowsAsync<InvalidSyncStateException>(() =>
            store.SaveAsync(State(height: 2000, transactions: new SyncTransaction(Hash('9'), 1990, Hash('8'), null, SyncOrigin.GatewayProven))));

        var loaded = await store.GetAsync("w1");
        Assert.Equal(1000, loaded!.LastSyncedHeight);
        Assert.Equal(Hash('a'), loaded.LastSyncedHash);
    }

    [Fact]
    public async Task A_gateway_proven_transaction_must_keep_its_raw_bytes()
    {
        var store = new WalletSyncStateStore(new MemoryStorage());

        await Assert.ThrowsAsync<InvalidSyncStateException>(() =>
            store.SaveAsync(State(transactions: new SyncTransaction(Hash('c'), 990, Hash('d'), null, SyncOrigin.GatewayProven))));
        // An explorer-sourced one does not need them.
        await store.SaveAsync(State(transactions: FromExplorer()));
    }

    [Theory]
    [InlineData(-1, "a")]
    [InlineData(10, "short")]
    [InlineData(10, "")]
    public async Task A_height_or_hash_that_is_not_valid_is_refused(int height, string hash)
    {
        var store = new WalletSyncStateStore(new MemoryStorage());
        var state = State() with { LastSyncedHeight = height, LastSyncedHash = hash.Length == 1 ? Hash(hash[0]) : hash };

        await Assert.ThrowsAsync<InvalidSyncStateException>(() => store.SaveAsync(state));
    }

    [Fact]
    public async Task A_confirmed_transaction_needs_its_block_hash_but_an_unconfirmed_one_does_not()
    {
        var store = new WalletSyncStateStore(new MemoryStorage());

        await Assert.ThrowsAsync<InvalidSyncStateException>(() =>
            store.SaveAsync(State(transactions: new SyncTransaction(Hash('c'), 990, "", "0100", SyncOrigin.GatewayProven))));
        await store.SaveAsync(State(transactions: new SyncTransaction(Hash('c'), 0, "", "0100", SyncOrigin.GatewayProven, IsOwnSend: true)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    [InlineData("{\"walletId\":\"someone-else\",\"lastSyncedHeight\":1,\"lastSyncedHash\":\"x\",\"transactions\":[],\"unspentOutpoints\":[],\"updatedAt\":\"2026-01-01T00:00:00+00:00\"}")]
    public async Task A_record_that_cannot_be_read_is_discarded_and_treated_as_absent(string stored)
    {
        var storage = new MemoryStorage();
        storage.Data[WalletSyncStateStore.KeyFor("w1")] = stored;
        var store = new WalletSyncStateStore(storage);

        Assert.Null(await store.GetAsync("w1"));
        Assert.False(storage.Data.ContainsKey(WalletSyncStateStore.KeyFor("w1")));
    }

    [Fact]
    public async Task Deleting_a_wallet_removes_only_its_state()
    {
        var storage = new MemoryStorage();
        var store = new WalletSyncStateStore(storage);
        await store.SaveAsync(State("w1"));
        await store.SaveAsync(State("w2"));

        await store.DeleteAsync("w1");

        Assert.Null(await store.GetAsync("w1"));
        Assert.NotNull(await store.GetAsync("w2"));
    }

    [Fact]
    public async Task Clearing_everything_removes_every_wallets_state_and_nothing_else()
    {
        var storage = new MemoryStorage();
        storage.Data["wallet:unrelated"] = "keep";
        var store = new WalletSyncStateStore(storage);
        await store.SaveAsync(State("w1"));
        await store.SaveAsync(State("w2"));

        await store.DeleteAllAsync();

        Assert.Equal(["wallet:unrelated"], storage.Data.Keys.ToList());
    }

    [Fact]
    public async Task The_state_is_only_ever_stored_under_the_prefix_the_clear_paths_know()
    {
        var storage = new MemoryStorage();
        await new WalletSyncStateStore(storage).SaveAsync(State("w1", transactions: Proven()));

        Assert.All(storage.Data.Keys, k => Assert.StartsWith(WalletSyncStateStore.KeyPrefix, k));
    }

    [Fact]
    public async Task A_missing_wallet_id_is_refused()
    {
        var store = new WalletSyncStateStore(new MemoryStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(" "));
    }
}
