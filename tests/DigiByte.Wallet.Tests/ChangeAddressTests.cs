using System.Text.Json;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Change must always land on an address the wallet scans, and a change address must not be handed out twice.
/// These run real transactions against <see cref="FakeChain"/>, which spends inputs and creates outputs on broadcast.
/// </summary>
public class ChangeAddressTests
{
    internal static readonly Network Network = DigiByteNetwork.Mainnet;
    internal const string Pin = "123456";
    internal const long OneDgb = 100_000_000;
    private const long Funding = 1000 * OneDgb;

    internal sealed class Env
    {
        public InMemoryStorage Storage { get; } = new();
        public FakeChain Chain { get; } = new();
        public string Words { get; } = string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);
        public HdKeyDerivation Hd => new(MnemonicGenerator.FromWords(Words), network: Network);
        public string ChangeAddress(int i) => Hd.DeriveChangeAddress(i, ScriptPubKeyType.Segwit).ToString();
        public string ReceiveAddress(int i) => Hd.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString();

        public WalletService NewService()
        {
            var service = new WalletService(new WalletKeyStore(Storage), new PassThroughCrypto(), Chain, new TransactionTracker(Storage));
            service.SetNetworkMode("mainnet");
            // Generous, so a busy test run can't time out a discovery that these tests expect to finish.
            service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
            service.AddressDiscoverySendWait = TimeSpan.FromMinutes(1);
            return service;
        }

        public async Task<WalletService> CreateFundedAsync()
        {
            var service = NewService();
            await service.CreateWalletAsync("Test", Words, Pin);
            Chain.Credit(ReceiveAddress(0), Funding);
            return service;
        }

