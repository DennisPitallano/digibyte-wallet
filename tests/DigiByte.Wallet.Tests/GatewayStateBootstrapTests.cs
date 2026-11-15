using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>The wallet record is created from an explorer-served refresh and kept fresh by later ones (FR-009a, T059, T073).</summary>
public class GatewayStateBootstrapTests
{
    private const int Cp = 24563100;
    private const string WalletId = "w1";

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

    private static readonly Lazy<Dictionary<int, byte[]>> HeaderBytes = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private static string Id(char c) => new(c, 64);

    private sealed record Rig(GatewayStateBootstrap Bootstrap, WalletSyncStateStore States, GatewayHeaderStore Headers);

    private static async Task<Rig> RigAsync(int headersUpTo = Cp + 200)
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        await headers.AppendRangeAsync(Enumerable.Range(Cp + 1, headersUpTo - Cp).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        var states = new WalletSyncStateStore(storage);
        return new Rig(new GatewayStateBootstrap(states, headers), states, headers);
    }

    /// <summary>An explorer transaction that is <paramref name="confirmations"/> blocks deep when the explorer tip is <paramref name="tip"/>.</summary>
    private static TransactionInfo Tx(char id, int confirmations) => new()
    {
        TxId = Id(id), Confirmations = confirmations, Timestamp = DateTime.UtcNow, FeeSatoshis = 1,
        Inputs = [new TxInput { Address = "DInput", AmountSatoshis = 10 }], Outputs = [new TxOutput { Address = "DOut", AmountSatoshis = 9, Index = 0 }],
    };

    private static UtxoInfo Utxo(char id, uint index, long amount) => new() { TxId = Id(id), OutputIndex = index, AmountSatoshis = amount, ScriptPubKey = "0014" + new string('a', 40), Confirmations = 5 };

    // ---- creating the record ----

    [Fact]
    public async Task A_wallet_with_no_record_gets_one_with_every_transaction_explorer_sourced()
    {
        var rig = await RigAsync();

        var outcome = await rig.Bootstrap.RecordAsync(WalletId, explorerTipHeight: Cp + 150, [Tx('a', 20), Tx('b', 3)], [Utxo('b', 0, 500)]);

        Assert.Equal(BootstrapOutcome.Recorded, outcome);
        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.All(state.Transactions, t => Assert.Equal(SyncOrigin.ExplorerSourced, t.Origin));
        Assert.Equal(2, state.Transactions.Count);
        Assert.Equal(Cp + 131, state.Transactions.Single(t => t.Txid == Id('a')).Height); // 150 - 20 + 1
        Assert.Equal(rig.Headers.HeaderAt(Cp + 131)!.Hash.ToString(), state.Transactions.Single(t => t.Txid == Id('a')).BlockHash);
        Assert.Equal($"{Id('b')}:0", state.UnspentOutpoints.Single().Key);
    }

