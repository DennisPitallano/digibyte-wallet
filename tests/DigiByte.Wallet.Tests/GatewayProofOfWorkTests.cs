using System.Diagnostics;
using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Headers.Hashers;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using Xunit.Abstractions;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The wallet checking proof of work itself (spec FR-013, T112, T113). The cost measured here is NATIVE: it says nothing about
/// WebAssembly, which is where the decision to show "verified" depends on it.
/// </summary>
public class GatewayProofOfWorkTests(ITestOutputHelper output)
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

    private sealed record Row(int Height, byte[] Raw, string Algo);

    private static readonly Lazy<List<Row>> Run = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .Select(e => new Row(e.GetProperty("height").GetInt32(), Convert.FromHexString(e.GetProperty("header").GetString()!), e.GetProperty("algo").GetString()!))
            .ToList();
    });

    [Fact]
    public async Task Every_real_header_after_the_checkpoint_passes_the_full_check_with_proof_of_work_on()
    {
        var store = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet", PowHashers.Available, chunkSize: 100);
        await store.InitializeAsync();
        var headers = Run.Value.Where(r => r.Height > Cp).OrderBy(r => r.Height).Select(r => BlockHeader80.Parse(r.Raw)).ToList();

        var result = await store.AppendRangeAsync(headers);

        Assert.Null(result.Rejected);
        Assert.Equal(headers.Count, result.Accepted);
    }

    [Fact]
    public async Task A_header_with_a_changed_nonce_fails_the_work_check_when_it_is_on_and_passes_when_it_is_off()
    {
        var raw = (byte[])Run.Value.First(r => r.Height == Cp + 1).Raw.Clone();
        raw[76] ^= 0xFF;
        var bad = BlockHeader80.Parse(raw);

        var checking = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet", PowHashers.Available);
        await checking.InitializeAsync();
        var skipping = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet", hasher: null);
        await skipping.InitializeAsync();

        Assert.Equal(HeaderRejection.ProofOfWorkFailed, (await checking.AppendRangeAsync([bad])).Rejected);
        Assert.Null((await skipping.AppendRangeAsync([bad])).Rejected);
    }

    [Fact]
    public void Every_active_algorithm_has_a_hasher_so_there_is_no_partial_mode()
    {
        Assert.True(PowHashers.AllActiveAlgorithmsSupported);
        var algorithms = Run.Value.Select(r => r.Algo).Distinct().ToList();
        Assert.True(algorithms.Count >= 5, "the fixture should cover all five active algorithms");
    }

    [Fact]
    public async Task Native_cost_per_header_for_each_algorithm_is_measured_and_stays_small()
    {
        var hashers = PowHashers.Available;
        var byAlgo = Run.Value.GroupBy(r => r.Algo).ToList();
        var worst = 0.0;
        foreach (var group in byAlgo)
        {
            var headers = group.Select(r => BlockHeader80.Parse(r.Raw)).ToList();
            // Warm up once so the first call's setup is not counted.
            hashers.Hash(headers[0]);
            var clock = Stopwatch.StartNew();
            foreach (var header in headers)
                hashers.Hash(header);
            clock.Stop();
            var perHeader = clock.Elapsed.TotalMilliseconds / headers.Count;
            worst = Math.Max(worst, perHeader);
            output.WriteLine($"{group.Key,-8} {headers.Count,4} headers  {perHeader,8:F3} ms per header (native)");
        }
        output.WriteLine($"worst algorithm: {worst:F3} ms per header (native). WebAssembly is NOT measured by this test.");

        Assert.True(worst < 50, $"native cost per header was {worst} ms");
        await Task.CompletedTask;
    }

    [Fact]
    public void The_check_is_off_unless_asked_for_and_the_verified_label_stays_closed()
    {
        Assert.False(P2pGatewayOptions.FromConfiguration(_ => null).CheckProofOfWork);
        Assert.True(P2pGatewayOptions.FromConfiguration(k => k == "SpvGateway:CheckProofOfWork" ? "true" : null).CheckProofOfWork);
        // Whatever the switch says, nothing is called verified until the cost under WebAssembly has been measured and the flag is set.
        Assert.False(GatewayCapabilities.WalletPowVerified);
    }
}
