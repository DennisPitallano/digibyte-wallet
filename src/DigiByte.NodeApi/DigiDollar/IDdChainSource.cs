namespace DigiByte.NodeApi.DigiDollar;

/// <summary>A transaction as delivered by <c>getblock</c> (verbosity 2).</summary>
public sealed record ChainTx(string Txid, uint Version, string Hex, long? FeeSats);

public sealed record ChainBlock(int Height, string Hash, long Time, IReadOnlyList<ChainTx> Txs);

public enum ChainTxState
{
    Unknown,
    Mempool,
    Confirmed,
}

public sealed record ChainTxStatus(ChainTxState State, int Confirmations);

/// <summary>
/// The slice of the node the DD index needs. Implemented over JSON-RPC in production
/// (<see cref="RpcDdChainSource"/>) and faked in tests.
/// </summary>
public interface IDdChainSource
{
    Task<int> GetTipHeightAsync(CancellationToken ct);

    /// <summary>Best-known chain height from peers (headers); null when unknown.</summary>
    Task<int?> GetNetworkHeightAsync(CancellationToken ct);

    /// <summary>Hash of the block at <paramref name="height"/>, or null when that height does not exist.</summary>
    Task<string?> GetBlockHashAsync(int height, CancellationToken ct);

    Task<ChainBlock> GetBlockAsync(int height, CancellationToken ct);

    Task<IReadOnlyList<string>> GetMempoolTxidsAsync(CancellationToken ct);

    /// <summary>Raw transaction hex from the mempool or txindex; null when not found.</summary>
    Task<string?> GetRawTransactionHexAsync(string txid, CancellationToken ct);

    /// <summary>Mempool/confirmed/unknown status straight from the node (does not depend on the index).</summary>
    Task<ChainTxStatus> GetTxStatusAsync(string txid, CancellationToken ct);
}
