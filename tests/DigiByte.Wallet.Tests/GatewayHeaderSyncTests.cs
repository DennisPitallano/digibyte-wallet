using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>The wallet follows the gateway chain through its own header store and keeps its own branch when the gateway offers less work (T067, T055).</summary>
public class GatewayHeaderSyncTests
{
    private const int Cp = 24563100;

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

    private static readonly Lazy<Dictionary<int, byte[]>> Fixture = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private static BlockHeader80 Real(int height) => BlockHeader80.Parse(Fixture.Value[height]);

    private static List<BlockHeader80> RealRange(int from, int to) => Enumerable.Range(Cp + from, to - from + 1).Select(Real).ToList();

    /// <summary>A branch that leaves the real chain after <paramref name="forkAfter"/>: same time, algorithm and difficulty, other nonce.</summary>
    private static List<BlockHeader80> Fork(int forkAfter, int length)
    {
        var result = new List<BlockHeader80>();
        var previous = Real(Cp + forkAfter).Hash;
        for (var i = 1; i <= length; i++)
        {
            var raw = (byte[])Fixture.Value[Cp + forkAfter + i].Clone();
            previous.ToBytes().CopyTo(raw, 4);
            raw[76] ^= 0xA5;
            var header = BlockHeader80.Parse(raw);
            result.Add(header);
            previous = header.Hash;
        }
        return result;
    }

    /// <summary>A gateway whose chain is a list of headers after the checkpoint, which a test can replace.</summary>
    private sealed class ScriptedFeed : IGatewayHeaderFeed
    {
        public List<BlockHeader80> Chain { get; set; } = [];
        public int Calls { get; private set; }

        public Task<GatewayHeaders> GetHeadersAsync(int from, int count, CancellationToken cancellationToken = default)
        {
            Calls++;
            var slice = Chain.Skip(from - (Cp + 1)).Take(count).ToList();
            return Task.FromResult(new GatewayHeaders(slice, Cp + Chain.Count, Chain.Count == 0 ? string.Empty : Chain[^1].Hash.ToString()));
        }
    }

    private static async Task<(GatewayHeaderSync Sync, GatewayHeaderStore Store, ScriptedFeed Feed)> RigAsync()
    {
        var store = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet", chunkSize: 50);
        await store.InitializeAsync();
        var feed = new ScriptedFeed();
        return (new GatewayHeaderSync(store, feed), store, feed);
    }

    [Fact]
    public async Task Headers_are_taken_from_the_checkpoint_to_the_gateway_tip()
    {
        var (sync, store, feed) = await RigAsync();
        feed.Chain = RealRange(1, 120);

        var result = await sync.SyncAsync();

        Assert.Equal(120, result.Appended);
        Assert.Equal(Cp + 120, store.TipHeight);
        Assert.Equal(0, result.ReorgDepth);
    }

    [Fact]
    public async Task A_second_sync_asks_only_for_what_is_new()
    {
        var (sync, store, feed) = await RigAsync();
        feed.Chain = RealRange(1, 60);
        await sync.SyncAsync();

        feed.Chain = RealRange(1, 90);
        var result = await sync.SyncAsync();

        Assert.Equal(30, result.Appended);
        Assert.Equal(Cp + 90, store.TipHeight);
    }

    [Fact]
    public async Task A_longer_branch_from_the_gateway_replaces_the_wallet_tip_and_reports_the_depth()
    {
        var (sync, store, feed) = await RigAsync();
        feed.Chain = RealRange(1, 40);
        await sync.SyncAsync();

        feed.Chain = [.. RealRange(1, 30), .. Fork(30, 25)];
        var result = await sync.SyncAsync();

        Assert.Equal(Cp + 55, store.TipHeight);
        Assert.Equal(10, result.ReorgDepth);
        Assert.False(result.KeptOwnChain);
        Assert.Equal(feed.Chain[^1].Hash, store.TipHash);
    }

    [Fact]
    public async Task A_gateway_that_is_behind_leaves_the_wallet_chain_alone()
    {
        var (sync, store, feed) = await RigAsync();
        feed.Chain = RealRange(1, 50);
        await sync.SyncAsync();
        var ownTip = store.TipHash;

        feed.Chain = [.. RealRange(1, 30), .. Fork(30, 5)]; // 35 blocks against the wallet 50
        var result = await sync.SyncAsync();

        Assert.Equal(0, result.Appended);
        Assert.Equal(Cp + 50, store.TipHeight);
        Assert.Equal(ownTip, store.TipHash);
    }

    [Fact]
    public async Task A_header_that_fails_the_checks_makes_the_sync_throw_and_keeps_what_was_good()
    {
        var (sync, store, feed) = await RigAsync();
        var chain = RealRange(1, 30);
        var raw = chain[10].ToBytes();
        raw[72] ^= 0x01; // wrong difficulty
        chain[10] = BlockHeader80.Parse(raw);
        feed.Chain = chain;

        var error = await Assert.ThrowsAsync<GatewayHeadersRejectedException>(() => sync.SyncAsync());

        Assert.Equal(HeaderRejection.WrongDifficulty, error.Reason);
        Assert.Equal(Cp + 10, store.TipHeight);
    }

    [Fact]
    public async Task A_gateway_with_nothing_to_offer_changes_nothing()
    {
        var (sync, store, feed) = await RigAsync();

        var result = await sync.SyncAsync();

        Assert.Equal(0, result.Appended);
        Assert.Equal(Cp, store.TipHeight);
    }

    [Fact]
    public async Task A_chain_that_never_meets_the_wallet_chain_is_refused()
    {
        var (sync, store, feed) = await RigAsync();
        feed.Chain = RealRange(1, 30);
        await sync.SyncAsync();
        // A whole chain that does not link to the checkpoint.
        var strange = Fork(0, 40);
        var raw = strange[0].ToBytes();
        raw[4] ^= 0xFF;
        strange[0] = BlockHeader80.Parse(raw);
        feed.Chain = strange;

        await Assert.ThrowsAsync<GatewayHeadersRejectedException>(() => sync.SyncAsync());
    }
}
