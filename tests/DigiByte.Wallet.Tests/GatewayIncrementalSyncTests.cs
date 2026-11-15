using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A routine refresh through the gateway, with real mainnet headers, transactions and merkle proofs (T057, T058, spec FR-009a):
/// from the last synced height minus 6, known outpoints sent along, lists split at 100, nothing saved when a refresh fails part way.
/// </summary>
public class GatewayIncrementalSyncTests
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

    private static readonly Lazy<Dictionary<int, byte[]>> Headers = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    /// <summary>A real transaction with its proof, and the wallet address its first standard output pays.</summary>
    private sealed record Payment(GatewayTransactionProof Proof, Transaction Tx, string Address, int Vout);

    private static readonly Lazy<List<Payment>> Payments = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "merkle-mainnet.json")));
        var list = new List<Payment>();
        foreach (var e in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var raw = e.GetProperty("rawTx").GetString()!;
            var tx = Transaction.Parse(raw, Net);
            for (var n = 0; n < tx.Outputs.Count; n++)
            {
                if (tx.Outputs[n].ScriptPubKey.GetDestinationAddress(Net) is not { } address)
                    continue;
                list.Add(new Payment(new GatewayTransactionProof(e.GetProperty("txid").GetString()!, e.GetProperty("height").GetInt32(), e.GetProperty("blockId").GetString()!,
                    raw, e.GetProperty("txIndex").GetInt32(), e.GetProperty("branch").EnumerateArray().Select(b => b.GetString()!).ToList()), tx, address.ToString(), n));
                break;
            }
        }
        return list;
    });

    private static Payment PaymentAfter(int heightOffset) => Payments.Value.First(p => p.Proof.Height >= Cp + heightOffset && !p.Tx.IsCoinBase);

    private static Payment AnyPayment(int minOffset = 14) => Payments.Value.First(p => p.Proof.Height >= Cp + minOffset);

    /// <summary>The gateway: answers a scan with the scripted transactions that pay a requested address or spend a requested outpoint.</summary>
    private sealed class ScriptedGateway : IGatewayScanner
    {
        public List<GatewayTransactionProof> Transactions { get; } = [];
        public List<(int Addresses, int From, int To, int Outpoints)> Calls { get; } = [];
        public int TipHeight { get; set; } = Cp + 200;
        public int? FailOnCall { get; set; }
        public int? StopEarlyAt { get; set; }
        public Exception? Fail { get; set; }

        public Task<GatewayScan> ScanAsync(IReadOnlyCollection<string> addresses, int fromHeight, int toHeight, IReadOnlyCollection<string>? knownOutpoints = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((addresses.Count, fromHeight, toHeight, knownOutpoints?.Count ?? 0));
            if (Fail is not null)
                throw Fail;
            if (FailOnCall == Calls.Count)
                throw new GatewayRequestException("unreachable", 0, "down");

            var scripts = addresses.Select(a => BitcoinAddress.Create(a, Net).ScriptPubKey).ToHashSet();
            var outpoints = (knownOutpoints ?? []).ToHashSet();
            var upper = Math.Min(toHeight, TipHeight);
            var complete = true;
            if (StopEarlyAt is { } stop && stop < upper && stop >= fromHeight)
            {
                upper = stop;
                complete = false;
            }
            var hits = Transactions.Where(t => t.Height >= fromHeight && t.Height <= upper).Where(t =>
            {
                var tx = Transaction.Parse(t.Raw, Net);
                return tx.Outputs.Any(o => scripts.Contains(o.ScriptPubKey)) || tx.Inputs.Any(i => outpoints.Contains($"{i.PrevOut.Hash}:{i.PrevOut.N}"));
            }).ToList();
            return Task.FromResult(new GatewayScan(TipHeight, fromHeight, upper, complete, hits));
        }
    }

    private sealed record Rig(GatewayIncrementalSync Sync, ScriptedGateway Gateway, WalletSyncStateStore States, GatewayHeaderStore Headers);

    private static async Task<Rig> RigAsync(int headersUpTo = Cp + 200)
    {
        var storage = new MemoryStorage();
        var headerStore = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headerStore.InitializeAsync();
        var result = await headerStore.AppendRangeAsync(Enumerable.Range(Cp + 1, headersUpTo - Cp).Select(h => BlockHeader80.Parse(Headers.Value[h])));
        Assert.Null(result.Rejected);
        var states = new WalletSyncStateStore(storage);
        var gateway = new ScriptedGateway();
        return new Rig(new GatewayIncrementalSync(gateway, headerStore, new GatewayProofChecker(headerStore), states, Net), gateway, states, headerStore);
    }

    private static async Task SaveStateAsync(Rig rig, int lastSyncedOffset, IReadOnlyList<SyncTransaction>? transactions = null, IReadOnlyList<SyncOutpoint>? outpoints = null)
    {
        var height = Cp + lastSyncedOffset;
        await rig.States.SaveAsync(new WalletSyncState(WalletId, height, rig.Headers.HeaderAt(height)!.Hash.ToString(), transactions ?? [], outpoints ?? [], DateTimeOffset.UtcNow));
    }

    // ---- the happy path ----

    [Fact]
    public async Task A_refresh_asks_from_the_last_synced_height_minus_six_to_the_tip()
    {
        var rig = await RigAsync();
        await SaveStateAsync(rig, 50);

        await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        var call = rig.Gateway.Calls.Single();
        Assert.Equal(Cp + 50 - 6, call.From);
        Assert.Equal(Cp + 200, call.To);
    }

    [Fact]
    public async Task A_payment_found_by_the_gateway_is_stored_with_its_raw_bytes_and_becomes_an_unspent_output()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        rig.Gateway.Transactions.Add(payment.Proof);
        await SaveStateAsync(rig, 20);

        var result = await rig.Sync.RefreshAsync(WalletId, [payment.Address]);

        Assert.Equal(1, result.NewTransactions);
        var stored = Assert.Single(result.State.Transactions);
        Assert.Equal((payment.Proof.Txid, SyncOrigin.GatewayProven, payment.Proof.Raw), (stored.Txid, stored.Origin, stored.Raw));
        var output = Assert.Single(result.State.UnspentOutpoints, o => o.Txid == payment.Proof.Txid && o.Vout == payment.Vout);
        Assert.Equal(payment.Tx.Outputs[payment.Vout].Value.Satoshi, output.Amount);
    }

    [Fact]
    public async Task The_new_height_and_hash_are_recorded_only_after_the_refresh_succeeded()
    {
        var rig = await RigAsync();
        await SaveStateAsync(rig, 20);

        var result = await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        Assert.Equal(Cp + 200, result.State.LastSyncedHeight);
        Assert.Equal(rig.Headers.HeaderAt(Cp + 200)!.Hash.ToString(), result.State.LastSyncedHash);
        Assert.Equal(Cp + 200, (await rig.States.GetAsync(WalletId))!.LastSyncedHeight);
    }

    [Fact]
    public async Task Funds_received_while_the_wallet_was_offline_are_found_by_the_next_refresh()
    {
        var rig = await RigAsync();
        var received = AnyPayment(60);
        rig.Gateway.Transactions.Add(received.Proof);
        await SaveStateAsync(rig, 20); // the wallet was last open at block 20

        var result = await rig.Sync.RefreshAsync(WalletId, [received.Address]);

        Assert.Contains(result.State.Transactions, t => t.Txid == received.Proof.Txid);
        Assert.Contains(result.State.UnspentOutpoints, o => o.Txid == received.Proof.Txid);
    }

    // ---- known outpoints and spends ----

    [Fact]
    public async Task Known_unspent_outpoints_are_sent_and_a_spend_of_one_is_found_and_removes_it()
    {
        var rig = await RigAsync();
        var spender = Payments.Value.First(p => p.Proof.Height >= Cp + 40 && !p.Tx.IsCoinBase);
        var spentPoint = spender.Tx.Inputs[0].PrevOut;
        rig.Gateway.Transactions.Add(spender.Proof);
        var known = new SyncOutpoint(spentPoint.Hash.ToString(), (int)spentPoint.N, 1000, "0014" + new string('0', 40));
        await SaveStateAsync(rig, 20, outpoints: [known]);

        var result = await rig.Sync.RefreshAsync(WalletId, [new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString()]);

        Assert.Equal(1, rig.Gateway.Calls.Single().Outpoints);
        Assert.DoesNotContain(result.State.UnspentOutpoints, o => o.Key == known.Key);
        Assert.Contains(result.State.Transactions, t => t.Txid == spender.Proof.Txid);
    }

    // ---- splitting ----

    [Fact]
    public async Task More_than_one_hundred_addresses_are_split_into_requests_of_at_most_one_hundred_and_combined()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        rig.Gateway.Transactions.Add(payment.Proof);
        await SaveStateAsync(rig, 20);
        var addresses = Enumerable.Range(0, 250).Select(_ => new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString()).ToList();
        addresses[230] = payment.Address; // in the last chunk

        var result = await rig.Sync.RefreshAsync(WalletId, addresses);

        Assert.Equal([100, 100, 50], rig.Gateway.Calls.Select(c => c.Addresses).ToArray());
        Assert.Equal(3, result.Requests);
        Assert.Contains(result.State.Transactions, t => t.Txid == payment.Proof.Txid);
    }

    [Fact]
    public async Task More_than_one_hundred_outpoints_are_split_too_and_no_request_exceeds_the_limit()
    {
        var rig = await RigAsync();
        await SaveStateAsync(rig, 20, outpoints: Enumerable.Range(0, 230).Select(i => new SyncOutpoint(new string('a', 64), i, 1, "00")).ToList());

        await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        Assert.All(rig.Gateway.Calls, c => Assert.True(c.Addresses <= 100 && c.Outpoints <= 100));
        Assert.Equal(230, rig.Gateway.Calls.Sum(c => c.Outpoints));
    }

    [Fact]
    public async Task A_scan_that_stops_early_is_continued_where_it_stopped()
    {
        var rig = await RigAsync();
        var late = AnyPayment(100);
        rig.Gateway.Transactions.Add(late.Proof);
        rig.Gateway.StopEarlyAt = Cp + 60;
        await SaveStateAsync(rig, 20);

        // The gateway stops at block 60 on the first answer, then (the stop point is behind the cursor) answers the rest.
        var result = await rig.Sync.RefreshAsync(WalletId, [late.Address]);

        Assert.True(rig.Gateway.Calls.Count >= 2);
        Assert.Equal(Cp + 60 + 1, rig.Gateway.Calls[1].From);
        Assert.Contains(result.State.Transactions, t => t.Txid == late.Proof.Txid);
    }

    // ---- failures leave the state alone ----

    [Fact]
    public async Task A_refresh_that_fails_part_way_leaves_the_height_hash_and_records_unchanged()
    {
        var rig = await RigAsync();
        rig.Gateway.Transactions.Add(AnyPayment(30).Proof);
        await SaveStateAsync(rig, 20);
        var before = await rig.States.GetAsync(WalletId);
        rig.Gateway.FailOnCall = 2;
        var addresses = Enumerable.Range(0, 150).Select(_ => new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString()).ToList();
        addresses[0] = AnyPayment(30).Address;

        await Assert.ThrowsAsync<GatewayRequestException>(() => rig.Sync.RefreshAsync(WalletId, addresses));

        var after = await rig.States.GetAsync(WalletId);
        Assert.Equal(before!.LastSyncedHeight, after!.LastSyncedHeight);
        Assert.Equal(before.LastSyncedHash, after.LastSyncedHash);
        Assert.Empty(after.Transactions);
    }

    [Fact]
    public async Task A_transaction_with_a_bad_proof_is_refused_the_source_is_at_fault_and_nothing_is_saved()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        var branch = payment.Proof.Branch.ToList();
        branch[0] = (branch[0][0] == 'a' ? 'b' : 'a') + branch[0][1..];
        rig.Gateway.Transactions.Add(payment.Proof with { Branch = branch });
        await SaveStateAsync(rig, 20);

        await Assert.ThrowsAsync<GatewayFaultException>(() => rig.Sync.RefreshAsync(WalletId, [payment.Address]));

        Assert.Equal(Cp + 20, (await rig.States.GetAsync(WalletId))!.LastSyncedHeight);
        Assert.Empty((await rig.States.GetAsync(WalletId))!.Transactions);
    }

    [Fact]
    public async Task A_state_ahead_of_the_wallet_header_chain_means_headers_must_catch_up_first()
    {
        var rig = await RigAsync(headersUpTo: Cp + 100);
        await rig.States.SaveAsync(new WalletSyncState(WalletId, Cp + 150, new string('a', 64), [], [], DateTimeOffset.UtcNow));

        var error = await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]));

        Assert.Equal(SyncUnavailableReason.HeadersBehind, error.Reason);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task A_range_the_gateway_no_longer_keeps_means_the_state_is_too_old_for_it()
    {
        var rig = await RigAsync();
        rig.Gateway.Fail = new GatewayRequestException("range_unavailable", 404, "old");
        await SaveStateAsync(rig, 20);

        var error = await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]));

        Assert.Equal(SyncUnavailableReason.StateTooOld, error.Reason);
    }

    [Fact]
    public async Task A_wallet_without_a_state_is_told_to_bootstrap_first_and_the_gateway_is_not_asked()
    {
        var rig = await RigAsync();

        var error = await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]));

        Assert.Equal(SyncUnavailableReason.NoState, error.Reason);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task A_gateway_that_is_behind_the_wallet_headers_moves_the_record_only_as_far_as_it_reached()
    {
        var rig = await RigAsync();
        rig.Gateway.TipHeight = Cp + 120;
        await SaveStateAsync(rig, 20);

        var result = await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        Assert.Equal(Cp + 120, result.State.LastSyncedHeight);
        Assert.Equal(rig.Headers.HeaderAt(Cp + 120)!.Hash.ToString(), result.State.LastSyncedHash);
    }

    // ---- reorganizations ----

    [Fact]
    public async Task When_the_recorded_hash_no_longer_matches_the_wallet_rewinds_and_drops_what_the_new_chain_does_not_have()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        var staleTx = new SyncTransaction(payment.Proof.Txid, payment.Proof.Height, new string('9', 64), payment.Proof.Raw, SyncOrigin.GatewayProven);
        await rig.States.SaveAsync(new WalletSyncState(WalletId, Cp + 50, new string('9', 64), [staleTx], [new SyncOutpoint(payment.Proof.Txid, payment.Vout, 5, "00")], DateTimeOffset.UtcNow));

        var result = await rig.Sync.RefreshAsync(WalletId, [payment.Address]);

        Assert.True(rig.Gateway.Calls[0].From <= payment.Proof.Height - 1);
        Assert.DoesNotContain(result.State.Transactions, t => t.Txid == payment.Proof.Txid);
        Assert.DoesNotContain(result.State.UnspentOutpoints, o => o.Txid == payment.Proof.Txid);
        Assert.Equal(1, result.RemovedTransactions);
    }

    [Fact]
    public async Task A_transaction_that_a_reorganization_moved_to_another_block_is_replaced_by_the_new_version()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        var recorded = new SyncTransaction(payment.Proof.Txid, payment.Proof.Height - 1, new string('9', 64), payment.Proof.Raw, SyncOrigin.GatewayProven);
        rig.Gateway.Transactions.Add(payment.Proof);
        await SaveStateAsync(rig, 50, transactions: [recorded]);

        var result = await rig.Sync.RefreshAsync(WalletId, [payment.Address]);

        var stored = result.State.Transactions.Single(t => t.Txid == payment.Proof.Txid);
        Assert.Equal(payment.Proof.Height, stored.Height);
        Assert.Equal(payment.Proof.BlockHash, stored.BlockHash);
    }

    // ---- explorer-sourced records ----

    [Fact]
    public async Task An_explorer_sourced_transaction_the_gateway_proves_becomes_gateway_proven()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        rig.Gateway.Transactions.Add(payment.Proof);
        var fromExplorer = new SyncTransaction(payment.Proof.Txid, payment.Proof.Height, payment.Proof.BlockHash, null, SyncOrigin.ExplorerSourced);
        await SaveStateAsync(rig, 50, transactions: [fromExplorer]);

        var result = await rig.Sync.RefreshAsync(WalletId, [payment.Address]);

        var stored = result.State.Transactions.Single(t => t.Txid == payment.Proof.Txid);
        Assert.Equal(SyncOrigin.GatewayProven, stored.Origin);
        Assert.Equal(payment.Proof.Raw, stored.Raw);
        Assert.False(stored.Disputed);
    }

    [Fact]
    public async Task An_explorer_sourced_transaction_the_gateway_chain_does_not_have_is_marked_disputed_not_deleted()
    {
        var rig = await RigAsync();
        var phantom = new SyncTransaction(new string('7', 64), Cp + 48, new string('8', 64), null, SyncOrigin.ExplorerSourced);
        await SaveStateAsync(rig, 50, transactions: [phantom]);

        var result = await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        var stored = result.State.Transactions.Single(t => t.Txid == phantom.Txid);
        Assert.True(stored.Disputed);
        Assert.Equal(SyncOrigin.ExplorerSourced, stored.Origin);
        Assert.Equal(1, result.DisputedTransactions);
    }

    [Fact]
    public async Task Older_records_outside_the_scanned_range_are_left_alone()
    {
        var rig = await RigAsync();
        var old = new SyncTransaction(new string('6', 64), Cp + 5, new string('5', 64), null, SyncOrigin.ExplorerSourced);
        await SaveStateAsync(rig, 50, transactions: [old]);

        var result = await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        Assert.Equal(old, result.State.Transactions.Single());
    }

    [Fact]
    public async Task An_own_send_not_yet_in_a_block_stays_until_a_block_shows_it_and_is_then_replaced_by_the_proven_version()
    {
        var rig = await RigAsync();
        var payment = AnyPayment(30);
        var pending = new SyncTransaction(payment.Proof.Txid, 0, "", payment.Proof.Raw, SyncOrigin.GatewayProven, IsOwnSend: true);
        await SaveStateAsync(rig, 50, transactions: [pending]);

        var stays = await rig.Sync.RefreshAsync(WalletId, [payment.Address]);
        Assert.Equal(0, stays.State.Transactions.Single().Height);

        rig.Gateway.Transactions.Add(payment.Proof);
        await rig.States.SaveAsync(stays.State with { LastSyncedHeight = Cp + 50, LastSyncedHash = rig.Headers.HeaderAt(Cp + 50)!.Hash.ToString() });
        var confirmed = await rig.Sync.RefreshAsync(WalletId, [payment.Address]);

        var stored = confirmed.State.Transactions.Single();
        Assert.Equal(payment.Proof.Height, stored.Height);
        Assert.True(stored.IsOwnSend);
    }

    [Fact]
    public async Task The_wallet_id_and_record_never_go_to_the_gateway()
    {
        var rig = await RigAsync();
        await SaveStateAsync(rig, 20);

        await rig.Sync.RefreshAsync(WalletId, [AnyPayment().Address]);

        // The scanner interface takes only addresses, heights and outpoints: nothing else can reach it.
        Assert.Single(rig.Gateway.Calls);
    }
}
