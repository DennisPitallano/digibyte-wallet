using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// An Esplora-like explorer over a scripted chain (spec 023). It answers address summaries, newest-first history pages of 25
/// confirmed transactions (plus every pending one on the newest page), full history lists and the tip height, and it counts
/// every request by kind. Chosen addresses or pages can be made to fail, and every answer can be delayed or held.
/// </summary>
internal sealed class SummaryExplorer : IBlockchainService, IAddressSummarySource
{
    public const int PageSize = 25;

    public sealed class ScriptedTx
    {
        public required string TxId { get; init; }
        public int? Height { get; set; }
        public int Order { get; init; }
        public List<(string Address, long Sats)> Inputs { get; } = [];
        public List<(string Address, long Sats)> Outputs { get; } = [];
        public long Fee { get; init; }
        public bool Involves(string address) =>
            Inputs.Any(i => i.Address == address) || Outputs.Any(o => o.Address == address);
    }

    private readonly object _gate = new();
    private readonly List<ScriptedTx> _txs = [];
    private int _order;

    public int Tip { get; set; } = 1000;

    // Request counts by kind.
    private int _summary, _pages, _tip, _balance, _addressTxs, _history;
    public int SummaryRequests => _summary;
    public int PageRequests => _pages;
    public int TipRequests => _tip;
    public int BalanceRequests => _balance;
    public int AddressTxRequests => _addressTxs;
    public int HistoryLookups => _history;
    public List<string> PagedAddresses { get; } = [];
    public List<string> SummaryAddresses { get; } = [];
    private int _pagesInFlight;
    public int MaxPagesInFlight { get; private set; }

    public void ResetCounts()
    {
        lock (_gate)
        {
            _summary = _pages = _tip = _balance = _addressTxs = _history = 0;
            PagedAddresses.Clear();
            SummaryAddresses.Clear();
            MaxPagesInFlight = 0;
        }
    }

    public HashSet<string> FailSummaryFor { get; } = [];
    public HashSet<string> FailPagesFor { get; } = [];
    public bool FailEverything { get; set; }

    /// <summary>Summaries and pages throw <see cref="SummaryUnavailableException"/>, as the Fallback does on regtest.</summary>
    public bool Unavailable { get; set; }

    /// <summary>While set, every summary and page waits for it (to hold reads in flight).</summary>
    public Task? Hold { get; set; }

    public TimeSpan Delay { get; set; }

