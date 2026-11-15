using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Restoring a wallet through the gateway, with real headers, transactions and proofs (T091, T095): funds are found across the gap limit
/// and across chunks, a deeper scan reaches further, an interrupted restore resumes, and nothing is skipped silently.
/// </summary>
public class GatewayRestoreScanTests
{
    private const int Cp = 24563100;
    private const string WalletId = "w1";
    private static readonly Network Net = DigiByteNetwork.Mainnet;

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

    private static readonly Lazy<Dictionary<int, byte[]>> HeaderBytes = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private sealed record Payment(GatewayTransactionProof Proof, Transaction Tx, string Address);

    /// <summary>Real payments with distinct addresses, oldest first (not coinbase outputs).</summary>
    private static readonly Lazy<List<Payment>> Payments = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "merkle-mainnet.json")));
        var list = new List<Payment>();
        foreach (var e in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var raw = e.GetProperty("rawTx").GetString()!;
            var tx = Transaction.Parse(raw, Net);
            var address = tx.Outputs.Select(o => o.ScriptPubKey.GetDestinationAddress(Net)?.ToString()).FirstOrDefault(a => a is not null);
            if (address is null)
                continue;
            list.Add(new Payment(new GatewayTransactionProof(e.GetProperty("txid").GetString()!, e.GetProperty("height").GetInt32(), e.GetProperty("blockId").GetString()!,
                raw, e.GetProperty("txIndex").GetInt32(), e.GetProperty("branch").EnumerateArray().Select(b => b.GetString()!).ToList()), tx, address));
        }
        return list.GroupBy(p => p.Address).Select(g => g.First()).OrderBy(p => p.Proof.Height).ToList();
    });

    private sealed class Addresses(Dictionary<int, string>? receive = null, Dictionary<int, string>? change = null) : IRestoreAddressSource
    {
        private readonly Dictionary<int, string> _filler = [];

        public string Receive(int index) => receive is not null && receive.TryGetValue(index, out var a) ? a : Filler("r", index);

        public string Change(int index) => change is not null && change.TryGetValue(index, out var a) ? a : Filler("c", index);

        private string Filler(string kind, int index)
        {
            var key = kind + index;
            if (!_filler.TryGetValue(key.GetHashCode(), out var address))
            {
                address = new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString();
                _filler[key.GetHashCode()] = address;
            }
            return address;
        }
    }

    private sealed class Gateway : IGatewayScanner
    {
        public List<GatewayTransactionProof> Transactions { get; } = [];
        public List<(int Addresses, int From, int To)> Calls { get; } = [];
        public Func<int, int, Exception?>? FailWhen { get; set; }
        public int TipHeight { get; set; } = Cp + 200;

        public Task<GatewayScan> ScanAsync(IReadOnlyCollection<string> addresses, int fromHeight, int toHeight, IReadOnlyCollection<string>? knownOutpoints = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((addresses.Count, fromHeight, toHeight));
            if (FailWhen?.Invoke(fromHeight, toHeight) is { } failure)
                throw failure;
            var scripts = addresses.Select(a => BitcoinAddress.Create(a, Net).ScriptPubKey).ToHashSet();
            var upper = Math.Min(toHeight, TipHeight);
            var hits = Transactions.Where(t => t.Height >= fromHeight && t.Height <= upper)
                .Where(t => Transaction.Parse(t.Raw, Net).Outputs.Any(o => scripts.Contains(o.ScriptPubKey))).ToList();
            return Task.FromResult(new GatewayScan(TipHeight, fromHeight, upper, true, hits));
        }
    }

    private sealed record Rig(GatewayRestoreScanner Scanner, Gateway Gateway, ScanProgressStore Progress, WalletSyncStateStore States, GatewayHeaderStore Headers);

    private static async Task<Rig> RigAsync(int chunkBlocks = 50)
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        await headers.AppendRangeAsync(Enumerable.Range(Cp + 1, 200).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        var states = new WalletSyncStateStore(storage);
        var progress = new ScanProgressStore(storage);
        var gateway = new Gateway();
        var checker = new GatewayProofChecker(headers);
        var incremental = new GatewayIncrementalSync(gateway, headers, checker, states, Net);
        return new Rig(new GatewayRestoreScanner(gateway, headers, checker, progress, incremental, states, Net, chunkBlocks), gateway, progress, states, headers);
    }

    private static Payment Early => Payments.Value[0];
    private static Payment Middle => Payments.Value[Payments.Value.Count / 2];
    private static Payment Late => Payments.Value[^1];

    [Fact]
    public async Task The_real_payments_used_here_are_in_increasing_height_order_with_distinct_addresses()
    {
        Assert.True(Payments.Value.Count >= 4);
        Assert.True(Early.Proof.Height < Middle.Proof.Height && Middle.Proof.Height < Late.Proof.Height);
    }

    [Fact]
    public async Task Funds_on_receive_and_change_addresses_are_found_and_the_gap_window_grows_as_addresses_are_used()
    {
        var rig = await RigAsync();
        rig.Gateway.Transactions.AddRange([Early.Proof, Middle.Proof, Late.Proof]);
        // The late payment sits on receive 12, which opens addresses up to 32, where the early payment sits: found only by rescanning
        // the blocks already read. The middle payment is on change 2.
        var addresses = new Addresses(new() { [12] = Late.Address, [30] = Early.Address }, new() { [2] = Middle.Address });

        var result = await rig.Scanner.RestoreAsync(WalletId, addresses, Cp + 1, gapLimit: 20, ScanMode.Normal);

        Assert.Equal(RestoreOutcome.Completed, result.Outcome);
        Assert.Equal(30, result.ReceiveHighestUsed);
        Assert.Equal(2, result.ChangeHighestUsed);
        Assert.Equal(3, result.TransactionsFound);
        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.All(new[] { Early, Middle, Late }, p => Assert.Contains(state.Transactions, t => t.Txid == p.Proof.Txid && t.Origin == SyncOrigin.GatewayProven));
    }

    [Fact]
    public async Task An_address_beyond_the_gap_is_not_found_by_a_normal_scan_but_is_by_a_deep_one()
    {
        var far = new Dictionary<int, string> { [5] = Late.Address, [100] = Early.Address };

        var normal = await RigAsync();
        normal.Gateway.Transactions.AddRange([Early.Proof, Late.Proof]);
        var normalResult = await normal.Scanner.RestoreAsync(WalletId, new Addresses(far), Cp + 1, gapLimit: 20, ScanMode.Normal);

        var deep = await RigAsync();
        deep.Gateway.Transactions.AddRange([Early.Proof, Late.Proof]);
        var deepResult = await deep.Scanner.RestoreAsync(WalletId, new Addresses(far), Cp + 1, gapLimit: WalletService.DeepScanGapLimit, ScanMode.Deep);

        Assert.Equal(5, normalResult.ReceiveHighestUsed);
        Assert.Equal(1, normalResult.TransactionsFound);
        Assert.Equal(100, deepResult.ReceiveHighestUsed);
        Assert.Equal(2, deepResult.TransactionsFound);
    }

    [Fact]
    public async Task No_request_names_more_than_one_hundred_addresses_even_with_the_deep_gap()
    {
        var rig = await RigAsync();
        rig.Gateway.Transactions.Add(Late.Proof);

        await rig.Scanner.RestoreAsync(WalletId, new Addresses(new() { [5] = Late.Address }), Cp + 1, WalletService.DeepScanGapLimit, ScanMode.Deep);

        Assert.All(rig.Gateway.Calls, c => Assert.True(c.Addresses <= 100));
        Assert.Contains(rig.Gateway.Calls, c => c.Addresses == 100);
    }

    [Fact]
    public async Task A_range_the_gateway_does_not_keep_hands_over_to_the_explorer_with_an_explanation_and_writes_no_record()
    {
        var rig = await RigAsync();
        rig.Gateway.FailWhen = (_, _) => new GatewayRequestException("range_unavailable", 404, "old");

        var result = await rig.Scanner.RestoreAsync(WalletId, new Addresses(), Cp + 1, 20, ScanMode.Normal);

        Assert.Equal(RestoreOutcome.UseExplorer, result.Outcome);
        Assert.Contains("explorers", result.Message);
        Assert.Contains("Nothing is skipped", result.Message);
        Assert.Null(await rig.States.GetAsync(WalletId));
        Assert.Equal(ScanSource.Explorer, (await rig.Progress.GetAsync(WalletId))!.Source);
    }

    [Fact]
    public async Task An_interrupted_restore_resumes_at_the_first_unfinished_chunk_and_still_finds_everything()
    {
        var rig = await RigAsync();
        rig.Gateway.Transactions.AddRange([Early.Proof, Late.Proof]);
        var addresses = new Addresses(new() { [3] = Early.Address, [4] = Late.Address });
        var failedOnce = false;
        rig.Gateway.FailWhen = (from, _) =>
        {
            if (!failedOnce && from >= Cp + 101)
            {
                failedOnce = true;
                return new GatewayRequestException("unreachable", 0, "connection lost");
            }
            return null;
        };

        await Assert.ThrowsAsync<GatewayRequestException>(() => rig.Scanner.RestoreAsync(WalletId, addresses, Cp + 1, 20, ScanMode.Normal));
        var saved = (await rig.Progress.GetAsync(WalletId))!;
        Assert.Equal(Cp + 101, saved.NextHeight); // the two chunks before it were finished
        rig.Gateway.Calls.Clear();

        var result = await rig.Scanner.RestoreAsync(WalletId, addresses, Cp + 1, 20, ScanMode.Normal);

        Assert.Equal(RestoreOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.TransactionsFound);
        // The resumed run did not read the first two chunks of blocks again for the original addresses.
        Assert.DoesNotContain(rig.Gateway.Calls.Take(1), c => c.From < Cp + 101 && c.To < Cp + 101);
        Assert.Null(await rig.Progress.GetAsync(WalletId)); // finished scans leave no progress behind
    }

    [Fact]
    public async Task A_failure_other_than_an_old_range_stops_the_restore_instead_of_returning_less_than_was_there()
    {
        var rig = await RigAsync();
        rig.Gateway.FailWhen = (_, _) => new GatewayRequestException("unreachable", 0, "down");

        await Assert.ThrowsAsync<GatewayRequestException>(() => rig.Scanner.RestoreAsync(WalletId, new Addresses(), Cp + 1, 20, ScanMode.Normal));

        Assert.Null(await rig.States.GetAsync(WalletId));
    }

    [Fact]
    public async Task A_proof_the_wallet_refuses_makes_the_source_faulty_and_nothing_is_recorded()
    {
        var rig = await RigAsync();
        var branch = Late.Proof.Branch.ToList();
        branch[0] = (branch[0][0] == 'a' ? 'b' : 'a') + branch[0][1..];
        rig.Gateway.Transactions.Add(Late.Proof with { Branch = branch });

        await Assert.ThrowsAsync<GatewayFaultException>(() =>
            rig.Scanner.RestoreAsync(WalletId, new Addresses(new() { [1] = Late.Address }), Cp + 1, 20, ScanMode.Normal));

        Assert.Null(await rig.States.GetAsync(WalletId));
    }

    [Fact]
    public async Task A_finished_restore_leaves_a_wallet_record_ready_for_routine_refreshes()
    {
        var rig = await RigAsync();
        rig.Gateway.Transactions.Add(Late.Proof);

        await rig.Scanner.RestoreAsync(WalletId, new Addresses(new() { [1] = Late.Address }), Cp + 1, 20, ScanMode.Normal);

        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.Equal(Cp + 200, state.LastSyncedHeight);
        Assert.Equal(rig.Headers.HeaderAt(Cp + 200)!.Hash.ToString(), state.LastSyncedHash);
        Assert.Contains(state.UnspentOutpoints, o => o.Txid == Late.Proof.Txid);
    }

    [Fact]
    public async Task Progress_is_reported_up_to_complete()
    {
        var rig = await RigAsync();
        var reports = new List<double>();

        await rig.Scanner.RestoreAsync(WalletId, new Addresses(), Cp + 1, 20, ScanMode.Normal, new Progress<double>(reports.Add));
        await Task.Delay(50);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports.Max(), 3);
        Assert.All(reports, r => Assert.InRange(r, 0, 1));
    }
}
