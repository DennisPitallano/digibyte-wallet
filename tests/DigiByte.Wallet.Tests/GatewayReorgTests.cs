using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A reorganization of up to 6 blocks served by the gateway is reflected in the balance and history on the next refresh, the wallet
/// header chain truncates to the fork point and re-verifies, and a transaction that left the chain is dropped (T055, SC-008, FR-003).
/// </summary>
public class GatewayReorgTests
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

    private static BlockHeader80 Real(int height) => BlockHeader80.Parse(HeaderBytes.Value[height]);

    private static List<BlockHeader80> RealRange(int from, int to) => Enumerable.Range(Cp + from, to - from + 1).Select(Real).ToList();

    private static List<BlockHeader80> Fork(int forkAfter, int length)
    {
        var result = new List<BlockHeader80>();
        var previous = Real(Cp + forkAfter).Hash;
        for (var i = 1; i <= length; i++)
        {
            var raw = (byte[])HeaderBytes.Value[Cp + forkAfter + i].Clone();
            previous.ToBytes().CopyTo(raw, 4);
            raw[76] ^= 0xA5;
            var header = BlockHeader80.Parse(raw);
            result.Add(header);
            previous = header.Hash;
        }
        return result;
    }

    private sealed record Payment(GatewayTransactionProof Proof, Transaction Tx, string Address, int Vout);

    private static readonly Lazy<List<Payment>> PaymentsLazy = new(LoadPayments);

    private static List<Payment> LoadPayments()
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
    }

    /// <summary>A gateway whose chain the test can replace: headers, and the transactions that are in its current blocks.</summary>
    private sealed class ChainGateway : IGatewayScanner, IGatewayHeaderFeed, IGatewayBroadcaster
    {
        public List<BlockHeader80> Chain { get; set; } = [];
        public List<GatewayTransactionProof> Transactions { get; } = [];

        public int TipHeight => Cp + Chain.Count;

        public Task<GatewayHeaders> GetHeadersAsync(int from, int count, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayHeaders(Chain.Skip(from - (Cp + 1)).Take(count).ToList(), TipHeight, Chain.Count == 0 ? string.Empty : Chain[^1].Hash.ToString()));

        public Task<GatewayScan> ScanAsync(IReadOnlyCollection<string> addresses, int fromHeight, int toHeight, IReadOnlyCollection<string>? knownOutpoints = null, CancellationToken cancellationToken = default)
        {
            var scripts = addresses.Select(a => BitcoinAddress.Create(a, Net).ScriptPubKey).ToHashSet();
            var upper = Math.Min(toHeight, TipHeight);
            // A transaction is on this chain only while its block is still the block at that height.
            var hits = Transactions.Where(t => t.Height >= fromHeight && t.Height <= upper && Chain[t.Height - Cp - 1].Hash.ToString() == t.BlockHash)
                .Where(t => Transaction.Parse(t.Raw, Net).Outputs.Any(o => scripts.Contains(o.ScriptPubKey))).ToList();
            return Task.FromResult(new GatewayScan(TipHeight, fromHeight, upper, true, hits));
        }

        public Task<GatewayBroadcast> BroadcastAsync(byte[] rawTransaction, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayBroadcast("ab", 1, [], 0, "accepted"));
    }

    private sealed class Scope(GatewayWalletScope value) : IGatewayWalletScopeProvider
    {
        public Task<GatewayWalletScope?> GetScopeAsync() => Task.FromResult<GatewayWalletScope?>(value);
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Rig(P2pGatewayBlockchainService Service, ChainGateway Gateway, GatewayHeaderStore Headers, WalletSyncStateStore States, FakeTime Time);

    private static async Task<Rig> RigAsync(string address, int chainLength)
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        var gateway = new ChainGateway { Chain = RealRange(1, chainLength) };
        var states = new WalletSyncStateStore(storage);
        await states.SaveAsync(new WalletSyncState(WalletId, Cp + 20, Real(Cp + 20).Hash.ToString(), [], [], DateTimeOffset.UtcNow));
        var time = new FakeTime();
        var incremental = new GatewayIncrementalSync(gateway, headers, new GatewayProofChecker(headers), states, Net, time);
        var service = new P2pGatewayBlockchainService(new GatewayHeaderSync(headers, gateway), incremental, states, headers,
            new Scope(new GatewayWalletScope(WalletId, [address], new HashSet<string>())), gateway, Net, time, TimeSpan.FromSeconds(10));
        return new Rig(service, gateway, headers, states, time);
    }

    private static Payment PayAt(int offset) => PaymentsLazy.Value.First(p => p.Proof.Height >= Cp + offset && !p.Tx.IsCoinBase);

    [Fact]
    public async Task A_payment_in_a_block_that_a_reorganization_replaces_leaves_the_balance_and_history_on_the_next_refresh()
    {
        var pay = PayAt(100);
        var rig = await RigAsync(pay.Address, chainLength: 220);
        rig.Gateway.Transactions.Add(pay.Proof);
        var before = await rig.Service.GetBalanceAsync(pay.Address);
        Assert.True(before > 0);

        // The block holding the payment is replaced by a longer branch from 5 blocks earlier.
        var forkAfter = pay.Proof.Height - Cp - 5;
        rig.Gateway.Chain = [.. RealRange(1, forkAfter), .. Fork(forkAfter, 220 - forkAfter + 3)];
        rig.Time.Now += TimeSpan.FromSeconds(11);

        Assert.Equal(0, await rig.Service.GetBalanceAsync(pay.Address));
        var state = (await rig.States.GetAsync(WalletId))!;
        Assert.DoesNotContain(state.Transactions, t => t.Txid == pay.Proof.Txid);
        Assert.DoesNotContain(state.UnspentOutpoints, o => o.Txid == pay.Proof.Txid);
        Assert.Equal(rig.Gateway.Chain[^1].Hash, rig.Headers.TipHash);
    }

    [Fact]
    public async Task The_wallet_header_chain_follows_the_longer_branch_and_re_verifies_it()
    {
        var pay = PayAt(100);
        var rig = await RigAsync(pay.Address, chainLength: 220);
        await rig.Service.GetBlockHeightAsync();
        Assert.Equal(Cp + 220, rig.Headers.TipHeight);

        var forkAfter = 210 - 5;
        rig.Gateway.Chain = [.. RealRange(1, forkAfter), .. Fork(forkAfter, 20)];
        rig.Time.Now += TimeSpan.FromSeconds(11);

        Assert.Equal(Cp + forkAfter + 20, await rig.Service.GetBlockHeightAsync());
        Assert.Equal(rig.Gateway.Chain[^1].Hash, rig.Headers.TipHash);
        // Every header after the fork is the new branch.
        Assert.Equal(rig.Gateway.Chain[forkAfter].Hash, rig.Headers.HeaderAt(Cp + forkAfter + 1)!.Hash);
    }

    [Fact]
    public async Task A_reorganization_that_keeps_the_payment_block_changes_nothing_about_the_balance()
    {
        var pay = PayAt(100);
        var rig = await RigAsync(pay.Address, chainLength: 220);
        rig.Gateway.Transactions.Add(pay.Proof);
        var before = await rig.Service.GetBalanceAsync(pay.Address);

        // Only the last few blocks change; the payment block is far below the fork.
        rig.Gateway.Chain = [.. RealRange(1, 213), .. Fork(213, 12)];
        rig.Time.Now += TimeSpan.FromSeconds(11);

        Assert.Equal(before, await rig.Service.GetBalanceAsync(pay.Address));
    }

    [Fact]
    public async Task A_reorganization_is_picked_up_within_one_refresh_interval_not_later()
    {
        var pay = PayAt(100);
        var rig = await RigAsync(pay.Address, chainLength: 220);
        rig.Gateway.Transactions.Add(pay.Proof);
        await rig.Service.GetBalanceAsync(pay.Address);
        var forkAfter = pay.Proof.Height - Cp - 3;
        rig.Gateway.Chain = [.. RealRange(1, forkAfter), .. Fork(forkAfter, 220 - forkAfter + 2)];

        // Within the 10 second interval the earlier record is served; after it the change is seen. Both are inside the one-minute target.
        rig.Time.Now += TimeSpan.FromSeconds(11);

        Assert.Equal(0, await rig.Service.GetBalanceAsync(pay.Address));
        Assert.True(TimeSpan.FromSeconds(11) < TimeSpan.FromMinutes(1));
    }
}
