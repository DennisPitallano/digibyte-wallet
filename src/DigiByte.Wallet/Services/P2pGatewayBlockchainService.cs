using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>The wallet the gateway source answers for: its id, every address it watches, and which of them are change addresses.</summary>
public sealed record GatewayWalletScope(string WalletId, IReadOnlyList<string> Addresses, IReadOnlySet<string> ChangeAddresses);

/// <summary>Tells the gateway source which wallet is active and what it watches. Nothing from it is sent to the gateway except addresses.</summary>
public interface IGatewayWalletScopeProvider
{
    Task<GatewayWalletScope?> GetScopeAsync();
}

/// <summary>Where signed transactions are sent. The gateway client is the real one.</summary>
public interface IGatewayBroadcaster
{
    Task<GatewayBroadcast> BroadcastAsync(byte[] rawTransaction, CancellationToken cancellationToken = default);
}

/// <summary>
/// The SPV gateway as a blockchain source (spec FR-009, T075). Reads come from the wallet own record, which is refreshed through the
/// gateway first and rebuilt only from transactions whose proofs the wallet checked against its own headers. When the source cannot
/// answer a question correctly (no record yet, headers behind, a history row with an input it cannot name) it throws
/// <see cref="GatewayDeclinedException"/> so the next source is used and the gateway is not counted as failing; when it sends data that
/// fails the wallet checks it throws <see cref="GatewayFaultException"/>; any other failure is an ordinary read failure. It never
/// returns an empty list for a failure.
/// </summary>
public sealed class P2pGatewayBlockchainService : IBlockchainService
{
    public static readonly TimeSpan DefaultMinRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly GatewayHeaderSync _headerSync;
    private readonly GatewayIncrementalSync _incremental;
    private readonly WalletSyncStateStore _states;
    private readonly GatewayHeaderStore _headers;
    private readonly IGatewayWalletScopeProvider _scopes;
    private readonly IGatewayBroadcaster _broadcaster;
    private readonly Network _network;
    private readonly TimeProvider _time;
    private readonly TimeSpan _minRefresh;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _lastWallet;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private readonly GatewayTipCrossCheck? _crossCheck;
    private readonly GatewayProofUpgrader? _upgrader;
    private DateTimeOffset _lastCrossCheck = DateTimeOffset.MinValue;

    /// <summary>How often the header chain is compared with an independent explorer.</summary>
    public static readonly TimeSpan CrossCheckInterval = TimeSpan.FromMinutes(2);

    public P2pGatewayBlockchainService(
        GatewayHeaderSync headerSync, GatewayIncrementalSync incremental, WalletSyncStateStore states, GatewayHeaderStore headers,
        IGatewayWalletScopeProvider scopes, IGatewayBroadcaster broadcaster, Network? network = null, TimeProvider? time = null, TimeSpan? minRefreshInterval = null,
        GatewayTipCrossCheck? crossCheck = null, GatewayProofUpgrader? proofUpgrader = null)
    {
        _headerSync = headerSync;
        _incremental = incremental;
        _states = states;
        _headers = headers;
        _scopes = scopes;
        _broadcaster = broadcaster;
        _network = network ?? DigiByteNetwork.Mainnet;
        _time = time ?? TimeProvider.System;
        _minRefresh = minRefreshInterval ?? DefaultMinRefreshInterval;
        _crossCheck = crossCheck;
        _upgrader = proofUpgrader;
    }

    // ---- reads that come from the wallet record ----