    [Fact]
    public async Task The_last_refreshed_height_is_the_explorer_tip_minus_six_with_the_hash_from_the_wallet_own_headers()
    {
        var rig = await RigAsync();

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [], []);

        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.Equal(Cp + 144, state.LastSyncedHeight);
        Assert.Equal(rig.Headers.HeaderAt(Cp + 144)!.Hash.ToString(), state.LastSyncedHash);
    }

    [Fact]
    public async Task When_the_wallet_headers_cannot_supply_the_hash_nothing_is_recorded()
    {
        var rig = await RigAsync(headersUpTo: Cp + 100);

        var outcome = await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 5)], [Utxo('a', 0, 5)]);

        Assert.Equal(BootstrapOutcome.HeadersNotReady, outcome);
        Assert.Null(await rig.States.GetAsync(WalletId));
    }

    [Fact]
    public async Task A_transaction_older_than_the_wallet_headers_is_recorded_without_a_block_hash()
    {
        var rig = await RigAsync();

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 150 + 1_000_000)], []);

        var stored = (await rig.States.GetAsync(WalletId))!.Transactions.Single();
        Assert.Equal(SyncOrigin.ExplorerSourced, stored.Origin);
        Assert.Equal(string.Empty, stored.BlockHash);
        Assert.Equal(Cp + 150 - (150 + 1_000_000) + 1, stored.Height); // far older than the wallet headers: no hash
    }

    [Fact]
    public async Task An_unconfirmed_explorer_transaction_is_recorded_as_not_in_a_block()
    {
        var rig = await RigAsync();

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 0)], []);

        var stored = (await rig.States.GetAsync(WalletId))!.Transactions.Single();
        Assert.Equal(0, stored.Height);
        Assert.Equal(string.Empty, stored.BlockHash);
    }

    // ---- keeping it fresh ----

    [Fact]
    public async Task A_later_explorer_refresh_adds_new_transactions_and_moves_the_height_forward_only()
    {
        var rig = await RigAsync();
        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 20)], []);

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 170, [Tx('a', 40), Tx('c', 2)], []);
        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.Equal(Cp + 164, state.LastSyncedHeight);
        Assert.Equal(2, state.Transactions.Count);

        // An explorer that reports an older tip never moves the record back.
        await rig.Bootstrap.RecordAsync(WalletId, Cp + 120, [], []);
        Assert.Equal(Cp + 164, (await rig.States.GetAsync(WalletId))!.LastSyncedHeight);
    }

    [Fact]
    public async Task A_gateway_proven_transaction_that_agrees_with_the_explorer_is_kept_as_it_is()
    {
        var rig = await RigAsync();
        var height = Cp + 131;
        var proven = new SyncTransaction(Id('a'), height, rig.Headers.HeaderAt(height)!.Hash.ToString(), "0100", SyncOrigin.GatewayProven);
        await rig.States.SaveAsync(new WalletSyncState(WalletId, Cp + 140, rig.Headers.HeaderAt(Cp + 140)!.Hash.ToString(), [proven], [], DateTimeOffset.UtcNow));

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 20)], []);

        var stored = (await rig.States.GetAsync(WalletId))!.Transactions.Single();
        Assert.Equal(SyncOrigin.GatewayProven, stored.Origin);
        Assert.Equal("0100", stored.Raw);
        Assert.False(stored.Disputed);
    }

    [Fact]
    public async Task A_gateway_proven_transaction_the_explorer_places_in_another_block_is_marked_disputed()
    {
        var rig = await RigAsync();
        var height = Cp + 131;
        var proven = new SyncTransaction(Id('a'), height, rig.Headers.HeaderAt(height)!.Hash.ToString(), "0100", SyncOrigin.GatewayProven);
        await rig.States.SaveAsync(new WalletSyncState(WalletId, Cp + 140, rig.Headers.HeaderAt(Cp + 140)!.Hash.ToString(), [proven], [], DateTimeOffset.UtcNow));

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 10)], []); // the explorer says block 141

        var stored = (await rig.States.GetAsync(WalletId))!.Transactions.Single();
        Assert.True(stored.Disputed);
        Assert.Equal(SyncOrigin.GatewayProven, stored.Origin);
        Assert.Equal(height, stored.Height); // the proven version is kept
    }

    [Fact]
    public async Task The_wallet_own_unconfirmed_sends_survive_an_explorer_refresh()
    {
        var rig = await RigAsync();
        var own = new SyncTransaction(Id('d'), 0, string.Empty, "0100", SyncOrigin.GatewayProven, IsOwnSend: true);
        await rig.States.SaveAsync(new WalletSyncState(WalletId, Cp + 140, rig.Headers.HeaderAt(Cp + 140)!.Hash.ToString(), [own], [], DateTimeOffset.UtcNow));

        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 5)], []);

        Assert.Contains((await rig.States.GetAsync(WalletId))!.Transactions, t => t.Txid == Id('d') && t.IsOwnSend);
    }

    [Fact]
    public async Task The_explorer_unspent_list_replaces_the_explorer_derived_outputs_but_keeps_proven_ones()
    {
        var rig = await RigAsync();
        var height = Cp + 131;
        var proven = new SyncTransaction(Id('a'), height, rig.Headers.HeaderAt(height)!.Hash.ToString(), "0100", SyncOrigin.GatewayProven);
        var state = new WalletSyncState(WalletId, Cp + 140, rig.Headers.HeaderAt(Cp + 140)!.Hash.ToString(),
            [proven, new SyncTransaction(Id('b'), Cp + 100, string.Empty, null, SyncOrigin.ExplorerSourced)],
            [new SyncOutpoint(Id('a'), 0, 100, "00"), new SyncOutpoint(Id('b'), 0, 200, "00")], DateTimeOffset.UtcNow);
        await rig.States.SaveAsync(state);

        // The explorer now lists a different unspent output and no longer lists b:0.
        await rig.Bootstrap.RecordAsync(WalletId, Cp + 150, [Tx('a', 20), Tx('b', 51)], [Utxo('e', 1, 300)]);

        var keys = (await rig.States.GetAsync(WalletId))!.UnspentOutpoints.Select(o => o.Key).Order().ToList();
        Assert.Equal([$"{Id('a')}:0", $"{Id('e')}:1"], keys);
    }

    // ---- never changes the explorer result ----

    [Fact]
    public async Task A_failure_while_recording_is_swallowed_so_it_can_never_change_the_explorer_result()
    {
        var storage = new ThrowingStorage();
        var headers = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet");
        await headers.InitializeAsync();
        var bootstrap = new GatewayStateBootstrap(new WalletSyncStateStore(storage), headers);

        var outcome = await bootstrap.TryRecordAsync(WalletId, Cp + 5, [], []);

        Assert.Equal(BootstrapOutcome.Failed, outcome);
    }

    private sealed class ThrowingStorage : ISecureStorage
    {
        public Task<string?> GetAsync(string key) => throw new IOException("disk");
        public Task SetAsync(string key, string value) => throw new IOException("disk");
        public Task RemoveAsync(string key) => throw new IOException("disk");
        public Task<bool> ContainsKeyAsync(string key) => throw new IOException("disk");
        public Task ClearAsync() => throw new IOException("disk");
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => throw new IOException("disk");
    }
}
