using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Headers.Hashers;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The wallet's own copy of the header chain since the built-in checkpoint (specs/011-p2p-spv-gateway, T044): a header is stored only
/// after linkage, retarget and (once the wallet checks proof of work) proof-of-work checks pass; a reorganization takes headers back off.
/// </summary>
public class GatewayHeaderStoreTests
{
    private const int Cp = 24563100;

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

    private static readonly Lazy<Dictionary<int, byte[]>> Fixture = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private static BlockHeader80 Real(int height) => BlockHeader80.Parse(Fixture.Value[height]);

    private static IEnumerable<BlockHeader80> Range(int fromOffset, int toOffset) =>
        Enumerable.Range(Cp + fromOffset, toOffset - fromOffset + 1).Select(Real);

    private static GatewayHeaderStore NewStore(MemoryStorage storage, string network = "mainnet", IPowHasher? hasher = null, int chunk = 50) =>
        new(storage, Checkpoints.Mainnet, network, hasher, chunkSize: chunk);

    [Fact]
    public async Task A_new_store_starts_at_the_checkpoint()
    {
        var store = NewStore(new MemoryStorage());
        await store.InitializeAsync();

        Assert.Equal(Cp, store.TipHeight);
        Assert.Equal(Checkpoints.Mainnet.Hash, store.TipHash);
        Assert.Equal(System.Numerics.BigInteger.Zero, store.WorkSinceCheckpoint);
    }

    [Fact]
    public async Task Valid_headers_are_stored_and_survive_a_restart_across_chunk_boundaries()
    {
        var storage = new MemoryStorage();
        var store = NewStore(storage);
        await store.InitializeAsync();

        var result = await store.AppendRangeAsync(Range(1, 120));
        var work = store.WorkSinceCheckpoint;

        Assert.Null(result.Rejected);
        Assert.Equal(120, result.Accepted);
        Assert.Equal(Cp + 120, store.TipHeight);

        var reopened = NewStore(storage);
        await reopened.InitializeAsync();

        Assert.Equal(Cp + 120, reopened.TipHeight);
        Assert.Equal(Real(Cp + 120).Hash, reopened.TipHash);
        Assert.Equal(work, reopened.WorkSinceCheckpoint);
    }

    [Fact]
    public async Task A_bad_header_is_refused_and_nothing_at_or_after_it_is_stored()
    {
        var storage = new MemoryStorage();
        var store = NewStore(storage);
        await store.InitializeAsync();
        var raw = Real(Cp + 3).ToBytes();
        raw[72] ^= 0x01; // wrong difficulty
        var batch = new[] { Real(Cp + 1), Real(Cp + 2), BlockHeader80.Parse(raw), Real(Cp + 4) };

        var result = await store.AppendRangeAsync(batch);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(HeaderRejection.WrongDifficulty, result.Rejected);
        Assert.Equal(Cp + 2, store.TipHeight);

        var reopened = NewStore(storage);
        await reopened.InitializeAsync();
        Assert.Equal(Cp + 2, reopened.TipHeight);
    }

    [Fact]
    public async Task With_a_proof_of_work_hasher_a_header_with_too_little_work_is_refused()
    {
        var store = NewStore(new MemoryStorage(), hasher: PowHashers.Available);
        await store.InitializeAsync();
        var raw = Real(Cp + 1).ToBytes();
        raw[76] ^= 0xFF;

        var result = await store.AppendRangeAsync([BlockHeader80.Parse(raw)]);

        Assert.Equal(HeaderRejection.ProofOfWorkFailed, result.Rejected);
        Assert.Equal(Cp, store.TipHeight);
    }

    [Fact]
    public async Task A_reorganization_takes_headers_back_off_and_persists_the_shorter_chain()
    {
        var storage = new MemoryStorage();
        var store = NewStore(storage);
        await store.InitializeAsync();
        await store.AppendRangeAsync(Range(1, 80));

        await store.RewindAsync(Cp + 30);

        Assert.Equal(Cp + 30, store.TipHeight);
        Assert.Equal(Real(Cp + 30).Hash, store.TipHash);
        var reopened = NewStore(storage);
        await reopened.InitializeAsync();
        Assert.Equal(Cp + 30, reopened.TipHeight);

        // And it can grow again from there.
        Assert.Equal(10, (await reopened.AppendRangeAsync(Range(31, 40))).Accepted);
    }

    [Fact]
    public async Task Rewinding_below_the_checkpoint_is_refused()
    {
        var store = NewStore(new MemoryStorage());
        await store.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.RewindAsync(Cp - 1));
    }

    [Fact]
    public async Task Damaged_stored_data_is_discarded_and_the_store_starts_again_from_the_checkpoint()
    {
        var storage = new MemoryStorage();
        var store = NewStore(storage);
        await store.InitializeAsync();
        await store.AppendRangeAsync(Range(1, 60));
        var chunkKey = storage.Data.Keys.First(k => k.Contains("chunk"));
        var bytes = Convert.FromBase64String(storage.Data[chunkKey]);
        bytes[100] ^= 0xFF; // corrupt the second stored header
        storage.Data[chunkKey] = Convert.ToBase64String(bytes);

        var reopened = NewStore(storage);
        await reopened.InitializeAsync();

        Assert.Equal(Cp, reopened.TipHeight);
        // The store works again afterwards.
        Assert.Equal(5, (await reopened.AppendRangeAsync(Range(1, 5))).Accepted);
    }

    [Fact]
    public async Task Data_saved_for_a_different_checkpoint_is_not_used()
    {
        var storage = new MemoryStorage();
        var store = NewStore(storage);
        await store.InitializeAsync();
        await store.AppendRangeAsync(Range(1, 10));
        var metaKey = storage.Data.Keys.First(k => k.EndsWith(":meta"));
        storage.Data[metaKey] = storage.Data[metaKey].Replace(Cp.ToString(), (Cp - 5).ToString());

        var reopened = NewStore(storage);
        await reopened.InitializeAsync();

        Assert.Equal(Cp, reopened.TipHeight);
    }

    [Fact]
    public async Task Each_network_keeps_its_own_headers()
    {
        var storage = new MemoryStorage();
        var main = NewStore(storage, "mainnet");
        await main.InitializeAsync();
        await main.AppendRangeAsync(Range(1, 10));

        var other = NewStore(storage, "testnet");
        await other.InitializeAsync();

        Assert.Equal(Cp, other.TipHeight);
        Assert.Equal(Cp + 10, main.TipHeight);
    }

    [Fact]
    public async Task Headers_can_be_read_back_by_height_for_the_proof_checker()
    {
        var store = NewStore(new MemoryStorage());
        await store.InitializeAsync();
        await store.AppendRangeAsync(Range(1, 20));

        var range = store.HeadersFrom(Cp + 5, 3);

        Assert.Equal(3, range.Count);
        Assert.Equal(Real(Cp + 5).Hash, range[0].Hash);
        Assert.Equal(Real(Cp + 7).Hash, range[2].Hash);
        Assert.Equal(Real(Cp + 20).Hash, store.HeaderAt(Cp + 20)!.Hash);
        Assert.Null(store.HeaderAt(Cp + 21));
    }

    [Fact]
    public async Task Stored_data_never_holds_anything_but_public_headers()
    {
        var storage = new MemoryStorage();
        var store = NewStore(storage);
        await store.InitializeAsync();
        await store.AppendRangeAsync(Range(1, 10));

        Assert.All(storage.Data.Keys, k => Assert.StartsWith("gwhdr:", k));
    }
}
