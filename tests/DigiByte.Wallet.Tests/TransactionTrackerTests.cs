using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

public class TransactionTrackerTests
{
    private readonly InMemorySecureStorage _storage = new();
    private readonly TransactionTracker _sut;

    public TransactionTrackerTests()
    {
        _sut = new TransactionTracker(_storage);
        _sut.SetActiveWallet("test-wallet");
    }

    [Fact]
    public async Task GetAllAsync_EmptyStorage_ReturnsEmptyList()
    {
        var result = await _sut.GetAllAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task RecordSendAsync_StoresTransaction()
    {
        await _sut.RecordSendAsync("tx123", "dgb1qdest", 100_000_000, 10_000, "Test send");

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("tx123", all[0].TxId);
        Assert.Equal(TransactionDirection.Sent, all[0].Direction);
        Assert.Equal(100_000_000, all[0].AmountSatoshis);
        Assert.Equal(10_000, all[0].FeeSatoshis);
        Assert.Equal("dgb1qdest", all[0].CounterpartyAddress);
        Assert.Equal("Test send", all[0].Memo);
        Assert.Equal(0, all[0].Confirmations);
    }

    [Fact]
    public async Task RecordSendAsync_NullMemo_StoresNull()
    {
        await _sut.RecordSendAsync("tx123", "dgb1qdest", 100_000_000, 10_000);

        var all = await _sut.GetAllAsync();
        Assert.Null(all[0].Memo);
    }

    [Fact]
    public async Task RecordSendAsync_DoesNotDuplicate()
    {
        await _sut.RecordSendAsync("tx123", "dgb1qdest", 100_000_000, 10_000);
        await _sut.RecordSendAsync("tx123", "dgb1qdest", 100_000_000, 10_000);

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task RecordReceiveAsync_StoresTransaction()
    {
        await _sut.RecordReceiveAsync("tx456", "dgb1qfrom", 50_000_000);

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("tx456", all[0].TxId);
        Assert.Equal(TransactionDirection.Received, all[0].Direction);
        Assert.Equal(50_000_000, all[0].AmountSatoshis);
        Assert.Equal("dgb1qfrom", all[0].CounterpartyAddress);
    }

    [Fact]
    public async Task RecordReceiveAsync_DoesNotDuplicate()
    {
        await _sut.RecordReceiveAsync("tx456", "dgb1qfrom", 50_000_000);
        await _sut.RecordReceiveAsync("tx456", "dgb1qfrom", 50_000_000);

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task MultipleTransactions_InsertedInReverseOrder()
    {
        await _sut.RecordSendAsync("tx1", "dgb1qdest1", 100_000, 1000);
        await _sut.RecordReceiveAsync("tx2", "dgb1qfrom", 200_000);

        var all = await _sut.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("tx2", all[0].TxId);
        Assert.Equal("tx1", all[1].TxId);
    }

    [Fact]
    public async Task SetActiveWallet_ClearsCacheAndSwitchesContext()
    {
        await _sut.RecordSendAsync("tx_w1", "dgb1qdest", 100_000, 1000);

        _sut.SetActiveWallet("other-wallet");
        var all = await _sut.GetAllAsync();
        Assert.Empty(all);

        _sut.SetActiveWallet("test-wallet");
        all = await _sut.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("tx_w1", all[0].TxId);
    }

    [Fact]
    public async Task PersistsToStorage()
    {
        await _sut.RecordSendAsync("tx_persist", "dgb1qdest", 500_000, 5000, "Consolidation");

        var sut2 = new TransactionTracker(_storage);
        sut2.SetActiveWallet("test-wallet");

        var all = await sut2.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("tx_persist", all[0].TxId);
        Assert.Equal("Consolidation", all[0].Memo);
    }

    [Fact]
    public async Task ConsolidationMemo_RecordedCorrectly()
    {
        var memo = "UTXO consolidation (15 → 1)";
        await _sut.RecordSendAsync("tx_consol", "dgb1qself", 10_000_000, 50_000, memo);

        var all = await _sut.GetAllAsync();
        Assert.Equal(memo, all[0].Memo);
    }

    [Fact]
    public async Task AmountDgb_CalculatesCorrectly()
    {
        await _sut.RecordSendAsync("tx1", "dgb1qdest", 123_456_789, 10_000);

        var all = await _sut.GetAllAsync();
        Assert.Equal(1.23456789m, all[0].AmountDgb);
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
