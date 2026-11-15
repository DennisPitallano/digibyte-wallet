using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>Explorer-sourced transactions become gateway-proven one proof at a time, checked against the wallet own headers (T074, FR-012a).</summary>
public class GatewayProofUpgraderTests
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

    private static readonly Lazy<List<GatewayTransactionProof>> Proofs = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "merkle-mainnet.json")));
        return doc.RootElement.GetProperty("vectors").EnumerateArray().Select(e => new GatewayTransactionProof(
            e.GetProperty("txid").GetString()!, e.GetProperty("height").GetInt32(), e.GetProperty("blockId").GetString()!, e.GetProperty("rawTx").GetString()!,
            e.GetProperty("txIndex").GetInt32(), e.GetProperty("branch").EnumerateArray().Select(b => b.GetString()!).ToList())).ToList();
    });

    private sealed class Source : IGatewayProofSource
    {
        public List<GatewayTransactionProof> Known { get; } = [];
        public List<int> BatchSizes { get; } = [];
        public List<(string Txid, int Height)> Asked { get; } = [];
        public Func<GatewayProofsAnswer, GatewayProofsAnswer>? Tamper { get; set; }

        public Task<GatewayProofsAnswer> GetProofsAsync(IReadOnlyCollection<(string Txid, int Height)> items, CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(items.Count);
            Asked.AddRange(items);
            var proofs = new List<GatewayTransactionProof>();
            var unavailable = new List<(string, string)>();
            foreach (var (txid, height) in items)
            {
                var found = Known.FirstOrDefault(p => p.Txid == txid && p.Height == height);
                if (found is not null)
                    proofs.Add(found);
                else
                    unavailable.Add((txid, "not_in_block"));
            }
            var answer = new GatewayProofsAnswer(proofs, unavailable);
            return Task.FromResult(Tamper is null ? answer : Tamper(answer));
        }
    }

    private sealed record Rig(GatewayProofUpgrader Upgrader, Source Source, WalletSyncStateStore States, FakeTime Time, bool[] Allowed);

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<Rig> RigAsync(IReadOnlyList<SyncTransaction> transactions, int headersUpTo = Cp + 250, int maxPerRun = 100)
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        await headers.AppendRangeAsync(Enumerable.Range(Cp + 1, headersUpTo - Cp).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        var states = new WalletSyncStateStore(storage);
        await states.SaveAsync(new WalletSyncState(WalletId, Math.Min(headersUpTo, Cp + 240), headers.HeaderAt(Math.Min(headersUpTo, Cp + 240))!.Hash.ToString(), transactions, [], DateTimeOffset.UtcNow));
        var source = new Source();
        source.Known.AddRange(Proofs.Value);
        var time = new FakeTime();
        var allowed = new[] { true };
        return new Rig(new GatewayProofUpgrader(source, new GatewayProofChecker(headers), states, () => allowed[0], time, TimeSpan.FromMinutes(1), maxPerRun), source, states, time, allowed);
    }

    private static SyncTransaction FromExplorer(GatewayTransactionProof p, string? blockHash = null) =>
        new(p.Txid, p.Height, blockHash ?? p.BlockHash, null, SyncOrigin.ExplorerSourced);

    [Fact]
    public async Task A_proof_that_checks_turns_an_explorer_sourced_transaction_into_a_gateway_proven_one_with_its_raw_bytes()
    {
        var proof = Proofs.Value[1];
        var rig = await RigAsync([FromExplorer(proof)]);

        var result = await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal(1, result.Upgraded);
        var stored = (await rig.States.GetAsync(WalletId))!.Transactions.Single();
        Assert.Equal(SyncOrigin.GatewayProven, stored.Origin);
        Assert.Equal(proof.Raw, stored.Raw);
        Assert.Equal(proof.BlockHash, stored.BlockHash);
        Assert.False(stored.Disputed);
    }

    [Fact]
    public async Task Something_the_gateway_cannot_prove_stays_unchecked()
    {
        var known = Proofs.Value[1];
        var unknown = new SyncTransaction(new string('7', 64), Cp + 50, new string('8', 64), null, SyncOrigin.ExplorerSourced);
        var rig = await RigAsync([FromExplorer(known), unknown]);

        var result = await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal((1, 1), (result.Upgraded, result.Unavailable));
        var left = (await rig.States.GetAsync(WalletId))!.Transactions.Single(t => t.Txid == unknown.Txid);
        Assert.Equal(SyncOrigin.ExplorerSourced, left.Origin);
    }

    [Fact]
    public async Task Requests_go_in_batches_of_at_most_twenty_newest_first()
    {
        var many = Enumerable.Range(0, 45).Select(i => new SyncTransaction(new string((char)('a' + i % 6), 60) + i.ToString("D4"), Cp + 10 + i, new string('9', 64), null, SyncOrigin.ExplorerSourced)).ToList();
        var rig = await RigAsync(many);

        await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal([20, 20, 5], rig.Source.BatchSizes.ToArray());
        Assert.Equal(Cp + 10 + 44, rig.Source.Asked[0].Height);
    }

    [Fact]
    public async Task A_run_handles_only_a_bounded_number_of_transactions()
    {
        var many = Enumerable.Range(0, 45).Select(i => new SyncTransaction(new string('a', 60) + i.ToString("D4"), Cp + 10 + i, new string('9', 64), null, SyncOrigin.ExplorerSourced)).ToList();
        var rig = await RigAsync(many, maxPerRun: 25);

        await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal(25, rig.Source.Asked.Count);
    }

    [Fact]
    public async Task A_proof_the_wallet_refuses_makes_the_source_faulty_and_changes_nothing()
    {
        var proof = Proofs.Value[1];
        var rig = await RigAsync([FromExplorer(proof)]);
        var branch = proof.Branch.ToList();
        branch[0] = (branch[0][0] == 'a' ? 'b' : 'a') + branch[0][1..];
        rig.Source.Known.Clear();
        rig.Source.Known.Add(proof with { Branch = branch });

        await Assert.ThrowsAsync<GatewayFaultException>(() => rig.Upgrader.UpgradeAsync(WalletId));

        Assert.Equal(SyncOrigin.ExplorerSourced, (await rig.States.GetAsync(WalletId))!.Transactions.Single().Origin);
    }

    [Fact]
    public async Task A_proof_for_a_block_the_wallet_has_not_verified_yet_is_left_for_later_and_is_not_held_against_the_source()
    {
        var proof = Proofs.Value.First(p => p.Height > Cp + 60);
        var rig = await RigAsync([FromExplorer(proof)], headersUpTo: Cp + 60);

        var result = await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal(0, result.Upgraded);
        Assert.Equal(SyncOrigin.ExplorerSourced, (await rig.States.GetAsync(WalletId))!.Transactions.Single().Origin);
    }

    [Fact]
    public async Task A_proven_block_that_differs_from_what_the_explorer_reported_marks_the_transaction_disputed_and_takes_the_proven_data()
    {
        var proof = Proofs.Value[1];
        var rig = await RigAsync([FromExplorer(proof, blockHash: new string('5', 64))]);

        var result = await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal((0, 1), (result.Upgraded, result.Disputed));
        var stored = (await rig.States.GetAsync(WalletId))!.Transactions.Single();
        Assert.True(stored.Disputed);
        Assert.Equal(SyncOrigin.ExplorerSourced, stored.Origin); // stays unchecked
        Assert.Equal(proof.BlockHash, stored.BlockHash);
        Assert.Equal(proof.Raw, stored.Raw);
        // A disputed transaction is never spendable.
        var facts = new TransactionFacts(50, false, ProofChecked: false, ExplorerSourced: true, Disputed: stored.Disputed);
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(facts, false), false));
    }

    [Fact]
    public async Task A_proof_for_something_that_was_not_asked_for_is_ignored()
    {
        var asked = Proofs.Value[1];
        var other = Proofs.Value[3];
        var rig = await RigAsync([FromExplorer(asked)]);
        rig.Source.Tamper = answer => new GatewayProofsAnswer([.. answer.Proofs, other], answer.Unavailable);

        await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.DoesNotContain((await rig.States.GetAsync(WalletId))!.Transactions, t => t.Txid == other.Txid);
    }

    [Fact]
    public async Task Gateway_proven_and_unconfirmed_entries_are_never_asked_about()
    {
        var proven = new SyncTransaction(Proofs.Value[1].Txid, Proofs.Value[1].Height, Proofs.Value[1].BlockHash, "0100", SyncOrigin.GatewayProven);
        var pending = new SyncTransaction(new string('4', 64), 0, string.Empty, null, SyncOrigin.ExplorerSourced);
        var rig = await RigAsync([proven, pending]);

        await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Empty(rig.Source.Asked);
    }

    [Fact]
    public async Task Nothing_is_asked_when_the_caller_does_not_allow_it_because_proof_requests_reveal_transaction_ids()
    {
        var rig = await RigAsync([FromExplorer(Proofs.Value[1])]);
        rig.Allowed[0] = false;

        var result = await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal(UpgradeSkipReason.NotAllowed, result.Skipped);
        Assert.Empty(rig.Source.Asked);
    }

    [Fact]
    public async Task It_runs_at_most_once_per_interval_per_wallet()
    {
        var rig = await RigAsync([FromExplorer(Proofs.Value[1])]);
        await rig.Upgrader.UpgradeAsync(WalletId);

        Assert.Equal(UpgradeSkipReason.TooSoon, (await rig.Upgrader.UpgradeAsync(WalletId)).Skipped);
        rig.Time.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(UpgradeSkipReason.None, (await rig.Upgrader.UpgradeAsync(WalletId)).Skipped);
    }

    [Fact]
    public async Task A_wallet_without_a_record_is_skipped()
    {
        var rig = await RigAsync([]);

        Assert.Equal(UpgradeSkipReason.NoState, (await rig.Upgrader.UpgradeAsync("someone-else")).Skipped);
    }

    [Fact]
    public async Task A_refresh_that_changed_the_record_meanwhile_is_not_overwritten()
    {
        var proof = Proofs.Value[1];
        var rig = await RigAsync([FromExplorer(proof)]);
        rig.Source.Tamper = answer =>
        {
            // While the proof request is out, a refresh adds a transaction and moves the height.
            var current = rig.States.GetAsync(WalletId).GetAwaiter().GetResult()!;
            rig.States.SaveAsync(current with
            {
                LastSyncedHeight = Cp + 245,
                LastSyncedHash = new string('c', 64),
                Transactions = [.. current.Transactions, new SyncTransaction(new string('3', 64), Cp + 244, new string('b', 64), "0100", SyncOrigin.GatewayProven)],
            }).GetAwaiter().GetResult();
            return answer;
        };

        await rig.Upgrader.UpgradeAsync(WalletId);

        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.Equal(Cp + 245, state.LastSyncedHeight);
        Assert.Equal(2, state.Transactions.Count);
        Assert.Equal(SyncOrigin.GatewayProven, state.Transactions.Single(t => t.Txid == proof.Txid).Origin);
    }
}