    public async Task<long> GetBalanceAsync(string address) => await GetBalanceAsync([address]);

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses) =>
        (await GetUtxosAsync(addresses)).Sum(u => u.AmountSatoshis);

    public async Task<List<UtxoInfo>> GetUtxosAsync(string address) => await GetUtxosAsync([address]);

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var wanted = addresses.ToList();
        var (scope, state) = await FreshAsync();
        RequireInScope(scope, wanted);
        var scripts = wanted.Select(a => BitcoinAddress.Create(a, _network).ScriptPubKey.ToHex()).ToHashSet();
        var tip = _headers.TipHeight;
        var transactions = state.Transactions.ToDictionary(t => t.Txid, t => t);

        var result = new List<UtxoInfo>();
        foreach (var output in state.UnspentOutpoints.Where(o => scripts.Contains(o.ScriptPubKey)))
        {
            if (!transactions.TryGetValue(output.Txid, out var tx))
                continue;
            var facts = FactsOf(tx, tip);
            if (!GatewaySpendRules.IsSpendable(new OutputFacts(facts, output.IsChangeToSelf), GatewayCapabilities.WalletPowVerified))
                continue;
            result.Add(new UtxoInfo { TxId = output.Txid, OutputIndex = (uint)output.Vout, AmountSatoshis = output.Amount, ScriptPubKey = output.ScriptPubKey, Confirmations = facts.Confirmations });
        }
        return result;
    }

    public Task<List<UtxoInfo>> GetVerifiedUtxosAsync(string address) => GetUtxosAsync(address);

    public async Task<int> GetBlockHeightAsync()
    {
        await SyncHeadersAsync();
        return _headers.TipHeight;
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        var (_, state) = await FreshAsync();
        var tx = state.Transactions.FirstOrDefault(t => t.Txid.Equals(txId, StringComparison.OrdinalIgnoreCase));
        return tx is null ? null : Describe(tx, state);
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        var (scope, state) = await FreshAsync();
        RequireInScope(scope, [address]);
        var script = BitcoinAddress.Create(address, _network).ScriptPubKey;

        var related = new List<(SyncTransaction Tx, TransactionInfo Info)>();
        foreach (var tx in state.Transactions)
        {
            if (string.IsNullOrEmpty(tx.Raw))
                throw new GatewaySyncUnavailableException(SyncUnavailableReason.CannotAnswer);
            var parsed = Transaction.Parse(tx.Raw, _network);
            var pays = parsed.Outputs.Any(o => o.ScriptPubKey == script);
            var info = Describe(tx, state); // throws CannotAnswer when an input cannot be named
            var spends = info.Inputs.Any(i => i.Address == address);
            if (pays || spends)
                related.Add((tx, info));
        }
        return related
            .OrderBy(r => r.Tx.Height == 0 ? int.MinValue : -r.Tx.Height)
            .Select(r => r.Info)
            .Skip(skip).Take(take).ToList();
    }

    // ---- writes and things the gateway does not own ----

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var result = await _broadcaster.BroadcastAsync(rawTransaction);
        if (result.Outcome == "rejected")
            throw new InvalidOperationException("The network rejected the transaction: " + string.Join("; ", result.Rejections));
        // "accepted" is sent; "no_response" is sent but not yet confirmed by peers, and the wallet keeps watching.
        return result.Txid;
    }

    public Task<decimal> GetFeeRateAsync() => throw new GatewayDeclinedException("The gateway fee estimate is advisory; the existing fee source stays authoritative.");

    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new GatewayDeclinedException("The gateway does not provide prices.");

    // ---- plumbing ----

    private async Task SyncHeadersAsync()
    {
        await _headers.EnsureInitializedAsync();
        try
        {
            await _headerSync.SyncAsync();
        }
        catch (GatewayHeadersRejectedException e)
        {
            throw new GatewayFaultException(e.Message);
        }

        // The headers pass the wallet own checks; an independent explorer is asked whether it sees the same chain. No answer neither
        // promotes nor demotes; a different block at a settled height means the gateway chain is suspect.
        if (_crossCheck is not null && _time.GetUtcNow() - _lastCrossCheck >= CrossCheckInterval)
        {
            _lastCrossCheck = _time.GetUtcNow();
            if (await _crossCheck.CheckAsync() == CrossCheckResult.Mismatch)
            {
                _lastCrossCheck = DateTimeOffset.MinValue; // ask again at the next read: the source stays faulty until it agrees
                throw new GatewayFaultException("An independent explorer does not see the same chain as the gateway.");
            }
        }
    }

    /// <summary>Brings headers and the wallet record up to date (at most once per interval, shared by concurrent callers) and returns the record.</summary>
    private async Task<(GatewayWalletScope Scope, WalletSyncState State)> FreshAsync()
    {
        var scope = await _scopes.GetScopeAsync() ?? throw new GatewaySyncUnavailableException(SyncUnavailableReason.NoState);
        await _gate.WaitAsync();
        try
        {
            var now = _time.GetUtcNow();
            if (_lastWallet != scope.WalletId || now - _lastRefresh >= _minRefresh)
            {
                await SyncHeadersAsync();
                await _incremental.RefreshAsync(scope.WalletId, scope.Addresses, scope.ChangeAddresses);
                _lastWallet = scope.WalletId;
                _lastRefresh = now;
                StartUpgrade(scope.WalletId);
            }
        }
        finally
        {
            _gate.Release();
        }
        var state = await _states.GetAsync(scope.WalletId) ?? throw new GatewaySyncUnavailableException(SyncUnavailableReason.NoState);
        return (scope, state);
    }

    /// <summary>
    /// The gateway is answering, so proof requests for explorer-sourced transactions may go out. They run in the background, rate-limited
    /// by the upgrader, and never hold up or fail a read: a failure here is simply tried again at a later refresh.
    /// </summary>
    private void StartUpgrade(string walletId)
    {
        if (_upgrader is null)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _upgrader.UpgradeAsync(walletId);
            }
            catch (Exception)
            {
                // best effort
            }
        });
    }

    private static void RequireInScope(GatewayWalletScope scope, IReadOnlyCollection<string> addresses)
    {
        // The record covers the wallet addresses only; an address outside them cannot be answered correctly from it.
        if (addresses.Any(a => !scope.Addresses.Contains(a)))
            throw new GatewaySyncUnavailableException(SyncUnavailableReason.CannotAnswer);
    }

    private static TransactionFacts FactsOf(SyncTransaction tx, int tip) => new(
        tx.IsConfirmed ? Math.Max(0, tip - tx.Height + 1) : 0, tx.IsOwnSend,
        ProofChecked: tx.Origin == SyncOrigin.GatewayProven, ExplorerSourced: tx.Origin == SyncOrigin.ExplorerSourced, Rejected: false, Disputed: tx.Disputed);

    private TransactionInfo Describe(SyncTransaction tx, WalletSyncState state)
    {
        if (string.IsNullOrEmpty(tx.Raw))
            throw new GatewaySyncUnavailableException(SyncUnavailableReason.CannotAnswer);
        var parsed = Transaction.Parse(tx.Raw, _network);

        var inputs = new List<TxInput>();
        long totalIn = 0;
        if (!parsed.IsCoinBase)
        {
            foreach (var input in parsed.Inputs)
            {
                var previous = Resolve(input.PrevOut, state) ?? throw new GatewaySyncUnavailableException(SyncUnavailableReason.CannotAnswer);
                totalIn += previous.Value.Satoshi;
                inputs.Add(new TxInput { Address = previous.ScriptPubKey.GetDestinationAddress(_network)?.ToString() ?? string.Empty, AmountSatoshis = previous.Value.Satoshi });
            }
        }
        var outputs = parsed.Outputs.Select((o, n) => new TxOutput
        {
            Address = o.ScriptPubKey.GetDestinationAddress(_network)?.ToString() ?? string.Empty,
            AmountSatoshis = o.Value.Satoshi,
            Index = (uint)n,
            ScriptHex = o.ScriptPubKey.ToHex(),
        }).ToList();
        var totalOut = outputs.Sum(o => o.AmountSatoshis);

        var tip = _headers.TipHeight;
        var timestamp = tx.IsConfirmed && _headers.HeaderAt(tx.Height) is { } header
            ? DateTimeOffset.FromUnixTimeSeconds(header.Time).UtcDateTime
            : _time.GetUtcNow().UtcDateTime;
        return new TransactionInfo
        {
            TxId = tx.Txid,
            Confirmations = tx.IsConfirmed ? Math.Max(0, tip - tx.Height + 1) : 0,
            Timestamp = timestamp,
            FeeSatoshis = parsed.IsCoinBase ? 0 : Math.Max(0, totalIn - totalOut),
            Inputs = inputs,
            Outputs = outputs,
        };
    }

    /// <summary>The output an input spends: from a transaction the record holds, or from the outputs it remembers as spent or unspent.</summary>
    private TxOut? Resolve(OutPoint point, WalletSyncState state)
    {
        var id = point.Hash.ToString();
        var holder = state.Transactions.FirstOrDefault(t => t.Txid == id && !string.IsNullOrEmpty(t.Raw));
        if (holder is not null)
        {
            var previous = Transaction.Parse(holder.Raw!, _network);
            return point.N < previous.Outputs.Count ? previous.Outputs[(int)point.N] : null;
        }
        var key = $"{id}:{point.N}";
        var remembered = state.UnspentOutpoints.Concat(state.SpentOutpoints ?? []).FirstOrDefault(o => o.Key == key);
        return remembered is null ? null : new TxOut(Money.Satoshis(remembered.Amount), Script.FromHex(remembered.ScriptPubKey));
    }
}
