using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Connects to a DigiByte block explorer API (Esplora-compatible or chainz.cryptoid.info)
/// to fetch balances, UTXOs, and transaction history.
///
/// For testnet: uses digiexplorer.info/testnet or a configured endpoint.
/// For mainnet: uses digiexplorer.info or chainz.cryptoid.info.
/// </summary>
public class BlockchainApiService : IBlockchainService
{
    private readonly HttpClient _http;
    private string _baseUrl;
    private bool _isTestnet;

    // Public DigiByte explorer APIs
    private const string MainnetApi = "https://digiexplorer.info/api";
    private const string TestnetApi = "https://digiexplorer.info/testnet/api";

    // Fallback: chainz.cryptoid.info
    private const string FallbackMainnet = "https://chainz.cryptoid.info/dgb/api.dws";

    public BlockchainApiService(HttpClient http)
    {
        _http = http;
        _baseUrl = TestnetApi;
        _isTestnet = true;
    }

    public void SetNetwork(bool isTestnet)
    {
        _isTestnet = isTestnet;
        _baseUrl = isTestnet ? TestnetApi : MainnetApi;
    }

    public async Task<long> GetBalanceAsync(string address)
    {
        try
        {
            // Esplora API: GET /address/{address}
            var response = await _http.GetFromJsonAsync<EsploraAddress>($"{_baseUrl}/address/{address}");
            if (response == null) return 0;
            return response.ChainStats.FundedTxoSum - response.ChainStats.SpentTxoSum;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        long total = 0;
        foreach (var addr in addresses)
        {
            total += await GetBalanceAsync(addr);
        }
        return total;
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        try
        {
            // Esplora API: GET /address/{address}/utxo
            var utxos = await _http.GetFromJsonAsync<List<EsploraUtxo>>($"{_baseUrl}/address/{address}/utxo");
            return utxos?.Select(u => new UtxoInfo
            {
                TxId = u.TxId,
                OutputIndex = (uint)u.Vout,
                AmountSatoshis = u.Value,
                ScriptPubKey = "",
                Confirmations = u.Status?.Confirmed == true ? 1 : 0,
            }).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var allUtxos = new List<UtxoInfo>();
        foreach (var addr in addresses)
        {
            allUtxos.AddRange(await GetUtxosAsync(addr));
        }
        return allUtxos;
    }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var hex = Convert.ToHexString(rawTransaction).ToLower();
        var response = await _http.PostAsync($"{_baseUrl}/tx",
            new StringContent(hex, System.Text.Encoding.UTF8, "text/plain"));

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }

        var error = await response.Content.ReadAsStringAsync();
        throw new Exception($"Broadcast failed: {error}");
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        try
        {
            var tx = await _http.GetFromJsonAsync<EsploraTx>($"{_baseUrl}/tx/{txId}");
            if (tx == null) return null;

            return new TransactionInfo
            {
                TxId = tx.TxId,
                Confirmations = tx.Status?.Confirmed == true ? (tx.Status.BlockHeight > 0 ? 1 : 0) : 0,
                Timestamp = tx.Status?.BlockTime != null
                    ? DateTimeOffset.FromUnixTimeSeconds(tx.Status.BlockTime.Value).UtcDateTime
                    : DateTime.UtcNow,
                FeeSatoshis = tx.Fee,
                Inputs = tx.Vin?.Select(v => new TxInput
                {
                    Address = v.Prevout?.ScriptPubKeyAddress ?? "unknown",
                    AmountSatoshis = v.Prevout?.Value ?? 0,
                }).ToList() ?? [],
                Outputs = tx.Vout?.Select((v, i) => new TxOutput
                {
                    Address = v.ScriptPubKeyAddress ?? "unknown",
                    AmountSatoshis = v.Value,
                    Index = (uint)i,
                }).ToList() ?? [],
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        try
        {
            // Esplora API: GET /address/{address}/txs
            var txs = await _http.GetFromJsonAsync<List<EsploraTx>>($"{_baseUrl}/address/{address}/txs");
            if (txs == null) return [];

            return txs.Skip(skip).Take(take).Select(tx => new TransactionInfo
            {
                TxId = tx.TxId,
                Confirmations = tx.Status?.Confirmed == true ? 1 : 0,
                Timestamp = tx.Status?.BlockTime != null
                    ? DateTimeOffset.FromUnixTimeSeconds(tx.Status.BlockTime.Value).UtcDateTime
                    : DateTime.UtcNow,
                FeeSatoshis = tx.Fee,
                Inputs = tx.Vin?.Select(v => new TxInput
                {
                    Address = v.Prevout?.ScriptPubKeyAddress ?? "unknown",
                    AmountSatoshis = v.Prevout?.Value ?? 0,
                }).ToList() ?? [],
                Outputs = tx.Vout?.Select((v, i) => new TxOutput
                {
                    Address = v.ScriptPubKeyAddress ?? "unknown",
                    AmountSatoshis = v.Value,
                    Index = (uint)i,
                }).ToList() ?? [],
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<decimal> GetFeeRateAsync()
    {
        // DigiByte has very low fees — 1 sat/vB is usually enough
        return 1.0m;
    }

    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        try
        {
            // Use CoinGecko free API
            var data = await _http.GetFromJsonAsync<JsonElement>(
                $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={fiatCurrency.ToLower()}");
            if (data.TryGetProperty("digibyte", out var dgb) &&
                dgb.TryGetProperty(fiatCurrency.ToLower(), out var price))
            {
                return price.GetDecimal();
            }
        }
        catch { }
        return 0;
    }

    public async Task<int> GetBlockHeightAsync()
    {
        try
        {
            var height = await _http.GetStringAsync($"{_baseUrl}/blocks/tip/height");
            return int.TryParse(height, out var h) ? h : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ---- Esplora API response DTOs ----

    private class EsploraAddress
    {
        [JsonPropertyName("chain_stats")]
        public EsploraChainStats ChainStats { get; set; } = new();
    }

    private class EsploraChainStats
    {
        [JsonPropertyName("funded_txo_sum")]
        public long FundedTxoSum { get; set; }

        [JsonPropertyName("spent_txo_sum")]
        public long SpentTxoSum { get; set; }

        [JsonPropertyName("tx_count")]
        public int TxCount { get; set; }
    }

    private class EsploraUtxo
    {
        [JsonPropertyName("txid")]
        public string TxId { get; set; } = "";

        [JsonPropertyName("vout")]
        public int Vout { get; set; }

        [JsonPropertyName("value")]
        public long Value { get; set; }

        [JsonPropertyName("status")]
        public EsploraStatus? Status { get; set; }
    }

    private class EsploraTx
    {
        [JsonPropertyName("txid")]
        public string TxId { get; set; } = "";

        [JsonPropertyName("fee")]
        public long Fee { get; set; }

        [JsonPropertyName("status")]
        public EsploraStatus? Status { get; set; }

        [JsonPropertyName("vin")]
        public List<EsploraVin>? Vin { get; set; }

        [JsonPropertyName("vout")]
        public List<EsploraVout>? Vout { get; set; }
    }

    private class EsploraStatus
    {
        [JsonPropertyName("confirmed")]
        public bool Confirmed { get; set; }

        [JsonPropertyName("block_height")]
        public int BlockHeight { get; set; }

        [JsonPropertyName("block_time")]
        public long? BlockTime { get; set; }
    }

    private class EsploraVin
    {
        [JsonPropertyName("prevout")]
        public EsploraVout? Prevout { get; set; }
    }

    private class EsploraVout
    {
        [JsonPropertyName("value")]
        public long Value { get; set; }

        [JsonPropertyName("scriptpubkey_address")]
        public string? ScriptPubKeyAddress { get; set; }
    }
}
