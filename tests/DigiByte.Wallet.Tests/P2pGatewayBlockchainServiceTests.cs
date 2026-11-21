using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The gateway as a blockchain source, with real mainnet headers, transactions and proofs (T051, T075): balance, outputs, history and
/// height come from the wallet record, failures throw instead of returning empty answers, and a question it cannot answer correctly is
/// declined so the next source is used.
/// </summary>
public partial class P2pGatewayBlockchainServiceTests
{
    private const int Cp = 24563100;
    private const string WalletId = "w1";
    private static readonly Network Net = DigiByteNetwork.Mainnet;

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

    /// <summary>A payment in a spendable-looking block (not a coinbase, which the first listed ones are).</summary>
    private static Payment Pay(int minHeightOffset = 14) => Payments.Value.First(p => p.Proof.Height >= Cp + minHeightOffset && !p.Tx.IsCoinBase);

    private sealed class Gateway : IGatewayScanner, IGatewayHeaderFeed, IGatewayBroadcaster
    {
        public List<GatewayTransactionProof> Transactions { get; } = [];
        public int ScanCalls { get; private set; }
        public Exception? ScanFailure { get; set; }
        public GatewayBroadcast Broadcast { get; set; } = new("ab", 6, [], 2, "accepted");
        public int TipHeight { get; set; } = Cp + 200;

        public Task<GatewayScan> ScanAsync(IReadOnlyCollection<string> addresses, int fromHeight, int toHeight, IReadOnlyCollection<string>? knownOutpoints = null, CancellationToken cancellationToken = default)
        {
            ScanCalls++;
            if (ScanFailure is not null)
                throw ScanFailure;
            var scripts = addresses.Select(a => BitcoinAddress.Create(a, Net).ScriptPubKey).ToHashSet();
            var hits = Transactions.Where(t => t.Height >= fromHeight && t.Height <= toHeight)
                .Where(t => Transaction.Parse(t.Raw, Net).Outputs.Any(o => scripts.Contains(o.ScriptPubKey))).ToList();
            return Task.FromResult(new GatewayScan(TipHeight, fromHeight, Math.Min(toHeight, TipHeight), true, hits));
        }

        public Task<GatewayHeaders> GetHeadersAsync(int from, int count, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayHeaders([], TipHeight, string.Empty));

        public Task<GatewayBroadcast> BroadcastAsync(byte[] rawTransaction, CancellationToken cancellationToken = default) => Task.FromResult(Broadcast);
    }

    private sealed class Scope(GatewayWalletScope? value) : IGatewayWalletScopeProvider
    {
        public GatewayWalletScope? Value { get; set; } = value;
        public Task<GatewayWalletScope?> GetScopeAsync() => Task.FromResult(Value);
    }

    private sealed record Rig(P2pGatewayBlockchainService Service, Gateway Gateway, WalletSyncStateStore States, Scope Scope, GatewayHeaderStore Headers, FakeTime Time);

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<Rig> RigAsync(IReadOnlyList<string> addresses, int headersUpTo = Cp + 200, int? lastSyncedOffset = 20, Func<GatewayHeaderStore, IExplorerBlockHashSource>? explorer = null, Func<GatewayProofChecker, WalletSyncStateStore, GatewayProofUpgrader>? upgrader = null)
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        await headers.AppendRangeAsync(Enumerable.Range(Cp + 1, headersUpTo - Cp).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        var states = new WalletSyncStateStore(storage);
        if (lastSyncedOffset is { } offset)
            await states.SaveAsync(new WalletSyncState(WalletId, Cp + offset, headers.HeaderAt(Cp + offset)!.Hash.ToString(), [], [], DateTimeOffset.UtcNow));
        var gateway = new Gateway { TipHeight = headersUpTo };
        var time = new FakeTime();
        var scope = new Scope(new GatewayWalletScope(WalletId, addresses, new HashSet<string>()));
        var incremental = new GatewayIncrementalSync(gateway, headers, new GatewayProofChecker(headers), states, Net, time);
        var service = new P2pGatewayBlockchainService(new GatewayHeaderSync(headers, gateway), incremental, states, headers, scope, gateway, Net, time, TimeSpan.FromSeconds(10),
            explorer is null ? null : new GatewayTipCrossCheck(headers, explorer(headers)),
            upgrader?.Invoke(new GatewayProofChecker(headers), states));
        return new Rig(service, gateway, states, scope, headers, time);
    }

