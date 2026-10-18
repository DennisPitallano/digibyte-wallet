namespace DigiByte.NodeApi.DigiDollar;

/// <summary>Snapshot of how far the index has caught up; written by the indexer loop, read by the endpoints.</summary>
public sealed class DdIndexStatus
{
    private int _tip = -1;
    private int _networkHeight = -1;
    private int _lastIndexed = -1;

    public int? TipHeight => _tip < 0 ? null : _tip;
    public int? NetworkHeight => _networkHeight < 0 ? null : _networkHeight;
    public int? LastIndexedHeight => _lastIndexed < 0 ? null : _lastIndexed;

    /// <summary>Blocks the index is behind the node tip; null until the first pass completes.</summary>
    public int? LagBlocks => TipHeight is { } t && LastIndexedHeight is { } l ? Math.Max(0, t - l) : null;

    public void Update(int tip, int? networkHeight, int? lastIndexed)
    {
        Volatile.Write(ref _tip, tip);
        Volatile.Write(ref _networkHeight, networkHeight ?? -1);
        Volatile.Write(ref _lastIndexed, lastIndexed ?? -1);
    }
}

/// <summary>One unspent DD output as served to the wallet.</summary>
public sealed record DdUtxo(
    string Txid,
    int Vout,
    byte[] OutputKey,
    long AmountCents,
    string ScriptPubKeyHex,
    long ValueSats,
    int Confirmations,
    bool SpentByMempool);

public sealed record DdHistoryAmount(byte[] OutputKey, long AmountCents);

public sealed record DdHistoryItem(
    string Txid,
    int? BlockHeight,
    long? BlockTime,
    int Confirmations,
    long? FeeSats,
    IReadOnlyList<DdHistoryAmount> DdInputs,
    IReadOnlyList<DdHistoryAmount> DdOutputs);

public sealed record DdHistoryPage(IReadOnlyList<DdHistoryItem> Items, string? Next);

/// <summary>The read surface the wallet-facing endpoints use.</summary>
public interface IDdIndex
{
    Task<IReadOnlyList<DdUtxo>> GetUtxosAsync(IReadOnlyCollection<byte[]> outputKeys, int minConf, CancellationToken ct);

    Task<DdHistoryPage> GetHistoryAsync(IReadOnlyCollection<byte[]> outputKeys, int count, string? before, CancellationToken ct);

    Task<ChainTxStatus> GetTxStatusAsync(string txid, CancellationToken ct);
}

/// <summary>Confirmed rows (SQLite) merged with the in-memory mempool overlay.</summary>
public sealed class DdIndex : IDdIndex
{
    private readonly DdIndexStore _store;
    private readonly DdMempoolOverlay _mempool;
    private readonly DdIndexStatus _status;
    private readonly IDdChainSource _source;

    public DdIndex(DdIndexStore store, DdMempoolOverlay mempool, DdIndexStatus status, IDdChainSource source)
    {
        _store = store;
        _mempool = mempool;
        _status = status;
        _source = source;
    }

    private int Confirmations(int blockHeight) =>
        _status.TipHeight is { } tip ? Math.Max(1, tip - blockHeight + 1) : 1;

    public Task<IReadOnlyList<DdUtxo>> GetUtxosAsync(IReadOnlyCollection<byte[]> outputKeys, int minConf, CancellationToken ct)
    {
        var keys = new HashSet<string>(outputKeys.Select(Convert.ToHexString));
        var mempoolSpends = _mempool.SpentOutpoints();
        var result = new List<DdUtxo>();

        foreach (var row in _store.GetOutputsByKeys(outputKeys, unspentOnly: true))
        {
            var conf = Confirmations(row.BlockHeight);
            if (conf < minConf) continue;
            var spent = mempoolSpends.ContainsKey(new DdOutpointRef(row.Txid, row.Vout));
            result.Add(new DdUtxo(row.Txid, row.Vout, row.OutputKey, row.AmountCents, row.ScriptPubKeyHex, row.ValueSats, conf, spent));
        }

        if (minConf <= 0)
        {
            foreach (var tx in _mempool.Transactions)
                foreach (var o in tx.Outputs.Where(o => keys.Contains(Convert.ToHexString(o.OutputKey))))
                {
                    if (mempoolSpends.ContainsKey(new DdOutpointRef(tx.Txid, o.Vout))) continue; // already consumed in the mempool
                    result.Add(new DdUtxo(tx.Txid, o.Vout, o.OutputKey, o.AmountCents, o.ScriptPubKeyHex, 0, 0, false));
                }
        }
        return Task.FromResult<IReadOnlyList<DdUtxo>>(result);
    }

