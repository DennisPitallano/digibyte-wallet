using NBitcoin;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary>
/// Walks the chain from the DD start height into the <see cref="DdIndexStore"/>: selects DigiDollar transactions
/// (nVersion low 16 bits = 0x0770), decodes their outputs, marks spends, and handles reorgs by rolling back to the
/// fork point (hash mismatch at the last indexed height).
/// </summary>
public sealed class DdIndexSync
{
    private readonly DdIndexStore _store;
    private readonly IDdChainSource _source;
    private readonly Network _network;
    private readonly int _startHeight;

    public DdIndexSync(DdIndexStore store, IDdChainSource source, Network network, int startHeight)
    {
        _store = store;
        _source = source;
        _network = network;
        _startHeight = startHeight;
    }

    /// <summary>Index up to <paramref name="maxBlocks"/> new blocks. Returns how many blocks were applied.</summary>
    public async Task<int> SyncAsync(int maxBlocks, CancellationToken ct)
    {
        await HandleReorgAsync(ct);

        var tip = await _source.GetTipHeightAsync(ct);
        var state = _store.GetState();
        var next = state.LastHeight is { } last ? last + 1 : _startHeight;
        var applied = 0;

        while (next <= tip && applied < maxBlocks && !ct.IsCancellationRequested)
        {
            var block = await _source.GetBlockAsync(next, ct);
            var txs = new List<(DdParsedTx Tx, long? FeeSats)>();
            foreach (var chainTx in block.Txs)
            {
                if (!Crypto.Transactions.DigiDollarScripts.IsDigiDollarVersion(chainTx.Version)) continue;
                var parsed = DdTxParser.Parse(Transaction.Parse(chainTx.Hex, _network));
                if (parsed is not null) txs.Add((parsed, chainTx.FeeSats));
            }
            _store.ApplyBlock(next, block.Hash, block.Time, txs);
            next++;
            applied++;
        }
        return applied;
    }

    /// <summary>
    /// If the node's hash at our last indexed height differs from the stored one, walk back to the newest height
    /// where they agree and roll the index back to it.
    /// </summary>
    private async Task HandleReorgAsync(CancellationToken ct)
    {
        var state = _store.GetState();
        if (state.LastHeight is not { } last) return;

        var nodeHash = await _source.GetBlockHashAsync(last, ct);
        if (nodeHash == state.LastHash) return;

        var h = last - 1;
        while (h >= _startHeight)
        {
            var stored = _store.GetBlockHash(h);
            if (stored is null) break; // beyond the retention window: restart from the start height
            if (stored == await _source.GetBlockHashAsync(h, ct))
            {
                _store.RollbackTo(h, stored);
                return;
            }
            h--;
        }
        _store.RollbackTo(_startHeight - 1, null);
    }
}
