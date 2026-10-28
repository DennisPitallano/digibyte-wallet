using System.Net;
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
    private readonly bool _strict;

    /// <param name="strictErrors">
    /// Off (the default): every method swallows errors and returns a neutral value (0, empty list, null, default fee, height 0),
    /// exactly as before. On: a failed request throws (and only a genuine "not found" on a transaction lookup returns null), so the
    /// fallback service can answer from an explorer and count the failure. Used when our own node is a primary source
    /// (specs/005-wallet-own-node-source, research R14).
    /// </param>
    public NodeApiBlockchainService(HttpClient http, string baseUrl = "http://localhost:5260", bool strictErrors = false)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _strict = strictErrors;
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<long> GetBalanceAsync(string address)
    {
        if (_strict)
            return StrictInt64(await StrictGetAsync($"/api/address/{address}/balance"), "balanceSatoshis");

        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/address/{address}/balance");
            return result.TryGetProperty("balanceSatoshis", out var sat) ? sat.GetInt64() : 0;
        }
        catch { return 0; }
    }

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        var addrList = addresses.ToList();
        if (addrList.Count == 0) return 0;

        if (_strict)
            return StrictInt64(await StrictPostAsync("/api/address/balances", addrList.ToArray()), "balanceSatoshis");

        try
        {
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/address/balances", addrList.ToArray());
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                return result.TryGetProperty("balanceSatoshis", out var sat) ? sat.GetInt64() : 0;
            }
        }
        catch { }

        // Fallback to sequential
        long total = 0;
        foreach (var addr in addrList)
            total += await GetBalanceAsync(addr);
        return total;
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        if (_strict)
            return StrictUtxos(await StrictGetAsync($"/api/address/{address}/utxos"));

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
                        Confirmations = u.TryGetProperty("confirmations", out var conf) ? conf.GetInt32()
                            : u.TryGetProperty("height", out var h) && h.GetInt32() > 0 ? 1 : 0,
                    });
                }
            }
            return list;
        }
        catch { return []; }
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var addrList = addresses.ToList();
        if (addrList.Count == 0) return [];

        if (_strict)
            return StrictUtxos(await StrictPostAsync("/api/address/utxos", addrList.ToArray()));

        try
        {
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/address/utxos", addrList.ToArray());
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
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
                            Confirmations = u.TryGetProperty("confirmations", out var conf) ? conf.GetInt32()
                                : u.TryGetProperty("height", out var h) && h.GetInt32() > 0 ? 1 : 0,
                        });
                    }
                }
                return list;
            }
        }
        catch { }

        // Fallback to sequential
        var all = new List<UtxoInfo>();
        foreach (var addr in addrList)
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
        if (_strict)
            return await StrictTransactionAsync(txId);

        try
        {
            var tx = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/tx/{txId}");
            return ParseTransaction(tx);
        }
        catch { return null; }
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        // The node only sees the transactions behind an address's unspent outputs, so it can never produce full history.
        // In strict mode (our own node as a primary source) history must come from an explorer, never from here.
        if (_strict)
            throw new NotSupportedException("The node cannot list full address history; use an explorer.");

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
        if (_strict)
        {
            var fee = await StrictGetAsync("/api/network/fee/6");
            if (fee.ValueKind == JsonValueKind.Object && fee.TryGetProperty("feerate", out var rate) &&
                rate.ValueKind == JsonValueKind.Number && rate.TryGetDecimal(out var value) && value > 0)
                return value;
            throw new InvalidOperationException("Node API fee answer is missing the fee rate");
        }

        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/network/fee/6");
            return result.TryGetProperty("feerate", out var fee) ? fee.GetDecimal() : 0.00001m;
        }
        catch { return 0.00001m; }
    }

    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        // Price is not a node API feature — let the fallback chain handle it via explorer backends
        throw new NotSupportedException("Node API does not provide price data");
    }

    public async Task<int> GetBlockHeightAsync()
    {
        if (_strict)
        {
            var height = await StrictGetAsync("/api/blockchain/height");
            if (height.ValueKind == JsonValueKind.Object && height.TryGetProperty("height", out var h) &&
                h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var value) && value > 0)
                return value;
            throw new InvalidOperationException("Node API height answer is missing a usable height");
        }

        try
        {
            var result = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/blockchain/height");
            return result.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
        }
        catch { return 0; }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Strict mode helpers: failures throw, nothing is turned into a neutral value.
    // ---------------------------------------------------------------------------------------------------------

    private async Task<JsonElement> StrictGetAsync(string path)
    {
        using var response = await _http.GetAsync($"{_baseUrl}{path}");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Node API {path} answered {(int)response.StatusCode}");
        return ParseStrictJson(body, path);
    }

    private async Task<JsonElement> StrictPostAsync(string path, object payload)
    {
        using var response = await _http.PostAsJsonAsync($"{_baseUrl}{path}", payload);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Node API {path} answered {(int)response.StatusCode}");
        return ParseStrictJson(body, path);
    }

    private static JsonElement ParseStrictJson(string body, string what)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Node API {what} returned an unreadable answer");
        }
    }

    private static long StrictInt64(JsonElement result, string property)
    {
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        throw new InvalidOperationException($"Node API answer is missing \"{property}\"");
    }

    private static List<UtxoInfo> StrictUtxos(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("utxos", out var utxos) ||
            utxos.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Node API answer is missing the spendable outputs");

        var list = new List<UtxoInfo>();
        foreach (var u in utxos.EnumerateArray())
        {
            list.Add(new UtxoInfo
            {
                TxId = u.GetProperty("txid").GetString() ?? throw new InvalidOperationException("Node API output has no txid"),
                OutputIndex = (uint)u.GetProperty("vout").GetInt32(),
                AmountSatoshis = u.GetProperty("amountSatoshis").GetInt64(),
                ScriptPubKey = u.TryGetProperty("scriptPubKey", out var s) ? s.GetString() ?? "" : "",
                Confirmations = u.TryGetProperty("confirmations", out var conf) ? conf.GetInt32()
                    : u.TryGetProperty("height", out var h) && h.GetInt32() > 0 ? 1 : 0,
            });
        }
        return list;
    }

    /// <summary>A genuine "not found" (HTTP 404, or the node's own no-such-transaction code -5) is null; everything else that is not a good answer throws.</summary>
    private async Task<TransactionInfo?> StrictTransactionAsync(string txId)
    {
        using var response = await _http.GetAsync($"{_baseUrl}/api/tx/{txId}");
        var body = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (response.StatusCode == HttpStatusCode.BadRequest && IsNoSuchTransaction(body))
            return null;

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Node API transaction lookup answered {(int)response.StatusCode}");

        var tx = ParseStrictJson(body, "transaction lookup");
        if (tx.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Node API transaction answer is not an object");
        return ParseTransaction(tx);
    }

    private static bool IsNoSuchTransaction(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("code", out var code) &&
                   code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var n) && n == -5;
        }
        catch (JsonException)
        {
            return false;
        }
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
                string? scriptHex = null;
                if (v.TryGetProperty("scriptPubKey", out var spk))
                {
                    if (spk.TryGetProperty("address", out var a)) addr = a.GetString()!;
                    else if (spk.TryGetProperty("addresses", out var addrs) && addrs.GetArrayLength() > 0)
                        addr = addrs[0].GetString()!;
                    if (spk.TryGetProperty("hex", out var h)) scriptHex = h.GetString();
                }
                outputs.Add(new TxOutput
                {
                    Address = addr,
                    AmountSatoshis = (long)(v.GetProperty("value").GetDecimal() * 100_000_000m),
                    Index = idx++,
                    ScriptHex = scriptHex,
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
