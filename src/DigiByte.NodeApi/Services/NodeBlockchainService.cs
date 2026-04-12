using System.Text.Json;
using DigiByte.NodeApi.RpcClient;
using DigiByte.Wallet.Services;

namespace DigiByte.NodeApi.Services;

/// <summary>
/// Implements IBlockchainService by calling digibyted RPC directly.
/// Drop-in replacement for BlockchainApiService in the wallet app.
/// </summary>
public class NodeBlockchainService : IBlockchainService
{
    private readonly DigiByteRpcClient _rpc;

    public NodeBlockchainService(DigiByteRpcClient rpc)
    {
        _rpc = rpc;
    }

    public async Task<long> GetBalanceAsync(string address)
    {
        var result = await _rpc.CallAsync<JsonElement>("scantxoutset", "start",
            new object[] { new { desc = $"addr({address})" } });
        var amount = result.TryGetProperty("total_amount", out var a) ? a.GetDecimal() : 0m;
        return (long)(amount * 100_000_000m);
    }

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        var addrList = addresses.ToList();
        if (addrList.Count == 0) return 0;

        // Batch all addresses into a single scantxoutset call
        var descriptors = addrList.Select(a => (object)new { desc = $"addr({a})" }).ToArray();
        var result = await _rpc.CallAsync<JsonElement>("scantxoutset", "start", (object)descriptors);
        var amount = result.TryGetProperty("total_amount", out var a) ? a.GetDecimal() : 0m;
        return (long)(amount * 100_000_000m);
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        var currentHeight = await _rpc.CallAsync<int>("getblockcount");
        var result = await _rpc.CallAsync<JsonElement>("scantxoutset", "start",
            new object[] { new { desc = $"addr({address})" } });

        return ParseUtxos(result, currentHeight);
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var addrList = addresses.ToList();
        if (addrList.Count == 0) return [];

        var currentHeight = await _rpc.CallAsync<int>("getblockcount");
        var descriptors = addrList.Select(a => (object)new { desc = $"addr({a})" }).ToArray();
        var result = await _rpc.CallAsync<JsonElement>("scantxoutset", "start", (object)descriptors);

        return ParseUtxos(result, currentHeight);
    }

    private static List<UtxoInfo> ParseUtxos(JsonElement result, int currentHeight)
    {
        var utxos = new List<UtxoInfo>();
        if (result.TryGetProperty("unspents", out var unspents))
        {
            foreach (var u in unspents.EnumerateArray())
            {
                var height = u.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                utxos.Add(new UtxoInfo
                {
                    TxId = u.GetProperty("txid").GetString()!,
                    OutputIndex = (uint)u.GetProperty("vout").GetInt32(),
                    AmountSatoshis = (long)(u.GetProperty("amount").GetDecimal() * 100_000_000m),
                    ScriptPubKey = u.TryGetProperty("scriptPubKey", out var s) ? s.GetString()! : "",
                    Confirmations = height > 0 ? currentHeight - height + 1 : 0,
                });
            }
        }
        return utxos;
    }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var hex = Convert.ToHexString(rawTransaction).ToLower();
        return await _rpc.CallAsync<string>("sendrawtransaction", hex);
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        var tx = await _rpc.CallAsync<JsonElement>("getrawtransaction", txId, true);
        return ParseTransaction(tx);
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        // Note: requires -txindex on the node. Falls back to empty if not available.
        try
        {
            // Use wallet's listtransactions if address is in wallet
            var txs = await _rpc.CallAsync<JsonElement>("listtransactions", "*", take, skip);
            var results = new List<TransactionInfo>();
            foreach (var tx in txs.EnumerateArray())
            {
                if (tx.TryGetProperty("txid", out var txid))
                {
                    var full = await GetTransactionAsync(txid.GetString()!);
                    if (full != null) results.Add(full);
                }
            }
            return results;
        }
        catch
        {
            return [];
        }
    }

    public async Task<decimal> GetFeeRateAsync()
    {
        var result = await _rpc.CallAsync<JsonElement>("estimatesmartfee", 6);
        if (result.TryGetProperty("feerate", out var fee))
            return fee.GetDecimal();
        return 0.00001m; // fallback: 1 sat/vB
    }

    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        // RPC doesn't provide price data — return 0, let caller use CoinGecko
        return Task.FromResult(0m);
    }

    public async Task<int> GetBlockHeightAsync()
    {
        return await _rpc.CallAsync<int>("getblockcount");
    }

    private static TransactionInfo? ParseTransaction(JsonElement tx)
    {
        if (tx.ValueKind == JsonValueKind.Undefined || tx.ValueKind == JsonValueKind.Null)
            return null;

        var inputs = new List<TxInput>();
        var outputs = new List<TxOutput>();

        if (tx.TryGetProperty("vin", out var vin))
        {
            foreach (var v in vin.EnumerateArray())
            {
                inputs.Add(new TxInput
                {
                    Address = v.TryGetProperty("address", out var a) ? a.GetString()! : "coinbase",
                    AmountSatoshis = v.TryGetProperty("value", out var val) ? (long)(val.GetDecimal() * 100_000_000m) : 0,
                });
            }
        }

        if (tx.TryGetProperty("vout", out var vout))
        {
            uint idx = 0;
            foreach (var v in vout.EnumerateArray())
            {
                var addr = "unknown";
                if (v.TryGetProperty("scriptPubKey", out var spk) && spk.TryGetProperty("address", out var a))
                    addr = a.GetString()!;
                else if (v.TryGetProperty("scriptPubKey", out var spk2) && spk2.TryGetProperty("addresses", out var addrs))
                    addr = addrs[0].GetString()!;

                outputs.Add(new TxOutput
                {
                    Address = addr,
                    AmountSatoshis = (long)(v.GetProperty("value").GetDecimal() * 100_000_000m),
                    Index = idx++,
                });
            }
        }

        long blockTime = 0;
        if (tx.TryGetProperty("blocktime", out var bt)) blockTime = bt.GetInt64();
        else if (tx.TryGetProperty("time", out var t)) blockTime = t.GetInt64();

        return new TransactionInfo
        {
            TxId = tx.GetProperty("txid").GetString()!,
            Confirmations = tx.TryGetProperty("confirmations", out var c) ? c.GetInt32() : 0,
            Timestamp = blockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(blockTime).UtcDateTime : DateTime.UtcNow,
            FeeSatoshis = tx.TryGetProperty("fee", out var feeVal) ? (long)(Math.Abs(feeVal.GetDecimal()) * 100_000_000m) : 0,
            Inputs = inputs,
            Outputs = outputs,
        };
    }
}
