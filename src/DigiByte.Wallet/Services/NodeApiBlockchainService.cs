using System.Net.Http.Json;
using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>
/// IBlockchainService implementation that calls our own DigiByte Node API
/// (DigiByte.NodeApi running on localhost:5260 or Docker).
/// </summary>
public class NodeApiBlockchainService : IBlockchainService
{
    private readonly HttpClient _http;
    private string _baseUrl;

    public NodeApiBlockchainService(HttpClient http, string baseUrl = "http://localhost:5260")
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<long> GetBalanceAsync(string address)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/address/{address}/balance");
            return result.TryGetProperty("balanceSatoshis", out var sat) ? sat.GetInt64() : 0;
        }
        catch { return 0; }
    }

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        long total = 0;
        foreach (var addr in addresses)
            total += await GetBalanceAsync(addr);
        return total;
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/address/{address}/utxos");
            var list = new List<UtxoInfo>();
            if (result.TryGetProperty("utxos", out var utxos))
            {
                foreach (var u in utxos.EnumerateArray())
                {
                    list.Add(new UtxoInfo
                    {
                        TxId = u.GetProperty("txid").GetString()!,
                        OutputIndex = (uint)u.GetProperty("vout").GetInt32(),
                        AmountSatoshis = u.GetProperty("amountSatoshis").GetInt64(),
                        ScriptPubKey = u.TryGetProperty("scriptPubKey", out var s) ? s.GetString()! : "",
                        Confirmations = u.TryGetProperty("height", out var h) ? 1 : 0,
                    });
                }
            }
            return list;
        }
        catch { return []; }
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var all = new List<UtxoInfo>();
        foreach (var addr in addresses)
            all.AddRange(await GetUtxosAsync(addr));
        return all;
    }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var hex = Convert.ToHexString(rawTransaction).ToLower();
        Console.WriteLine($"[NodeApi] Broadcasting to {_baseUrl}/api/tx/broadcast — hex length: {hex.Length}");
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/tx/broadcast", new { hex });
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[NodeApi] Response: {(int)response.StatusCode} {response.StatusCode} — {body[..Math.Min(200, body.Length)]}");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Broadcast rejected: {body}");

        var result = JsonSerializer.Deserialize<JsonElement>(body);
        if (result.TryGetProperty("txid", out var txid))
        {
            Console.WriteLine($"[NodeApi] ✓ Broadcast success — txid: {txid.GetString()}");
            return txid.GetString()!;
        }
        if (result.TryGetProperty("error", out var error))
            throw new Exception($"Broadcast error: {error}");

        throw new Exception($"Unexpected broadcast response: {body}");
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        try
        {
            var tx = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/tx/{txId}");
            return ParseTransaction(tx);
        }
        catch { return null; }
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        // Get UTXOs for this address and fetch each UTXO's full transaction
        try
        {
            var utxos = await GetUtxosAsync(address);
            var results = new List<TransactionInfo>();
            var seen = new HashSet<string>();

            foreach (var utxo in utxos)
            {
                if (!seen.Add(utxo.TxId)) continue; // skip duplicate txids

                var txInfo = await GetTransactionAsync(utxo.TxId);
                if (txInfo != null)
                    results.Add(txInfo);

                if (results.Count >= take) break;
            }

            return results.Skip(skip).Take(take).ToList();
        }
        catch { return []; }
    }

    public async Task<decimal> GetFeeRateAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/network/fee/6");
            return result.TryGetProperty("feerate", out var fee) ? fee.GetDecimal() : 0.00001m;
        }
        catch { return 0.00001m; }
    }

    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        // Node API doesn't provide price — use CoinGecko directly
        var data = await _http.GetFromJsonAsync<JsonElement>(
            $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={fiatCurrency.ToLower()}");
        if (data.TryGetProperty("digibyte", out var dgb) &&
            dgb.TryGetProperty(fiatCurrency.ToLower(), out var price))
        {
            var result = price.GetDecimal();
            if (result > 0) return result;
        }
        throw new InvalidOperationException("CoinGecko returned no price data");
    }

    public async Task<int> GetBlockHeightAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/blockchain/height");
            return result.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
        }
        catch { return 0; }
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
                var addr = "coinbase";
                if (v.TryGetProperty("prevout", out var prevout) && prevout.TryGetProperty("scriptPubKey", out var spk))
                {
                    if (spk.TryGetProperty("address", out var a)) addr = a.GetString()!;
                }
                inputs.Add(new TxInput { Address = addr, AmountSatoshis = 0 });
            }
        }

        if (tx.TryGetProperty("vout", out var vout))
        {
            uint idx = 0;
            foreach (var v in vout.EnumerateArray())
            {
                var addr = "unknown";
                if (v.TryGetProperty("scriptPubKey", out var spk))
                {
                    if (spk.TryGetProperty("address", out var a)) addr = a.GetString()!;
                    else if (spk.TryGetProperty("addresses", out var addrs) && addrs.GetArrayLength() > 0)
                        addr = addrs[0].GetString()!;
                }
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
            FeeSatoshis = 0,
            Inputs = inputs,
            Outputs = outputs,
        };
    }
}
