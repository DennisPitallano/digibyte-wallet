using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>A restore scan can be interrupted and resumed, and its progress never leaves the device (T090, data-model: ScanProgress).</summary>
public class ScanProgressStoreTests
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

    private static SyncTransaction Tx(char c, int height) => new(new string(c, 64), height, new string('b', 64), "0100", SyncOrigin.GatewayProven);

    [Fact]
    public async Task A_new_scan_starts_at_its_start_height_with_nothing_found()
    {
        var store = new ScanProgressStore(new MemoryStorage());

        var progress = await store.StartOrResumeAsync("w1", 1000, ScanMode.Normal);

        Assert.Equal((1000, 1000, -1, -1), (progress.StartHeight, progress.NextHeight, progress.ReceiveHighestUsed, progress.ChangeHighestUsed));
        Assert.Equal((ScanMode.Normal, ScanSource.Gateway), (progress.Mode, progress.Source));
        Assert.Empty(progress.Transactions);
    }

    [Fact]
    public async Task The_resume_point_advances_only_through_a_finished_chunk_and_only_forward()
    {
        var store = new ScanProgressStore(new MemoryStorage());
        await store.StartOrResumeAsync("w1", 1000, ScanMode.Normal);

        var after = await store.AdvanceAsync("w1", 51_000, 3, 1, [Tx('a', 5000)]);

        Assert.Equal(51_000, after.NextHeight);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AdvanceAsync("w1", 51_000, 3, 1, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AdvanceAsync("w1", 40_000, 3, 1, []));
        Assert.Equal(51_000, (await store.GetAsync("w1"))!.NextHeight);
    }

    [Fact]
    public async Task An_interrupted_scan_resumes_where_it_stopped_with_what_it_had_found()
    {
        var storage = new MemoryStorage();
        var first = new ScanProgressStore(storage);
        await first.StartOrResumeAsync("w1", 1000, ScanMode.Normal);
        await first.AdvanceAsync("w1", 51_000, 4, 2, [Tx('a', 5000), Tx('b', 9000)]);

        // The app is closed and opened again.
        var resumed = await new ScanProgressStore(storage).StartOrResumeAsync("w1", 1000, ScanMode.Normal);

        Assert.Equal(51_000, resumed.NextHeight);
        Assert.Equal((4, 2), (resumed.ReceiveHighestUsed, resumed.ChangeHighestUsed));
        Assert.Equal(2, resumed.Transactions.Count);
    }

    [Fact]
    public async Task The_highest_used_indexes_never_go_down()
    {
        var store = new ScanProgressStore(new MemoryStorage());
        await store.StartOrResumeAsync("w1", 1000, ScanMode.Normal);
        await store.AdvanceAsync("w1", 2000, 9, 4, []);

        var after = await store.AdvanceAsync("w1", 3000, 2, 1, []);

        Assert.Equal((9, 4), (after.ReceiveHighestUsed, after.ChangeHighestUsed));
    }

    [Fact]
    public async Task A_scan_in_the_other_mode_starts_fresh_instead_of_resuming_the_wrong_one()
    {
        var store = new ScanProgressStore(new MemoryStorage());
        await store.StartOrResumeAsync("w1", 1000, ScanMode.Normal);
        await store.AdvanceAsync("w1", 51_000, 1, 1, []);

        var deep = await store.StartOrResumeAsync("w1", 1000, ScanMode.Deep);

        Assert.Equal(ScanMode.Deep, deep.Mode);
        Assert.Equal(1000, deep.NextHeight);
    }

    [Fact]
    public async Task A_change_of_source_is_recorded()
    {
        var store = new ScanProgressStore(new MemoryStorage());
        await store.StartOrResumeAsync("w1", 1000, ScanMode.Normal);

        await store.SetSourceAsync("w1", ScanSource.Explorer);

        Assert.Equal(ScanSource.Explorer, (await store.GetAsync("w1"))!.Source);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"walletId\":\"other\",\"startHeight\":1,\"nextHeight\":2,\"receiveHighestUsed\":0,\"changeHighestUsed\":0,\"mode\":\"normal\",\"source\":\"gateway\",\"transactions\":[]}")]
    [InlineData("{\"walletId\":\"w1\",\"startHeight\":100,\"nextHeight\":50,\"receiveHighestUsed\":0,\"changeHighestUsed\":0,\"mode\":\"normal\",\"source\":\"gateway\",\"transactions\":[]}")]
    public async Task A_damaged_or_foreign_record_is_discarded_and_the_scan_starts_again(string stored)
    {
        var storage = new MemoryStorage();
        storage.Data[ScanProgressStore.KeyFor("w1")] = stored;
        var store = new ScanProgressStore(storage);

        Assert.Null(await store.GetAsync("w1"));
        Assert.False(storage.Data.ContainsKey(ScanProgressStore.KeyFor("w1")));
    }

    [Fact]
    public async Task Progress_is_stored_only_on_the_device_under_its_own_prefix_and_deleted_on_request()
    {
        var storage = new MemoryStorage();
        var store = new ScanProgressStore(storage);
        await store.StartOrResumeAsync("w1", 1000, ScanMode.Normal);
        await store.StartOrResumeAsync("w2", 2000, ScanMode.Deep);

        Assert.All(storage.Data.Keys, k => Assert.StartsWith(ScanProgressStore.KeyPrefix, k));

        await store.DeleteAsync("w1");

        Assert.Null(await store.GetAsync("w1"));
        Assert.NotNull(await store.GetAsync("w2"));
    }

    [Fact]
    public async Task Advancing_a_scan_that_was_never_started_is_an_error()
    {
        var store = new ScanProgressStore(new MemoryStorage());

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AdvanceAsync("nobody", 10, 0, 0, []));
    }
}
