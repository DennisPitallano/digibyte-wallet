using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigiByte.Wallet.Services;

/// <summary>
/// IBlockchainService implementation using the Blockbook REST API.
/// Blockbook returns scriptPubKey in UTXO responses, enabling correct tx signing.
/// Compatible with: digibyteblockexplorer.com, any Blockbook instance.
/// </summary>
public class BlockbookApiService : IBlockchainService
{
    private readonly HttpClient _http;
    private string _baseUrl;

    public string Name { get; }

    public BlockbookApiService(HttpClient http, string baseUrl, string name = "blockbook")
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        Name = name;
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<long> GetBalanceAsync(string address)
    {
        var data = await _http.GetFromJsonAsync<BlockbookAddress>(
            $"{_baseUrl}/api/v2/address/{address}?details=basic");
        if (data == null) return 0;
        return long.TryParse(data.Balance, out var bal) ? bal : 0;
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
        var utxos = await _http.GetFromJsonAsync<List<BlockbookUtxo>>(
            $"{_baseUrl}/api/v2/utxo/{address}?confirmed=true");
        return utxos?.Select(u => new UtxoInfo
        {
            TxId = u.TxId,
            OutputIndex = (uint)u.Vout,
            AmountSatoshis = long.TryParse(u.Value, out var v) ? v : 0,
            ScriptPubKey = u.ScriptPubKey ?? "",
            Confirmations = u.Confirmations,
        }).ToList() ?? [];
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
        var response = await _http.GetFromJsonAsync<BlockbookSendResult>(
            $"{_baseUrl}/api/v2/sendtx/{hex}");

        if (response?.Result != null)
            return response.Result;

        throw new Exception($"Blockbook broadcast failed: {response?.Error ?? "unknown error"}");
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        try
        {
            var tx = await _http.GetFromJsonAsync<BlockbookTx>($"{_baseUrl}/api/v2/tx/{txId}");
            if (tx == null) return null;

            return new TransactionInfo
            {
                TxId = tx.TxId,
                Confirmations = tx.Confirmations,
                Timestamp = tx.BlockTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(tx.BlockTime).UtcDateTime
                    : DateTime.UtcNow,
                FeeSatoshis = long.TryParse(tx.Fees, out var f) ? f : 0,
                Inputs = tx.Vin?.Select(v => new TxInput
                {
                    Address = v.Addresses?.FirstOrDefault() ?? "unknown",
                    AmountSatoshis = long.TryParse(v.Value, out var val) ? val : 0,
                }).ToList() ?? [],
                Outputs = tx.Vout?.Select(v => new TxOutput
                {
                    Address = v.Addresses?.FirstOrDefault() ?? "unknown",
                    AmountSatoshis = long.TryParse(v.Value, out var val) ? val : 0,
                    Index = (uint)v.N,
                }).ToList() ?? [],
            };
        }
        catch { return null; }
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        try
        {
            var data = await _http.GetFromJsonAsync<BlockbookAddress>(
                $"{_baseUrl}/api/v2/address/{address}?details=txs&page=1&pageSize={take}");
            if (data?.Transactions == null) return [];

            return data.Transactions.Skip(skip).Select(tx => new TransactionInfo
            {
                TxId = tx.TxId,
                Confirmations = tx.Confirmations,
                Timestamp = tx.BlockTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(tx.BlockTime).UtcDateTime
                    : DateTime.UtcNow,
                FeeSatoshis = long.TryParse(tx.Fees, out var f) ? f : 0,
                Inputs = tx.Vin?.Select(v => new TxInput
                {
                    Address = v.Addresses?.FirstOrDefault() ?? "unknown",
                    AmountSatoshis = long.TryParse(v.Value, out var val) ? val : 0,
                }).ToList() ?? [],
                Outputs = tx.Vout?.Select(v => new TxOutput
                {
                    Address = v.Addresses?.FirstOrDefault() ?? "unknown",
                    AmountSatoshis = long.TryParse(v.Value, out var val) ? val : 0,
                    Index = (uint)v.N,
                }).ToList() ?? [],
            }).ToList();
        }
        catch { return []; }
    }

    public async Task<decimal> GetFeeRateAsync()
    {
        try
        {
            var data = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/v2/estimatefee/6");
            if (data.TryGetProperty("result", out var result))
                return result.GetDecimal();
        }
        catch { }
        return 0.00001m; // DGB default min fee
    }

    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        try
        {
            var data = await _http.GetFromJsonAsync<JsonElement>(
                $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={fiatCurrency.ToLower()}");
            if (data.TryGetProperty("digibyte", out var dgb) &&
                dgb.TryGetProperty(fiatCurrency.ToLower(), out var price))
                return price.GetDecimal();
        }
        catch { }
        return 0;
    }

    public async Task<int> GetBlockHeightAsync()
    {
        try
        {
            var data = await _http.GetFromJsonAsync<BlockbookStatus>($"{_baseUrl}/api/v2/api");
            return data?.Blockbook?.BestHeight ?? data?.Backend?.Blocks ?? 0;
        }
        catch { return 0; }
    }

    // ---- Blockbook API response DTOs ----

    private class BlockbookStatus
    {
        [JsonPropertyName("blockbook")]
        public BlockbookInfo? Blockbook { get; set; }

        [JsonPropertyName("backend")]
        public BackendInfo? Backend { get; set; }
    }

    private class BlockbookInfo
    {
        [JsonPropertyName("bestHeight")]
        public int BestHeight { get; set; }
    }

    private class BackendInfo
    {
        [JsonPropertyName("blocks")]
        public int Blocks { get; set; }
    }

    private class BlockbookAddress
    {
        [JsonPropertyName("balance")]
        public string Balance { get; set; } = "0";

        [JsonPropertyName("transactions")]
        public List<BlockbookTx>? Transactions { get; set; }
    }

    private class BlockbookUtxo
    {
        [JsonPropertyName("txid")]
        public string TxId { get; set; } = "";

        [JsonPropertyName("vout")]
        public int Vout { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; } = "0";

        [JsonPropertyName("confirmations")]
        public int Confirmations { get; set; }

        [JsonPropertyName("scriptPubKey")]
        public string? ScriptPubKey { get; set; }
    }

    private class BlockbookTx
    {
        [JsonPropertyName("txid")]
        public string TxId { get; set; } = "";

        [JsonPropertyName("confirmations")]
        public int Confirmations { get; set; }

        [JsonPropertyName("blockTime")]
        public long BlockTime { get; set; }

        [JsonPropertyName("fees")]
        public string Fees { get; set; } = "0";

        [JsonPropertyName("vin")]
        public List<BlockbookVin>? Vin { get; set; }

        [JsonPropertyName("vout")]
        public List<BlockbookVout>? Vout { get; set; }
    }

    private class BlockbookVin
    {
        [JsonPropertyName("addresses")]
        public List<string>? Addresses { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; } = "0";
    }

    private class BlockbookVout
    {
        [JsonPropertyName("n")]
        public int N { get; set; }

        [JsonPropertyName("addresses")]
        public List<string>? Addresses { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; } = "0";
    }

    private class BlockbookSendResult
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
