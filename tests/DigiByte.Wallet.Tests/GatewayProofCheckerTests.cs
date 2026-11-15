using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>A proof is accepted only against the wallet own verified headers (T049, spec SC-001), using real mainnet headers and proofs.</summary>
public class GatewayProofCheckerTests
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

    private static readonly Lazy<Dictionary<int, byte[]>> Headers = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private sealed record Vector(int Height, string BlockId, string Txid, int TxIndex, string[] Branch, string RawTx);

    private static readonly Lazy<List<Vector>> Vectors = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "merkle-mainnet.json")));
        return doc.RootElement.GetProperty("vectors").EnumerateArray().Select(e => new Vector(
            e.GetProperty("height").GetInt32(), e.GetProperty("blockId").GetString()!, e.GetProperty("txid").GetString()!,
            e.GetProperty("txIndex").GetInt32(), e.GetProperty("branch").EnumerateArray().Select(b => b.GetString()!).ToArray(),
            e.GetProperty("rawTx").GetString()!)).ToList();
    });

    private static GatewayTransactionProof ProofOf(Vector v) => new(v.Txid, v.Height, v.BlockId, v.RawTx, v.TxIndex, v.Branch);

    /// <summary>A header store that has verified headers from the checkpoint up to <paramref name="upTo"/>.</summary>
    private static async Task<GatewayHeaderStore> StoreAsync(int upTo)
    {
        var store = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet", chunkSize: 50);
        await store.InitializeAsync();
        var result = await store.AppendRangeAsync(Enumerable.Range(Cp + 1, upTo - Cp).Select(h => BlockHeader80.Parse(Headers.Value[h])));
        Assert.Null(result.Rejected);
        return store;
    }

    private static Vector Sample => Vectors.Value.First(v => v.TxIndex > 0 && v.Branch.Length > 0);

    [Fact]
    public async Task Every_real_proof_is_accepted_against_the_wallet_own_headers()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 130));

        foreach (var vector in Vectors.Value.Where(v => v.Height <= Cp + 130))
            Assert.Equal(ProofCheckResult.Valid, checker.Check(ProofOf(vector)));
    }

    [Fact]
    public async Task One_changed_byte_in_the_branch_is_rejected_and_is_the_sources_fault()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 20));
        var vector = Vectors.Value.First(v => v.Height <= Cp + 20 && v.Branch.Length > 0);
        var branch = vector.Branch.ToArray();
        branch[0] = (branch[0][0] == 'a' ? 'b' : 'a') + branch[0][1..];

        var result = checker.Check(ProofOf(vector) with { Branch = branch });

        Assert.Equal(ProofCheckResult.ProofRejected, result);
        Assert.True(result.IsSourceFault());
    }

    [Fact]
    public async Task One_changed_byte_in_the_transaction_no_longer_matches_its_id()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 20));
        var vector = Vectors.Value.First(v => v.Height <= Cp + 20);
        var raw = vector.RawTx.ToCharArray();
        raw[^3] = raw[^3] == '0' ? '1' : '0'; // inside the lock time, which is part of the id

        var result = checker.Check(ProofOf(vector) with { Raw = new string(raw) });

        Assert.Equal(ProofCheckResult.TxidMismatch, result);
        Assert.True(result.IsSourceFault());
    }

    [Fact]
    public async Task A_proof_for_a_height_the_wallet_has_not_verified_is_not_accepted_and_is_not_held_against_the_source()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 5));
        var vector = Vectors.Value.First(v => v.Height > Cp + 5);

        var result = checker.Check(ProofOf(vector));

        Assert.Equal(ProofCheckResult.HeaderNotVerified, result);
        Assert.False(result.IsSourceFault());
    }

    [Fact]
    public async Task A_block_hash_that_is_not_the_wallets_header_at_that_height_is_rejected()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 20));
        var vector = Vectors.Value.First(v => v.Height <= Cp + 20);

        var result = checker.Check(ProofOf(vector) with { BlockHash = new string('1', 64) });

        Assert.Equal(ProofCheckResult.BlockHashMismatch, result);
        Assert.True(result.IsSourceFault());
    }

    [Fact]
    public async Task A_proof_claimed_for_a_different_height_than_its_block_is_rejected()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 130));
        var vector = Vectors.Value.First(v => v.Height <= Cp + 20);

        var result = checker.Check(ProofOf(vector) with { Height = vector.Height + 1 });

        Assert.True(result.IsSourceFault());
        Assert.NotEqual(ProofCheckResult.Valid, result);
    }

    [Fact]
    public async Task A_position_that_does_not_fit_the_branch_is_rejected()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 20));
        var vector = Vectors.Value.First(v => v.Height <= Cp + 20 && v.Branch.Length > 0);

        Assert.Equal(ProofCheckResult.ProofRejected, checker.Check(ProofOf(vector) with { Index = vector.TxIndex + 1 }));
        Assert.Equal(ProofCheckResult.Malformed, checker.Check(ProofOf(vector) with { Index = -1 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("0102")]
    public async Task Bytes_that_are_not_a_transaction_are_malformed(string raw)
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 5));

        Assert.Equal(ProofCheckResult.Malformed, checker.Check(ProofOf(Sample) with { Raw = raw }));
    }

    [Fact]
    public async Task Hashes_that_are_not_hashes_are_malformed_not_a_crash()
    {
        var checker = new GatewayProofChecker(await StoreAsync(Cp + 20));
        var vector = Vectors.Value.First(v => v.Height <= Cp + 20 && v.Branch.Length > 0);

        Assert.Equal(ProofCheckResult.Malformed, checker.Check(ProofOf(vector) with { Branch = ["not-a-hash"] }));
        Assert.Equal(ProofCheckResult.Malformed, checker.Check(ProofOf(vector) with { Txid = "short" }));
    }
}