    private async Task Pause()
    {
        if (Hold is { } hold) await hold;
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay);
        else await Task.Yield();
    }

    // ── Scripting ──

    public ScriptedTx Pay(string to, long sats, int? height = 900, string? from = null, long fee = 0, string? txid = null)
    {
        lock (_gate)
        {
            var order = ++_order;
            var tx = new ScriptedTx { TxId = txid ?? order.ToString("x").PadLeft(64, '0'), Height = height, Order = order, Fee = fee };
            tx.Inputs.Add((from ?? "dgb1qsomeoneelse0000000000000000xyz", sats + fee));
            tx.Outputs.Add((to, sats));
            _txs.Add(tx);
            return tx;
        }
    }

    /// <summary>A send from <paramref name="from"/> of <paramref name="sats"/> to a stranger, with change back.</summary>
    public ScriptedTx Spend(string from, long inputSats, long sats, long fee, string change, int? height = null)
    {
        lock (_gate)
        {
            var tx = new ScriptedTx { TxId = ("s" + (++_order).ToString("x")).PadLeft(64, '0'), Height = height, Order = _order, Fee = fee };
            tx.Inputs.Add((from, inputSats));
            tx.Outputs.Add(("dgb1qstranger00000000000000000000xyz", sats));
            tx.Outputs.Add((change, inputSats - sats - fee));
            _txs.Add(tx);
            return tx;
        }
    }

    public void Confirm(ScriptedTx tx, int height) { lock (_gate) tx.Height = height; }
    public void Drop(ScriptedTx tx) { lock (_gate) _txs.Remove(tx); }

    private List<ScriptedTx> NewestFirst(string address)
    {
        lock (_gate)
        {
            var mine = _txs.Where(t => t.Involves(address)).ToList();
            return mine.Where(t => t.Height is null).OrderByDescending(t => t.Order)
                .Concat(mine.Where(t => t.Height is not null).OrderByDescending(t => t.Height).ThenByDescending(t => t.Order))
                .ToList();
        }
    }

    private TransactionInfo ToInfo(ScriptedTx t) => new()
    {
        TxId = t.TxId,
        Confirmations = t.Height is { } h ? Tip - h + 1 : 0,
        Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(t.Order),
        FeeSatoshis = t.Fee,
        Inputs = t.Inputs.Select(i => new TxInput { Address = i.Address, AmountSatoshis = i.Sats }).ToList(),
        Outputs = t.Outputs.Select((o, i) => new TxOutput { Address = o.Address, AmountSatoshis = o.Sats, Index = (uint)i }).ToList(),
    };

    public long ConfirmedBalance(IEnumerable<string> addresses) => addresses.Sum(a => SummaryOf(a).Confirmed);

    private AddressSummary SummaryOf(string address)
    {
        lock (_gate)
        {
            var mine = _txs.Where(t => t.Involves(address)).ToList();
            var confirmed = mine.Where(t => t.Height is not null).ToList();
            var pending = mine.Where(t => t.Height is null).ToList();
            return new AddressSummary
            {
                Address = address,
                ConfirmedTxCount = confirmed.Count,
                PendingTxCount = pending.Count,
                ConfirmedFunded = confirmed.SelectMany(t => t.Outputs).Where(o => o.Address == address).Sum(o => o.Sats),
                ConfirmedSpent = confirmed.SelectMany(t => t.Inputs).Where(i => i.Address == address).Sum(i => i.Sats),
                PendingFunded = pending.SelectMany(t => t.Outputs).Where(o => o.Address == address).Sum(o => o.Sats),
                PendingSpent = pending.SelectMany(t => t.Inputs).Where(i => i.Address == address).Sum(i => i.Sats),
                ReadAtUtc = DateTime.UtcNow,
            };
        }
    }

    // ── IAddressSummarySource ──

    public async Task<AddressSummary> GetAddressSummaryAsync(string address, bool recordFailure = true)
    {
        if (Unavailable) throw new SummaryUnavailableException("not on this network");
        Interlocked.Increment(ref _summary);
        lock (_gate) SummaryAddresses.Add(address);
        await Pause();
        if (FailEverything || FailSummaryFor.Contains(address)) throw new HttpRequestException("429 Too Many Requests");
        return SummaryOf(address);
    }

    public async Task<AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId)
    {
        if (Unavailable) throw new SummaryUnavailableException("not on this network");
        Interlocked.Increment(ref _pages);
        lock (_gate)
        {
            PagedAddresses.Add(address);
            MaxPagesInFlight = Math.Max(MaxPagesInFlight, ++_pagesInFlight);
        }
        try { await Pause(); }
        finally { lock (_gate) _pagesInFlight--; }
        if (FailEverything || FailPagesFor.Contains(address)) throw new HttpRequestException("503 Service Unavailable");

        var all = NewestFirst(address);
        List<ScriptedTx> page;
        if (afterTxId is null)
            page = all.Where(t => t.Height is null).Concat(all.Where(t => t.Height is not null).Take(PageSize)).ToList();
        else
        {
            var confirmed = all.Where(t => t.Height is not null).ToList();
            var at = confirmed.FindIndex(t => t.TxId == afterTxId);
            page = at < 0 ? [] : confirmed.Skip(at + 1).Take(PageSize).ToList();
        }

        return new AddressHistoryPage(
            page.Select(ToInfo).ToList(),
            page.LastOrDefault(t => t.Height is not null)?.TxId,
            page.Where(t => t.Height is not null).ToDictionary(t => t.TxId, t => t.Height!.Value));
    }

    // ── IBlockchainService ──

    public async Task<int> GetBlockHeightAsync()
    {
        Interlocked.Increment(ref _tip);
        await Task.Yield();
        return Tip;
    }

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        var list = addresses.ToList();
        Interlocked.Add(ref _balance, list.Count);
        await Pause();
        if (FailEverything || list.Any(FailSummaryFor.Contains)) throw new HttpRequestException("429 Too Many Requests");
        return ConfirmedBalance(list);
    }

    public Task<long> GetBalanceAsync(string address) => GetBalanceAsync([address]);

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        Interlocked.Increment(ref _addressTxs);
        await Pause();
        if (FailEverything || FailPagesFor.Contains(address)) throw new HttpRequestException("503 Service Unavailable");
        return NewestFirst(address).Select(ToInfo).Skip(skip).Take(take).ToList();
    }

    public async Task<bool> HasAddressHistoryAsync(string address)
    {
        Interlocked.Increment(ref _history);
        await Task.Yield();
        if (FailEverything) throw new HttpRequestException("explorer is down");
        return SummaryOf(address).IsUsed;
    }

    /// <summary>Spendable coins per address, for building real sends (spec 024).</summary>
    public Dictionary<string, List<UtxoInfo>> Coins { get; } = new();
    private int _coinReads;
    public int CoinReads => _coinReads;
    public List<string> CoinReadAddresses { get; } = [];

    public Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        Interlocked.Increment(ref _coinReads);
        lock (_gate) CoinReadAddresses.Add(address);
        return Task.FromResult(Coins.TryGetValue(address, out var coins) ? coins.ToList() : new List<UtxoInfo>());
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) =>
        Task.FromResult(addresses.SelectMany(a => Coins.TryGetValue(a, out var c) ? c : []).ToList());

    public void ResetCoinReads()
    {
        lock (_gate) CoinReadAddresses.Clear();
        _coinReads = 0;
    }
    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult(new string('f', 64));
    public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
    public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
}

