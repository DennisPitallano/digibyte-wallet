using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

public class AddressLabelServiceTests
{
    private readonly InMemorySecureStorage _storage = new();
    private readonly AddressLabelService _sut;

    public AddressLabelServiceTests()
    {
        _sut = new AddressLabelService(_storage);
    }

    [Fact]
    public async Task GetAllAsync_EmptyStorage_ReturnsEmptyDictionary()
    {
        var result = await _sut.GetAllAsync("wallet1");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetLabelAsync_StoresLabel_CanBeRetrieved()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");

        var label = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        Assert.Equal("Donations", label);
    }

    [Fact]
    public async Task SetLabelAsync_TrimsWhitespace()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "  My Label  ");

        var label = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        Assert.Equal("My Label", label);
    }

    [Fact]
    public async Task SetLabelAsync_EmptyLabel_RemovesEntry()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "");

        var label = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        Assert.Null(label);
    }

    [Fact]
    public async Task SetLabelAsync_WhitespaceOnlyLabel_RemovesEntry()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "   ");

        var label = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        Assert.Null(label);
    }

    [Fact]
    public async Task GetLabelAsync_UnknownAddress_ReturnsNull()
    {
        var label = await _sut.GetLabelAsync("wallet1", "dgb1qnonexistent");
        Assert.Null(label);
    }

    [Fact]
    public async Task SetLabelAsync_OverwritesExistingLabel()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Old Label");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "New Label");

        var label = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        Assert.Equal("New Label", label);
    }

    [Fact]
    public async Task RemoveLabelAsync_RemovesExistingLabel()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.RemoveLabelAsync("wallet1", "dgb1qaddr1");

        var label = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        Assert.Null(label);
    }

    [Fact]
    public async Task RemoveLabelAsync_NonExistent_DoesNotThrow()
    {
        await _sut.RemoveLabelAsync("wallet1", "dgb1qnonexistent");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllLabels()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr2", "Salary");

        var all = await _sut.GetAllAsync("wallet1");
        Assert.Equal(2, all.Count);
        Assert.Equal("Donations", all["dgb1qaddr1"].Label);
        Assert.Equal("Salary", all["dgb1qaddr2"].Label);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsAll()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr2", "Salary");

        var results = await _sut.SearchAsync("wallet1", "");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_MatchesByLabel()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr2", "Salary");

        var results = await _sut.SearchAsync("wallet1", "donat");
        Assert.Single(results);
        Assert.Equal("dgb1qaddr1", results[0].Address);
    }

    [Fact]
    public async Task SearchAsync_MatchesByAddress()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr2", "Salary");

        var results = await _sut.SearchAsync("wallet1", "addr2");
        Assert.Single(results);
        Assert.Equal("Salary", results[0].Label);
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitive()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Donations");

        var results = await _sut.SearchAsync("wallet1", "DONAT");
        Assert.Single(results);
    }

    [Fact]
    public async Task GetAllAsync_DifferentWallets_AreIsolated()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "W1 Label");
        await _sut.SetLabelAsync("wallet2", "dgb1qaddr1", "W2 Label");

        var label1 = await _sut.GetLabelAsync("wallet1", "dgb1qaddr1");
        var label2 = await _sut.GetLabelAsync("wallet2", "dgb1qaddr1");

        Assert.Equal("W1 Label", label1);
        Assert.Equal("W2 Label", label2);
    }

    [Fact]
    public async Task GetAllAsync_UsesCacheForSameWallet()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Cached");

        var result1 = await _sut.GetAllAsync("wallet1");
        var result2 = await _sut.GetAllAsync("wallet1");

        Assert.Same(result1, result2);
    }

    [Fact]
    public async Task SetLabelAsync_SetsUpdatedAt()
    {
        var before = DateTime.UtcNow;
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Test");
        var after = DateTime.UtcNow;

        var all = await _sut.GetAllAsync("wallet1");
        var updatedAt = all["dgb1qaddr1"].UpdatedAt;

        Assert.InRange(updatedAt, before, after);
    }

    [Fact]
    public async Task PersistsToStorage()
    {
        await _sut.SetLabelAsync("wallet1", "dgb1qaddr1", "Persisted");

        var sut2 = new AddressLabelService(_storage);
        var label = await sut2.GetLabelAsync("wallet1", "dgb1qaddr1");

        Assert.Equal("Persisted", label);
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
