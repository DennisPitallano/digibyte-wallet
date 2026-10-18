using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary>
/// In-memory view of unconfirmed DigiDollar transactions (never persisted). Refreshed from the node mempool on a
/// timer; transactions that left the mempool (mined or dropped) disappear on the next refresh.
/// </summary>
public sealed class DdMempoolOverlay
{
    private readonly Network _network;
    private readonly object _gate = new();
    private Dictionary<string, DdParsedTx> _txs = new();
    private readonly HashSet<string> _notDd = new();

    public DdMempoolOverlay(Network network) => _network = network;

    public async Task RefreshAsync(IDdChainSource source, CancellationToken ct)
    {
        var txids = await source.GetMempoolTxidsAsync(ct);
        var current = new HashSet<string>(txids);

        Dictionary<string, DdParsedTx> existing;
        lock (_gate)
        {
            existing = _txs;
            _notDd.IntersectWith(current); // forget non-DD txids that left the mempool
        }

        var next = new Dictionary<string, DdParsedTx>();
        foreach (var txid in txids)
        {
            if (existing.TryGetValue(txid, out var known)) { next[txid] = known; continue; }
            lock (_gate) { if (_notDd.Contains(txid)) continue; }

            var hex = await source.GetRawTransactionHexAsync(txid, ct);
            if (hex is null) continue; // raced with a block

            var tx = Transaction.Parse(hex, _network);
            var parsed = DigiDollarScripts.IsDigiDollarVersion(tx.Version) ? DdTxParser.Parse(tx) : null;
            if (parsed is null)
            {
                lock (_gate) { _notDd.Add(txid); }
                continue;
            }
            next[txid] = parsed;
        }

        lock (_gate) { _txs = next; }
    }

    public IReadOnlyCollection<DdParsedTx> Transactions
    {
        get { lock (_gate) { return _txs.Values.ToList(); } }
    }

    public DdParsedTx? Get(string txid)
    {
        lock (_gate) { return _txs.GetValueOrDefault(txid); }
    }

    /// <summary>Outpoints spent by mempool transactions, mapped to the spending txid.</summary>
    public IReadOnlyDictionary<DdOutpointRef, string> SpentOutpoints()
    {
        var map = new Dictionary<DdOutpointRef, string>();
        lock (_gate)
        {
            foreach (var tx in _txs.Values)
                foreach (var s in tx.Spends)
                    map[s] = tx.Txid;
        }
        return map;
    }

    /// <summary>Test seam and indexer hook: replace the overlay contents directly.</summary>
    public void Set(IEnumerable<DdParsedTx> txs)
    {
        lock (_gate) { _txs = txs.ToDictionary(t => t.Txid); }
    }
}
