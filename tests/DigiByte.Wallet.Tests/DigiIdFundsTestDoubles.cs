using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A chain for the Digi-ID funds recovery tests. It can answer or fail per address, records every call with the addresses it
/// carried (so a test can prove each Digi-ID address went in its own request), refuses to spend an output twice, and lets a
/// test set confirmations for broadcast transactions.
/// </summary>
internal sealed class DigiIdFakeChain : IBlockchainService
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;

    internal sealed class FakeUtxo
    {
        public required string TxId { get; init; }
        public required uint Vout { get; init; }
        public required string Address { get; init; }
        public required long Sats { get; init; }
        public int Confirmations { get; set; }
        public string? ScriptHex { get; init; }
    }

    internal sealed record Call(string Method, string[] Addresses);

    private readonly List<FakeUtxo> _utxos = [];
    private readonly Dictionary<string, int> _txConfirmations = new();
    private readonly object _gate = new();
    private int _counter;

    public List<Call> Calls { get; } = [];
    public List<Transaction> Broadcasts { get; } = [];

    /// <summary>Lookups of these addresses throw, to imitate an explorer that cannot answer for them.</summary>
    public HashSet<string> ThrowForAddresses { get; } = [];

    /// <summary>Every lookup throws.</summary>
    public bool ThrowForEverything { get; set; }

    /// <summary>Transaction-status lookups throw.</summary>
    public bool TransactionLookupFails { get; set; }

    /// <summary>A broadcast that spends an output of one of these addresses is rejected by the network.</summary>
    public HashSet<string> RejectBroadcastFrom { get; } = [];

    /// <summary>Runs on each verified lookup with its address, before it answers, for example to cancel a scan part way.</summary>
    public Action<string>? OnVerifiedLookup { get; set; }

    /// <summary>Runs before each broadcast is accepted, for example to lock the wallet between two items.</summary>
    public Action<int>? OnBeforeBroadcast { get; set; }

    /// <summary>How many broadcasts were tried, counting rejected ones. The callback above gets this count (1 for the first).</summary>
    public int BroadcastAttempts { get; private set; }

    /// <summary>Adds an unspent output. The script defaults to the address's own standard script; pass one to imitate another kind of output.</summary>
    public FakeUtxo AddUtxo(string address, long sats, int confirmations = 6, string? scriptHex = null)
    {
        lock (_gate)
        {
            var utxo = new FakeUtxo
            {
                TxId = (++_counter).ToString("x").PadLeft(64, 'd'),
                Vout = 0,
                Address = address,
                Sats = sats,
                Confirmations = confirmations,
                ScriptHex = scriptHex ?? BitcoinAddress.Create(address, Network).ScriptPubKey.ToHex(),
            };
            _utxos.Add(utxo);
            return utxo;
        }
    }

    public void Confirm(string txId, int confirmations)
    {
        lock (_gate)
        {
            _txConfirmations[txId] = confirmations;
            foreach (var u in _utxos.Where(u => u.TxId == txId)) u.Confirmations = confirmations;
        }
    }

    /// <summary>The network forgets a transaction, as if it was dropped.</summary>
    public void Drop(string txId)
    {
        lock (_gate)
        {
            _txConfirmations.Remove(txId);
            _utxos.RemoveAll(u => u.TxId == txId);
        }
    }

    public int UnspentCount(string address)
    {
        lock (_gate) return _utxos.Count(u => u.Address == address);
    }

    public long UnspentSats(string address)
    {
        lock (_gate) return _utxos.Where(u => u.Address == address).Sum(u => u.Sats);
    }

    public IReadOnlyList<Call> CallsTouching(IEnumerable<string> addresses)
    {
        var set = addresses.ToHashSet();
        lock (_gate) return Calls.Where(c => c.Addresses.Any(set.Contains)).ToList();
    }

    private void Log(string method, params string[] addresses)
    {
        lock (_gate) Calls.Add(new Call(method, addresses));
    }

    private void FailIfAsked(string address)
    {
        if (ThrowForEverything || ThrowForAddresses.Contains(address))
            throw new HttpRequestException($"explorer cannot answer for {address}");
    }

    private static UtxoInfo ToInfo(FakeUtxo u) => new()
    {
        TxId = u.TxId,
        OutputIndex = u.Vout,
        AmountSatoshis = u.Sats,
        ScriptPubKey = u.ScriptHex ?? "",
        Confirmations = u.Confirmations,
    };

    private List<UtxoInfo> UtxosFor(string address)
    {
        lock (_gate) return _utxos.Where(u => u.Address == address).Select(ToInfo).ToList();
    }

    // ─── IBlockchainService ───

    public Task<List<UtxoInfo>> GetVerifiedUtxosAsync(string address)
    {
        Log(nameof(GetVerifiedUtxosAsync), address);
        OnVerifiedLookup?.Invoke(address);
        FailIfAsked(address);
        return Task.FromResult(UtxosFor(address));
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        Log(nameof(GetUtxosAsync), address);
        FailIfAsked(address);
        return Task.FromResult(UtxosFor(address));
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var list = addresses.ToArray();
        Log("GetUtxosAsync(batch)", list);
        foreach (var a in list) FailIfAsked(a);
        return Task.FromResult(list.SelectMany(UtxosFor).ToList());
    }

    public Task<long> GetBalanceAsync(string address)
    {
        Log(nameof(GetBalanceAsync), address);
        FailIfAsked(address);
        return Task.FromResult(UtxosFor(address).Sum(u => u.AmountSatoshis));
    }

    public Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        var list = addresses.ToArray();
        Log("GetBalanceAsync(batch)", list);
        foreach (var a in list) FailIfAsked(a);
        return Task.FromResult(list.SelectMany(UtxosFor).Sum(u => u.AmountSatoshis));
    }

    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        BroadcastAttempts++;
        OnBeforeBroadcast?.Invoke(BroadcastAttempts);
        Log(nameof(BroadcastTransactionAsync));

        var tx = Transaction.Load(rawTransaction, Network);
        lock (_gate)
        {
            var spent = new List<FakeUtxo>();
            foreach (var input in tx.Inputs)
            {
                var prev = _utxos.SingleOrDefault(u => u.TxId == input.PrevOut.Hash.ToString() && u.Vout == input.PrevOut.N)
                    ?? throw new HttpRequestException("bad-txns-inputs-missingorspent");
                if (RejectBroadcastFrom.Contains(prev.Address))
                    throw new HttpRequestException("rejected by the network");
                spent.Add(prev);
            }

            foreach (var s in spent) _utxos.Remove(s);

            var txId = tx.GetHash().ToString();
            for (uint i = 0; i < tx.Outputs.Count; i++)
            {
                var address = tx.Outputs[(int)i].ScriptPubKey.GetDestinationAddress(Network)?.ToString();
                if (address is null) continue;
                _utxos.Add(new FakeUtxo
                {
                    TxId = txId, Vout = i, Address = address, Sats = tx.Outputs[(int)i].Value.Satoshi, Confirmations = 0,
                    ScriptHex = tx.Outputs[(int)i].ScriptPubKey.ToHex(),
                });
            }

            _txConfirmations[txId] = 0;
            Broadcasts.Add(tx);
            return Task.FromResult(txId);
        }
    }

    public Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        Log(nameof(GetTransactionAsync), txId);
        if (ThrowForEverything || TransactionLookupFails) throw new HttpRequestException("explorer is down");
        lock (_gate)
        {
            if (!_txConfirmations.TryGetValue(txId, out var confirmations)) return Task.FromResult<TransactionInfo?>(null);
            return Task.FromResult<TransactionInfo?>(new TransactionInfo
            {
                TxId = txId, Confirmations = confirmations, Timestamp = DateTime.UtcNow, FeeSatoshis = 0, Inputs = [], Outputs = [],
            });
        }
    }

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
        Task.FromResult(new List<TransactionInfo>());

    public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0m);
    public Task<int> GetBlockHeightAsync() => Task.FromResult(1);
}

