using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Tests for TransactionTracker.UpdateConfirmationsAsync — the core logic
/// that drives the live confirmation poller on the Home page.
/// DGB's ~15-second block time means pending txs progress quickly through
/// 6 confirmations; these tests verify the tracker keeps UI state accurate.
/// </summary>
public class ConfirmationPollerTests
{
    private readonly InMemorySecureStorage _storage = new();
    private readonly TransactionTracker _sut;

    public ConfirmationPollerTests()
    {
        _sut = new TransactionTracker(_storage);
        _sut.SetActiveWallet("poll-wallet");
    }

    // ─── UpdateConfirmationsAsync: updates confirmations ─────────────────────

    [Fact]
    public async Task UpdateConfirmations_PendingTx_GetsUpdated()
    {
        await _sut.RecordSendAsync("tx001", "dgb1qdest", 100_000_000, 10_000);

        var blockchain = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx001"] = 3,
        });

        await _sut.UpdateConfirmationsAsync(blockchain);

        var all = await _sut.GetAllAsync();
        Assert.Equal(3, all[0].Confirmations);
    }

    [Fact]
    public async Task UpdateConfirmations_FullyConfirmedTx_IsNotReQueried()
    {
        // Arrange — tx already at 6 confirmations in the tracker
        await _sut.RecordSendAsync("tx002", "dgb1qdest", 100_000_000, 10_000);
        var all = await _sut.GetAllAsync();
        all[0].Confirmations = 6;
        // Manually persist the 6-confirmation state
        var storageKey = "tx_history_poll-wallet";
        await _storage.SetAsync(storageKey,
            System.Text.Json.JsonSerializer.Serialize(all));

        // Blockchain returns a *different* value — should NOT be queried for confirmed txs
        var blockchain = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx002"] = 100, // would be wrong if applied
        });

        // Re-create tracker to pick up stored confirmations
        var tracker2 = new TransactionTracker(_storage);
        tracker2.SetActiveWallet("poll-wallet");
        await tracker2.UpdateConfirmationsAsync(blockchain);

        var result = await tracker2.GetAllAsync();
        // Should still be 6 — confirmed txs are excluded from polling (Confirmations < 6 guard)
        Assert.Equal(6, result[0].Confirmations);
        Assert.Equal(0, blockchain.QueryCount); // no network call made
    }

    [Fact]
    public async Task UpdateConfirmations_TxNotFoundOnChain_ConfirmationsUnchanged()
    {
        await _sut.RecordSendAsync("tx003", "dgb1qdest", 100_000_000, 10_000);

        // Blockchain returns null for this txid (not found / not yet indexed)
        var blockchain = new StubBlockchain(new Dictionary<string, int>());

        await _sut.UpdateConfirmationsAsync(blockchain);

        var all = await _sut.GetAllAsync();
        Assert.Equal(0, all[0].Confirmations);
    }

    [Fact]
    public async Task UpdateConfirmations_MultiplePartialUpdates_AccumulateCorrectly()
    {
        await _sut.RecordSendAsync("tx004", "dgb1qdest", 100_000_000, 10_000);
        await _sut.RecordReceiveAsync("tx005", "dgb1qfrom", 50_000_000);

        // Poll 1 — first block arrives
        var blockchainPass1 = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx004"] = 1,
            ["tx005"] = 1,
        });
        await _sut.UpdateConfirmationsAsync(blockchainPass1);

        // Poll 2 — two more blocks
        var blockchainPass2 = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx004"] = 3,
            ["tx005"] = 3,
        });
        await _sut.UpdateConfirmationsAsync(blockchainPass2);

        var all = await _sut.GetAllAsync();
        Assert.All(all, tx => Assert.Equal(3, tx.Confirmations));
    }

    [Fact]
    public async Task UpdateConfirmations_PersistsToDisk()
    {
        await _sut.RecordSendAsync("tx006", "dgb1qdest", 100_000_000, 10_000);

        var blockchain = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx006"] = 5,
        });
        await _sut.UpdateConfirmationsAsync(blockchain);

        // Re-instantiate to prove it was written to storage
        var freshTracker = new TransactionTracker(_storage);
        freshTracker.SetActiveWallet("poll-wallet");
        var all = await freshTracker.GetAllAsync();

        Assert.Equal(5, all[0].Confirmations);
    }

    [Fact]
    public async Task UpdateConfirmations_EmptyHistory_NoOp()
    {
        var blockchain = new StubBlockchain(new Dictionary<string, int>());

        // Should not throw even with nothing to update
        await _sut.UpdateConfirmationsAsync(blockchain);

        Assert.Equal(0, blockchain.QueryCount);
    }

    [Fact]
    public async Task UpdateConfirmations_MixedConfirmedAndPending_OnlyQueriesPending()
    {
        await _sut.RecordSendAsync("pending", "dgb1qdest", 100_000, 1000);
        await _sut.RecordReceiveAsync("confirmed", "dgb1qfrom", 200_000);

        // Manually set "confirmed" to 6 in storage
        var all = await _sut.GetAllAsync();
        all.First(t => t.TxId == "confirmed").Confirmations = 6;
        await _storage.SetAsync("tx_history_poll-wallet",
            System.Text.Json.JsonSerializer.Serialize(all));

        var tracker2 = new TransactionTracker(_storage);
        tracker2.SetActiveWallet("poll-wallet");

        var blockchain = new StubBlockchain(new Dictionary<string, int>
        {
            ["pending"] = 2,
        });
        await tracker2.UpdateConfirmationsAsync(blockchain);

        var result = await tracker2.GetAllAsync();
        Assert.Equal(2, result.First(t => t.TxId == "pending").Confirmations);
        Assert.Equal(6, result.First(t => t.TxId == "confirmed").Confirmations);
        // Only the pending tx was queried
        Assert.Equal(1, blockchain.QueryCount);
    }

    [Fact]
    public async Task UpdateConfirmations_SameCount_NoStorageWrite()
    {
        await _sut.RecordSendAsync("tx007", "dgb1qdest", 100_000_000, 10_000);

        // Both passes return 2 — second should not cause a storage write
        var blockchain = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx007"] = 2,
        });
        await _sut.UpdateConfirmationsAsync(blockchain);
        var initialStorageVersion = await _storage.GetAsync("tx_history_poll-wallet");

        // Second update with same value
        var blockchain2 = new StubBlockchain(new Dictionary<string, int>
        {
            ["tx007"] = 2,
        });
        await _sut.UpdateConfirmationsAsync(blockchain2);
        var secondStorageVersion = await _storage.GetAsync("tx_history_poll-wallet");

        // Content should be identical (no unnecessary write)
        Assert.Equal(initialStorageVersion, secondStorageVersion);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fake IBlockchainService that returns configurable confirmation counts
    /// and tracks how many times GetTransactionAsync was called.
    /// </summary>
    private class StubBlockchain : IBlockchainService
    {
        private readonly Dictionary<string, int> _confirmations;
        public int QueryCount { get; private set; }

        public StubBlockchain(Dictionary<string, int> confirmationsByTxId)
        {
            _confirmations = confirmationsByTxId;
        }

        public Task<TransactionInfo?> GetTransactionAsync(string txId)
        {
            QueryCount++;
            if (!_confirmations.TryGetValue(txId, out var confs))
                return Task.FromResult<TransactionInfo?>(null);

            return Task.FromResult<TransactionInfo?>(new TransactionInfo
            {
                TxId = txId,
                Confirmations = confs,
                Timestamp = DateTime.UtcNow,
                FeeSatoshis = 0,
                Inputs = [],
                Outputs = [],
            });
        }

        // Remaining interface members — not exercised by these tests
        public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("faketxid");
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
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
}