        public async Task<WalletService> ReloadAsync(string walletId)
        {
            var service = NewService();
            Assert.True(await service.UnlockWalletAsync(walletId, Pin));
            return service;
        }
    }

    internal static string Stranger() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Network).ToString();

    /// <summary>The wallet's own outputs in the last transaction: its change.</summary>
    private static string LastChangeAddress(Env env) =>
        env.Chain.LastBroadcast!.Outputs
            .Select(o => o.ScriptPubKey.GetDestinationAddress(Network)?.ToString())
            .Single(a => a is not null && a != env.Chain.LastRecipient)!;

    // ─── Change past index 9 must stay visible and spendable ───

    [Fact]
    public async Task SendAsync_ManySends_BalanceKeepsAllChange()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();

        for (var i = 0; i < 12; i++)
        {
            env.Chain.LastRecipient = Stranger();
            await service.SendAsync(env.Chain.LastRecipient, 1m);
        }

        var balance = await service.GetBalanceAsync();
        Assert.Equal(Funding - 12 * OneDgb - env.Chain.TotalFees, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task SendBatchAsync_ManySends_BalanceKeepsAllChange()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();

        for (var i = 0; i < 12; i++)
            await service.SendBatchAsync([(Stranger(), 1m), (Stranger(), 1m)]);

        var balance = await service.GetBalanceAsync();
        Assert.Equal(Funding - 24 * OneDgb - env.Chain.TotalFees, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task SendAsync_ChangePastIndexNine_CanBeSpent()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        for (var i = 0; i < 12; i++)
            await service.SendAsync(Stranger(), 1m);

        // All the wallet's money now sits in the latest change output.
        Assert.Equal(1, env.Chain.UnspentOutputsOwnedBy(env.ChangeAddress(11)));

        var txid = await service.SendAsync(Stranger(), 900m);

        Assert.False(string.IsNullOrEmpty(txid));
        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(env.ChangeAddress(11)));
    }

    // ─── The index must survive a reload ───

    [Fact]
    public async Task NextChangeIndex_IsSaved_AfterEachKindOfSend()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        var walletId = service.ActiveWallet!.Id;

        await service.SendAsync(Stranger(), 1m);
        await service.SendBatchAsync([(Stranger(), 1m), (Stranger(), 1m)]);

        var saved = JsonSerializer.Deserialize<WalletInfo>((await new WalletKeyStore(env.Storage).GetWalletInfoAsync(walletId))!)!;
        Assert.Equal(2, saved.NextChangeIndex);
    }

    [Fact]
    public async Task Reload_DoesNotReuseAChangeAddress()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        var walletId = service.ActiveWallet!.Id;

        env.Chain.LastRecipient = Stranger();
        await service.SendAsync(env.Chain.LastRecipient, 1m);
        var first = LastChangeAddress(env);

        var reloaded = await env.ReloadAsync(walletId);
        env.Chain.LastRecipient = Stranger();
        await reloaded.SendAsync(env.Chain.LastRecipient, 1m);

        Assert.NotEqual(first, LastChangeAddress(env));
    }

    [Fact]
    public async Task RenameAndRecolor_KeepTheSavedIndex()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        var walletId = service.ActiveWallet!.Id;
        await service.SendAsync(Stranger(), 1m);

        await service.RenameWalletAsync(walletId, "Renamed");
        await service.ChangeWalletColorAsync(walletId, "#123456");

        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(1, reloaded.ActiveWallet!.NextChangeIndex);
    }

    // ─── Funds already stranded by older versions, whose saved index is still 0 ───

    [Fact]
    public async Task Balance_FindsChangeStrandedPastTheOldRange()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.Credit(env.ChangeAddress(14), 5 * OneDgb);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5 * OneDgb, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Send_CanSpendChangeStrandedPastTheOldRange()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.Credit(env.ChangeAddress(14), 5 * OneDgb);

        await service.SendAsync(Stranger(), 1m);

        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(env.ChangeAddress(14)));
    }

    [Fact]
    public async Task Discovery_FollowsUsedAddressesAcrossGaps()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.Credit(env.ChangeAddress(14), OneDgb);
        env.Chain.Credit(env.ChangeAddress(30), 2 * OneDgb);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(3 * OneDgb, balance.ConfirmedSatoshis);
        Assert.Equal(31, service.ActiveWallet!.NextChangeIndex);
    }

    [Fact]
    public async Task Discovery_MovesAnOldWalletPastUsedChange_AndSavesIt()
    {
        var env = new Env();
        var service = env.NewService();
        var wallet = await service.CreateWalletAsync("Test", env.Words, Pin);
        for (var i = 0; i < 3; i++) env.Chain.Credit(env.ChangeAddress(i), OneDgb);

        await service.GetBalanceAsync();

        Assert.Equal(3, service.ActiveWallet!.NextChangeIndex);
        var reloaded = await env.ReloadAsync(wallet.Id);
        Assert.Equal(3, reloaded.ActiveWallet!.NextChangeIndex);
    }

    [Fact]
    public async Task Discovery_WhenExplorerIsDown_DoesNotBreakBalance_AndRetriesLater()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        service.AddressDiscoveryRetryDelay = TimeSpan.Zero;
        env.Chain.Credit(env.ReceiveAddress(0), OneDgb);
        env.Chain.Credit(env.ChangeAddress(14), 5 * OneDgb);

        env.Chain.HistoryDown = true;
        Assert.Equal(OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        env.Chain.HistoryDown = false;
        Assert.Equal(6 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Discovery_RunsOncePerUnlock()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);

        await service.GetBalanceAsync();
        var afterFirst = env.Chain.HistoryLookups;
        await service.GetBalanceAsync();

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, env.Chain.HistoryLookups);
    }

    [Fact]
    public async Task Discovery_WhenEveryAddressShowsHistory_StopsAndKeepsTheIndex()
    {
        // MockBlockchainService and a misbehaving explorer both answer like this. Believing them would never end.
        var env = new Env();
        var service = env.NewService();
        var wallet = await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.EveryAddressHasHistory = true;

        await service.GetBalanceAsync();
        var afterFirst = env.Chain.HistoryLookups;
        await service.GetBalanceAsync();

        Assert.InRange(afterFirst, 1, 1000);
        Assert.Equal(afterFirst, env.Chain.HistoryLookups); // an answer like this is not worth asking for again
        Assert.Equal(0, service.ActiveWallet!.NextChangeIndex);
        Assert.Equal(60, service.GetAllAddresses().Count);
        var reloaded = await env.ReloadAsync(wallet.Id);
        Assert.Equal(0, reloaded.ActiveWallet!.NextChangeIndex);
    }

    [Fact]
    public async Task WatchOnlyWallet_FindsChangePastTheOldRange()
    {
        var env = new Env();
        var xpub = env.Hd.GetAccountExtPubKey().ToString(Network);
        var service = env.NewService();
        await service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);
        env.Chain.Credit(env.ChangeAddress(14), 5 * OneDgb);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5 * OneDgb, balance.ConfirmedSatoshis);
    }

    // ─── A send still succeeds if the counter cannot be saved: the money has already left ───

    [Fact]
    public async Task SendAsync_WhenSavingTheIndexFails_StillReturnsTheTxid()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        env.Storage.FailWalletInfoWrites = true;

        var txid = await service.SendAsync(Stranger(), 1m);

        Assert.False(string.IsNullOrEmpty(txid));
        Assert.Equal(1, service.ActiveWallet!.NextChangeIndex);
    }

    // ─── DigiDollar pays its DGB leftovers to the same chain ───

    [Fact]
    public async Task DigiDollarChangeScript_PastIndexNine_IsScanned()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        for (var i = 0; i < 12; i++)
            await service.SendAsync(Stranger(), 1m);

        var script = await service.GetDgbChangeScriptAsync(CancellationToken.None);

        Assert.Contains(script.GetDestinationAddress(Network)!.ToString(), service.GetAllAddresses());
    }

    [Fact]
    public async Task DigiDollarChange_WhenMarkedUsed_MovesOn_AndIsSaved()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        var walletId = service.ActiveWallet!.Id;
        var before = await service.GetDgbChangeScriptAsync(CancellationToken.None);

        await service.MarkDgbChangeUsedAsync();

        Assert.NotEqual(before, await service.GetDgbChangeScriptAsync(CancellationToken.None));
        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(1, reloaded.ActiveWallet!.NextChangeIndex);
    }

    [Fact]
    public async Task DigiDollarFeeUtxos_IncludeChangePastIndexNine()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.Credit(env.ChangeAddress(14), 5 * OneDgb);

        var utxos = await service.GetFeeUtxosAsync(CancellationToken.None);

        Assert.Equal(5 * OneDgb, Assert.Single(utxos).ValueSats);
    }

    // ─── The key map cache must follow the active wallet ───

    [Fact]
    public async Task KeyMap_DoesNotLeakAcrossWallets()
    {
        var env = new Env();
        var service = await env.CreateFundedAsync();
        await service.GetFeeUtxosAsync(CancellationToken.None); // caches wallet A's key map

        var otherWords = string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);
        await service.CreateWalletAsync("Other", otherWords, Pin);
        service.GetAllAddresses();

        Assert.Empty(await service.GetFeeUtxosAsync(CancellationToken.None));
    }

    private static string Words(Env env) => env.Words;

    // ─── Fakes ───

    internal sealed class InMemoryStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _store = new();
        public bool FailWalletInfoWrites { get; set; }

        public Task<string?> GetAsync(string key) => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value)
        {
            if (FailWalletInfoWrites && value.Contains("NextChangeIndex"))
                throw new IOException("storage is full");
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task ClearAsync() { _store.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
            Task.FromResult(_store.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    internal sealed class PassThroughCrypto : LegacyCryptoBase
    {
        public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
    }

    /// <summary>A minimal chain: broadcasting spends the inputs and creates the outputs, all treated as confirmed.</summary>
    internal sealed class FakeChain : IBlockchainService
    {
        private sealed record Output(string TxId, uint Vout, string Address, long Sats, string ScriptHex);

        private readonly List<Output> _unspent = [];
        private readonly HashSet<string> _everUsed = [];
        private int _credits;

        public long TotalFees { get; private set; }
        public Transaction? LastBroadcast { get; private set; }
        public string? LastRecipient { get; set; }
        public bool EveryAddressHasHistory { get; set; }
        public bool HistoryDown { get; set; }
        public int HistoryLookups { get; private set; }

        /// <summary>While set, discovery's lookups wait for this task, to imitate a slow explorer.</summary>
        public Task? HistoryGate { get; set; }

        public async Task<bool> HasAddressHistoryAsync(string address)
        {
            if (HistoryGate is { } gate) await gate;
            HistoryLookups++;
            if (HistoryDown) throw new HttpRequestException("explorer is down");
            // A fuse, so an endless search fails this test instead of hanging the test run.
            if (HistoryLookups > 5000) throw new InvalidOperationException("Too many history lookups.");
            return EveryAddressHasHistory || _everUsed.Contains(address);
        }

        public void Credit(string address, long sats)
        {
            var txid = (++_credits).ToString("x").PadLeft(64, 'c');
            var script = BitcoinAddress.Create(address, Network).ScriptPubKey.ToHex();
            _unspent.Add(new Output(txid, 0, address, sats, script));
            _everUsed.Add(address);
        }

        public int UnspentOutputsOwnedBy(string address) => _unspent.Count(o => o.Address == address);

        public Task<long> GetBalanceAsync(string address) =>
            Task.FromResult(_unspent.Where(o => o.Address == address).Sum(o => o.Sats));

        public Task<long> GetBalanceAsync(IEnumerable<string> addresses)
        {
            var set = addresses.ToHashSet();
            return Task.FromResult(_unspent.Where(o => set.Contains(o.Address)).Sum(o => o.Sats));
        }

        public Task<List<UtxoInfo>> GetUtxosAsync(string address) =>
            Task.FromResult(_unspent.Where(o => o.Address == address).Select(ToInfo).ToList());

        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
        {
            var set = addresses.ToHashSet();
            return Task.FromResult(_unspent.Where(o => set.Contains(o.Address)).Select(ToInfo).ToList());
        }

        private static UtxoInfo ToInfo(Output o) => new()
        {
            TxId = o.TxId, OutputIndex = o.Vout, AmountSatoshis = o.Sats, ScriptPubKey = o.ScriptHex, Confirmations = 6,
        };

        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
        {
            var tx = Transaction.Load(rawTransaction, Network);
            var txid = tx.GetHash().ToString();
            long spent = 0;
            foreach (var input in tx.Inputs)
            {
                var prev = _unspent.Single(o => o.TxId == input.PrevOut.Hash.ToString() && o.Vout == input.PrevOut.N);
                spent += prev.Sats;
                _unspent.Remove(prev);
            }

            long paid = 0;
            for (uint i = 0; i < tx.Outputs.Count; i++)
            {
                var output = tx.Outputs[(int)i];
                paid += output.Value.Satoshi;
                var address = output.ScriptPubKey.GetDestinationAddress(Network)?.ToString();
                if (address is null) continue;
                _unspent.Add(new Output(txid, i, address, output.Value.Satoshi, output.ScriptPubKey.ToHex()));
                _everUsed.Add(address);
            }

            TotalFees += spent - paid;
            LastBroadcast = tx;
            return Task.FromResult(txid);
        }

        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
        {
            HistoryLookups++;
            if (HistoryDown) throw new HttpRequestException("explorer is down");
            // A fuse, so an endless search fails this test instead of hanging the test run.
            if (HistoryLookups > 5000) throw new InvalidOperationException("Too many history lookups.");
            var txs = new List<TransactionInfo>();
            if (EveryAddressHasHistory || _everUsed.Contains(address))
                txs.Add(new TransactionInfo
                {
                    TxId = new string('e', 64), Confirmations = 6, Timestamp = DateTime.UtcNow, FeeSatoshis = 0, Inputs = [], Outputs = [],
                });
            return Task.FromResult(txs);
        }

        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }
}