/// <summary>A seed wallet over a <see cref="SummaryExplorer"/>, with change-only sync wired in and a fake clock.</summary>
internal sealed class HistorySyncEnv
{
    public ChangeAddressTests.InMemoryStorage Storage { get; } = new();
    public SummaryExplorer Explorer { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Tiering off by default, so spec 023 tests keep checking 0.7.0 behaviour; spec 024 tests turn it on.</summary>
    public HistorySyncOptions Options { get; } = new() { Tiered = false };
    public string Words { get; } = string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words);
    public HdKeyDerivation Hd => new(MnemonicGenerator.FromWords(Words), network: ChangeAddressTests.Network);
    public string Receive(int i) => Hd.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString();
    public string Change(int i) => Hd.DeriveChangeAddress(i, ScriptPubKeyType.Segwit).ToString();

    public AddressSummaryStore Store { get; }
    public WalletHistorySync Sync { get; private set; }

    public HistorySyncEnv()
    {
        Store = new AddressSummaryStore(Storage);
        Sync = NewSync();
    }

    public WalletHistorySync NewSync() => new(Explorer, Store, Clock) { FullResyncRetryDelay = TimeSpan.FromMinutes(1) };

    /// <summary>A fresh in-memory session (as after a reload) over the same saved data.</summary>
    public void Reload() => Sync = NewSync();

    public WalletService NewService(IBlockchainService? chain = null)
    {
        var service = new WalletService(new WalletKeyStore(Storage), new ChangeAddressTests.PassThroughCrypto(), chain ?? Explorer,
            new TransactionTracker(Storage), historySync: Sync, historyOptions: Options);
        service.SetNetworkMode("mainnet");
        service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        service.AddressDiscoverySendWait = TimeSpan.FromMinutes(1);
        return service;
    }

    /// <summary>A confirmed payment to the address that is also a real spendable coin, so a send can be built and signed.</summary>
    public SummaryExplorer.ScriptedTx Fund(string address, long sats, int height = 900)
    {
        var tx = Explorer.Pay(address, sats, height: height);
        var script = BitcoinAddress.Create(address, ChangeAddressTests.Network).ScriptPubKey.ToHex();
        if (!Explorer.Coins.TryGetValue(address, out var coins))
            Explorer.Coins[address] = coins = [];
        coins.Add(new UtxoInfo { TxId = tx.TxId, OutputIndex = 0, AmountSatoshis = sats, ScriptPubKey = script, Confirmations = 6 });
        return tx;
    }

    public async Task<WalletService> CreateAsync()
    {
        var service = NewService();
        await service.CreateWalletAsync("Test", Words, ChangeAddressTests.Pin);
        return service;
    }
}