    public Task<DdHistoryPage> GetHistoryAsync(IReadOnlyCollection<byte[]> outputKeys, int count, string? before, CancellationToken ct)
    {
        var keys = new HashSet<string>(outputKeys.Select(Convert.ToHexString));
        var items = new List<DdHistoryItem>();

        DdTxRow? beforeRow = before is null ? null : _store.GetTx(before);

        // Mempool transactions only on the first page (a cursor that is a mempool txid also restarts at the top of confirmed).
        if (beforeRow is null)
        {
            foreach (var tx in _mempool.Transactions)
            {
                var inputs = ResolveMempoolInputs(tx);
                var touches = tx.Outputs.Any(o => keys.Contains(Convert.ToHexString(o.OutputKey)))
                              || inputs.Any(i => keys.Contains(Convert.ToHexString(i.OutputKey)));
                if (!touches) continue;
                items.Add(new DdHistoryItem(tx.Txid, null, null, 0, null, inputs,
                    tx.Outputs.Select(o => new DdHistoryAmount(o.OutputKey, o.AmountCents)).ToList()));
            }
        }

        var remaining = Math.Max(0, count - items.Count);
        var confirmed = remaining == 0 ? [] : _store.GetTxsTouching(outputKeys, remaining, beforeRow);
        foreach (var row in confirmed)
        {
            items.Add(new DdHistoryItem(
                row.Txid, row.BlockHeight, row.BlockTime, Confirmations(row.BlockHeight), row.FeeSats,
                _store.GetOutputsSpentBy(row.Txid).Select(o => new DdHistoryAmount(o.OutputKey, o.AmountCents)).ToList(),
                _store.GetOutputsCreatedBy(row.Txid).Select(o => new DdHistoryAmount(o.OutputKey, o.AmountCents)).ToList()));
        }

        var next = items.Count >= count && confirmed.Count > 0 ? confirmed[^1].Txid : null;
        return Task.FromResult(new DdHistoryPage(items, next));
    }

    public async Task<ChainTxStatus> GetTxStatusAsync(string txid, CancellationToken ct)
    {
        if (_mempool.Get(txid) is not null) return new ChainTxStatus(ChainTxState.Mempool, 0);
        if (_store.GetTx(txid) is { } row) return new ChainTxStatus(ChainTxState.Confirmed, Confirmations(row.BlockHeight));
        // Not in the index (it may simply not be indexed yet): ask the node directly so a lagging index
        // never makes a valid transaction look dropped.
        return await _source.GetTxStatusAsync(txid, ct);
    }

    private List<DdHistoryAmount> ResolveMempoolInputs(DdParsedTx tx)
    {
        var inputs = new List<DdHistoryAmount>();
        foreach (var s in tx.Spends)
        {
            if (_store.GetOutput(s.Txid, s.Vout) is { } row)
                inputs.Add(new DdHistoryAmount(row.OutputKey, row.AmountCents));
            else if (_mempool.Get(s.Txid)?.Outputs.FirstOrDefault(o => o.Vout == s.Vout) is { } mo)
                inputs.Add(new DdHistoryAmount(mo.OutputKey, mo.AmountCents));
        }
        return inputs;
    }
}