    // ---- balance, outputs, height ----

    [Fact]
    public async Task Balance_and_outputs_come_from_the_checked_transactions_with_their_confirmations()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address]);
        rig.Gateway.Transactions.Add(pay.Proof);

        var utxos = await rig.Service.GetUtxosAsync(pay.Address);
        var balance = await rig.Service.GetBalanceAsync(pay.Address);

        var utxo = utxos.Single(u => u.TxId == pay.Proof.Txid && u.OutputIndex == (uint)pay.Vout);
        Assert.Equal(pay.Tx.Outputs[pay.Vout].Value.Satoshi, utxo.AmountSatoshis);
        Assert.Equal(Cp + 200 - pay.Proof.Height + 1, utxo.Confirmations);
        Assert.Equal(utxos.Sum(u => u.AmountSatoshis), balance);
    }

    [Fact]
    public async Task A_payment_fewer_than_six_blocks_deep_is_not_spendable_yet()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address], headersUpTo: pay.Proof.Height - Cp > 0 ? pay.Proof.Height + 2 : Cp + 40, lastSyncedOffset: 20);
        rig.Gateway.Transactions.Add(pay.Proof);

        Assert.Empty(await rig.Service.GetUtxosAsync(pay.Address));
        Assert.Equal(0, await rig.Service.GetBalanceAsync(pay.Address));
    }

    [Fact]
    public async Task A_wallet_with_no_payments_has_a_zero_balance_without_errors()
    {
        var rig = await RigAsync([new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString()]);

        Assert.Equal(0, await rig.Service.GetBalanceAsync(rig.Scope.Value!.Addresses[0]));
        Assert.Empty(await rig.Service.GetUtxosAsync(rig.Scope.Value.Addresses[0]));
    }

    [Fact]
    public async Task The_block_height_is_the_wallet_own_verified_tip()
    {
        var rig = await RigAsync([Pay().Address]);

        Assert.Equal(Cp + 200, await rig.Service.GetBlockHeightAsync());
    }

    // ---- failures ----

    [Fact]
    public async Task A_failed_read_throws_and_never_returns_an_empty_list()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address]);
        rig.Gateway.ScanFailure = new GatewayRequestException("unreachable", 0, "down");

        await Assert.ThrowsAsync<GatewayRequestException>(() => rig.Service.GetUtxosAsync(pay.Address));
        await Assert.ThrowsAsync<GatewayRequestException>(() => rig.Service.GetVerifiedUtxosAsync(pay.Address));
        await Assert.ThrowsAsync<GatewayRequestException>(() => rig.Service.GetBalanceAsync(pay.Address));
    }

    [Fact]
    public async Task A_proof_that_fails_the_wallet_checks_throws_a_fault_and_nothing_counts()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address]);
        var branch = pay.Proof.Branch.ToList();
        branch[0] = (branch[0][0] == 'a' ? 'b' : 'a') + branch[0][1..];
        rig.Gateway.Transactions.Add(pay.Proof with { Branch = branch });

        await Assert.ThrowsAsync<GatewayFaultException>(() => rig.Service.GetBalanceAsync(pay.Address));
    }

    // ---- declining ----

    [Fact]
    public async Task A_wallet_without_a_record_is_declined_not_failed()
    {
        var rig = await RigAsync([Pay().Address], lastSyncedOffset: null);

        await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Service.GetBalanceAsync(Pay().Address));
        Assert.Equal(0, rig.Gateway.ScanCalls);
    }

    [Fact]
    public async Task No_active_wallet_is_declined()
    {
        var rig = await RigAsync([Pay().Address]);
        rig.Scope.Value = null;

        await Assert.ThrowsAnyAsync<GatewayDeclinedException>(() => rig.Service.GetBalanceAsync(Pay().Address));
    }

    [Fact]
    public async Task An_address_the_record_does_not_cover_is_declined()
    {
        var rig = await RigAsync([Pay().Address]);

        await Assert.ThrowsAnyAsync<GatewayDeclinedException>(() => rig.Service.GetBalanceAsync(new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString()));
    }

    [Fact]
    public async Task The_fee_and_the_price_are_left_to_the_existing_sources()
    {
        var rig = await RigAsync([Pay().Address]);

        await Assert.ThrowsAsync<GatewayDeclinedException>(() => rig.Service.GetFeeRateAsync());
        await Assert.ThrowsAsync<GatewayDeclinedException>(() => rig.Service.GetDgbPriceAsync());
    }

    // ---- refresh frequency ----

    [Fact]
    public async Task Reads_within_the_interval_share_one_refresh_and_a_later_read_refreshes_again()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address]);

        await rig.Service.GetBalanceAsync(pay.Address);
        await rig.Service.GetUtxosAsync(pay.Address);
        Assert.Equal(1, rig.Gateway.ScanCalls);

        rig.Time.Now += TimeSpan.FromSeconds(11);
        await rig.Service.GetBalanceAsync(pay.Address);
        Assert.Equal(2, rig.Gateway.ScanCalls);
    }

    // ---- history ----

    [Fact]
    public async Task History_of_a_received_payment_whose_inputs_cannot_be_named_is_declined_not_guessed()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address]);
        rig.Gateway.Transactions.Add(pay.Proof);

        // Direction in the history list depends on who funded a transaction, so a row with an unknown input must not be invented.
        await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Service.GetAddressTransactionsAsync(pay.Address));
    }

    [Fact]
    public async Task History_names_inputs_the_record_remembers_and_works_out_the_fee()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address]);
        rig.Gateway.Transactions.Add(pay.Proof);
        await rig.Service.GetBalanceAsync(pay.Address); // builds the record
        var state = (await rig.States.GetAsync(WalletId))!;
        var remembered = pay.Tx.Inputs.Select((input, i) => new SyncOutpoint(input.PrevOut.Hash.ToString(), (int)input.PrevOut.N, 1_000_000_000 + i, pay.Tx.Outputs[pay.Vout].ScriptPubKey.ToHex())).ToList();
        await rig.States.SaveAsync(state with { SpentOutpoints = remembered });
        rig.Time.Now += TimeSpan.FromSeconds(30);

        var history = await rig.Service.GetAddressTransactionsAsync(pay.Address);

        var row = history.Single(t => t.TxId == pay.Proof.Txid);
        Assert.Equal(pay.Tx.Inputs.Count, row.Inputs.Count);
        Assert.All(row.Inputs, i => Assert.Equal(pay.Address, i.Address));
        var totalIn = remembered.Sum(o => o.Amount);
        var totalOut = pay.Tx.Outputs.Sum(o => o.Value.Satoshi);
        Assert.Equal(Math.Max(0, totalIn - totalOut), row.FeeSatoshis);
        Assert.Equal(Cp + 200 - pay.Proof.Height + 1, row.Confirmations);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(BlockHeader80.Parse(HeaderBytes.Value[pay.Proof.Height]).Time).UtcDateTime, row.Timestamp);
    }

    [Fact]
    public async Task A_transaction_the_record_does_not_hold_is_not_found_here_so_other_sources_are_asked()
    {
        var rig = await RigAsync([Pay().Address]);

        Assert.Null(await rig.Service.GetTransactionAsync(new string('1', 64)));
    }

    // ---- the independent explorer check ----

    private sealed class ScriptedExplorer(GatewayHeaderStore headers) : IExplorerBlockHashSource
    {
        public bool Disagrees { get; set; }
        public bool Silent { get; set; }
        public int Calls { get; private set; }

        public Task<string?> GetBlockHashAsync(int height, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Silent)
                return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(Disagrees ? new string('1', 64) : headers.HeaderAt(height)?.Hash.ToString());
        }
    }

    [Fact]
    public async Task An_explorer_that_does_not_see_the_gateway_chain_makes_the_source_faulty()
    {
        ScriptedExplorer? explorer = null;
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address], explorer: h => explorer = new ScriptedExplorer(h) { Disagrees = true });

        await Assert.ThrowsAsync<GatewayFaultException>(() => rig.Service.GetBalanceAsync(pay.Address));
        await Assert.ThrowsAsync<GatewayFaultException>(() => rig.Service.GetBlockHeightAsync());
    }

    [Fact]
    public async Task An_explorer_that_agrees_changes_nothing_and_is_asked_only_once_per_interval()
    {
        ScriptedExplorer? explorer = null;
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address], explorer: h => explorer = new ScriptedExplorer(h));

        await rig.Service.GetBalanceAsync(pay.Address);
        var first = explorer!.Calls;
        rig.Time.Now += TimeSpan.FromSeconds(11);
        await rig.Service.GetBalanceAsync(pay.Address); // refreshes again, but the explorer check is not due yet
        Assert.Equal(first, explorer.Calls);

        rig.Time.Now += TimeSpan.FromMinutes(3);
        await rig.Service.GetBalanceAsync(pay.Address);
        Assert.True(explorer.Calls > first);
    }

    [Fact]
    public async Task A_silent_explorer_neither_promotes_nor_demotes_the_gateway()
    {
        var pay = Pay(30);
        var rig = await RigAsync([pay.Address], explorer: h => new ScriptedExplorer(h) { Silent = true });
        rig.Gateway.Transactions.Add(pay.Proof);

        Assert.NotEmpty(await rig.Service.GetUtxosAsync(pay.Address));
    }

    // ---- proofs for explorer-sourced transactions ----

    private sealed class ProofSource(Dictionary<string, GatewayTransactionProof> known) : IGatewayProofSource
    {
        public int Calls { get; private set; }

        public Task<GatewayProofsAnswer> GetProofsAsync(IReadOnlyCollection<(string Txid, int Height)> items, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new GatewayProofsAnswer(items.Where(i => known.ContainsKey(i.Txid)).Select(i => known[i.Txid]).ToList(), []));
        }
    }

    [Fact]
    public async Task When_the_gateway_answers_a_read_the_explorer_sourced_transactions_are_proven_in_the_background()
    {
        var pay = Pay(30);
        var source = new ProofSource(new() { [pay.Proof.Txid] = pay.Proof });
        var rig = await RigAsync([pay.Address], upgrader: (checker, states) => new GatewayProofUpgrader(source, checker, states, () => true));
        var explorerSourced = new SyncTransaction(pay.Proof.Txid, pay.Proof.Height, pay.Proof.BlockHash, null, SyncOrigin.ExplorerSourced);
        var state = (await rig.States.GetAsync(WalletId))!;
        await rig.States.SaveAsync(state with { Transactions = [explorerSourced] });

        await rig.Service.GetBalanceAsync(pay.Address);

        for (var i = 0; i < 100 && source.Calls == 0; i++)
            await Task.Delay(20);
        Assert.True(source.Calls > 0);
    }

    // ---- broadcast ----

    [Fact]
    public async Task An_accepted_broadcast_returns_the_transaction_id()
    {
        var rig = await RigAsync([Pay().Address]);
        rig.Gateway.Broadcast = new GatewayBroadcast("ab12", 6, [], 2, "accepted");

        Assert.Equal("ab12", await rig.Service.BroadcastTransactionAsync([1, 2, 3]));
    }

    [Fact]
    public async Task A_no_response_broadcast_still_returns_the_id_because_the_transaction_was_sent()
    {
        var rig = await RigAsync([Pay().Address]);
        rig.Gateway.Broadcast = new GatewayBroadcast("ab12", 0, [], 0, "no_response");

        Assert.Equal("ab12", await rig.Service.BroadcastTransactionAsync([1, 2, 3]));
    }

    [Fact]
    public async Task A_rejected_broadcast_throws_with_the_reason()
    {
        var rig = await RigAsync([Pay().Address]);
        rig.Gateway.Broadcast = new GatewayBroadcast("ab12", 5, ["66: insufficient fee"], 0, "rejected");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.BroadcastTransactionAsync([1, 2, 3]));

        Assert.Contains("insufficient fee", error.Message);
    }
}
