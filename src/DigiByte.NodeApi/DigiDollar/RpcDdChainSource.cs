using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary><see cref="IDdChainSource"/> over the node's JSON-RPC (txindex=1 is required for confirmed lookups).</summary>
public sealed class RpcDdChainSource : IDdChainSource
{
    private const int RpcMethodNotFound = -5; // "No such mempool or blockchain transaction"

    private readonly DigiByteRpcClient _rpc;

    public RpcDdChainSource(DigiByteRpcClient rpc) => _rpc = rpc;

    public Task<int> GetTipHeightAsync(CancellationToken ct) => _rpc.CallAsync<int>("getblockcount");

    public async Task<int?> GetNetworkHeightAsync(CancellationToken ct)
    {
        try
        {
            var info = await _rpc.CallAsync<JsonElement>("getblockchaininfo");
            return info.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : null;
        }
        catch (RpcException)
        {
            return null;
        }
    }

    public async Task<string?> GetBlockHashAsync(int height, CancellationToken ct)
    {
        try
        {
            return await _rpc.CallAsync<string>("getblockhash", height);
        }
        catch (RpcException ex) when (ex.Code == -8)
        {
            return null; // block height out of range
        }
    }

    public async Task<ChainBlock> GetBlockAsync(int height, CancellationToken ct)
    {
        var hash = await _rpc.CallAsync<string>("getblockhash", height);
        var block = await _rpc.CallAsync<JsonElement>("getblock", hash, 2);
        var time = block.TryGetProperty("time", out var t) ? t.GetInt64() : 0;

        var txs = new List<ChainTx>();
        foreach (var tx in block.GetProperty("tx").EnumerateArray())
        {
            var version = unchecked((uint)tx.GetProperty("version").GetInt32());
            long? fee = null;
            if (tx.TryGetProperty("fee", out var f) && f.ValueKind == JsonValueKind.Number)
                fee = (long)Math.Round(f.GetDecimal() * 100_000_000m);
            txs.Add(new ChainTx(tx.GetProperty("txid").GetString()!, version, tx.GetProperty("hex").GetString()!, fee));
        }
        return new ChainBlock(height, hash, time, txs);
    }

    public async Task<IReadOnlyList<string>> GetMempoolTxidsAsync(CancellationToken ct) =>
        await _rpc.CallAsync<string[]>("getrawmempool", false);

    public async Task<string?> GetRawTransactionHexAsync(string txid, CancellationToken ct)
    {
        try
        {
            return await _rpc.CallAsync<string>("getrawtransaction", txid, false);
        }
        catch (RpcException ex) when (ex.Code == RpcMethodNotFound)
        {
            return null;
        }
    }

    public async Task<ChainTxStatus> GetTxStatusAsync(string txid, CancellationToken ct)
    {
        try
        {
            var tx = await _rpc.CallAsync<JsonElement>("getrawtransaction", txid, true);
            var confirmations = tx.TryGetProperty("confirmations", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            return confirmations > 0
                ? new ChainTxStatus(ChainTxState.Confirmed, confirmations)
                : new ChainTxStatus(ChainTxState.Mempool, 0);
        }
        catch (RpcException ex) when (ex.Code == RpcMethodNotFound)
        {
            return new ChainTxStatus(ChainTxState.Unknown, 0);
        }
    }
}