/// <summary>
/// A wallet, its storage and a <see cref="DigiIdFakeChain"/>, for the Digi-ID funds recovery tests.
/// </summary>
internal sealed class DigiIdEnv
{
    public const string Pin = "123456";
    public static readonly Network Network = DigiByteNetwork.Mainnet;

    public ChangeAddressTests.InMemoryStorage Storage { get; } = new();
    public DigiIdFakeChain Chain { get; } = new();

    /// <summary>The store the wallet under test writes sweep records to, so a test can read or seed it.</summary>
    public DigiIdSweepStore SweepStore { get; }

    public DigiIdEnv()
    {
        SweepStore = new DigiIdSweepStore(Storage);
    }

    public string Words { get; } = string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);
    public HdKeyDerivation Hd => new(MnemonicGenerator.FromWords(Words), network: Network);

    /// <summary>The Digi-ID address the wallet derives for a site, worked out here independently of the wallet code under test.</summary>
    public string DigiIdAddress(string domain) =>
        DigiIdService.GetAddress(Hd.DeriveDigiIdKey(DigiIdService.DeriveSiteIndex(domain)).PrivateKey);

    public string ReceiveAddress(int index) => Hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString();

    public WalletService NewService()
    {
        var service = new WalletService(new WalletKeyStore(Storage), new ChangeAddressTests.PassThroughCrypto(), Chain, new TransactionTracker(Storage), SweepStore);
        service.SetNetworkMode("mainnet");
        // Generous, so a busy test run cannot time out a discovery that these tests expect to finish.
        service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        service.AddressDiscoverySendWait = TimeSpan.FromMinutes(1);
        return service;
    }

    /// <summary>An unlocked HD wallet.</summary>
    public async Task<WalletService> CreateUnlockedHdAsync()
    {
        var service = NewService();
        await service.CreateWalletAsync("Test", Words, Pin);
        return service;
    }

    /// <summary>An HD wallet that has been locked.</summary>
    public async Task<WalletService> CreateLockedHdAsync()
    {
        var service = await CreateUnlockedHdAsync();
        await service.LockWalletAsync();
        return service;
    }

    /// <summary>A watch-only (xpub) wallet.</summary>
    public async Task<WalletService> CreateWatchOnlyAsync()
    {
        var service = NewService();
        var xpub = Hd.GetAccountExtPubKey().ToString(Network);
        await service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);
        return service;
    }

    /// <summary>A single-key (WIF) wallet. It has no seed, so it has no Digi-ID addresses.</summary>
    public async Task<WalletService> CreateWifAsync()
    {
        var service = NewService();
        await service.CreateWalletFromWifAsync("Wif", new Key().GetWif(Network).ToString(), Pin);
        return service;
    }
}
