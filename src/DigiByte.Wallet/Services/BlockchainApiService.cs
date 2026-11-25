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
public class BlockchainApiService : IBlockchainService, IAddressSummarySource
{
    private readonly HttpClient _http;
    private readonly HttpClient _priceHttp;
    private readonly string? _priceApiBaseUrl;
    private string _baseUrl;
    private bool _isTestnet;

    /// <summary>How many transactions one address's history is paged up to (a safety cap against a source that never ends).</summary>
    public const int MaxTransactionsPerAddress = 2000;

    /// <summary>Confirmed transactions per Esplora history page.</summary>
    private const int EsploraPageSize = 25;

    /// <summary>How long one tip height is shared by every caller (spec 023, research R6). Shorter than the Fallback's 15 s.</summary>
    public static readonly TimeSpan TipHeightLifetime = TimeSpan.FromSeconds(10);

    private readonly object _tipGate = new();
    private Task<int>? _tipTask;
    private DateTime _tipReadAt;

    // Public DigiByte explorer APIs
    private const string MainnetApi = "https://digiexplorer.info/api";
    private const string TestnetApi = "https://digiexplorer.info/testnet/api";

    // Fallback: chainz.cryptoid.info
    private const string FallbackMainnet = "https://chainz.cryptoid.info/dgb/api.dws";

    public BlockchainApiService(HttpClient http, HttpClient? priceHttp = null, string? priceApiBaseUrl = null, bool isTestnet = false)
    {
        _http = http;
        _priceHttp = priceHttp ?? http;
        _priceApiBaseUrl = priceApiBaseUrl?.TrimEnd('/');
        _isTestnet = isTestnet;
        _baseUrl = isTestnet ? TestnetApi : MainnetApi;
    }

    public void SetNetwork(bool isTestnet)
    {
        _isTestnet = isTestnet;
        _baseUrl = isTestnet ? TestnetApi : MainnetApi;
        lock (_tipGate) _tipTask = null;
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
        var addressList = addresses.ToList();
        if (addressList.Count == 0) return 0;

        // Query up to 10 addresses in parallel to avoid hammering the API.
        // Strict: an address the explorer could not answer for must fail the whole read, never count as an empty one. Otherwise a
        // rate limit or a timeout on a few addresses shows a balance that is too low, and the caller cannot tell. The fallback
        // service then tries the next source, and the screens show "unable to load" instead of a wrong total.
        var semaphore = new SemaphoreSlim(10);
        var tasks = addressList.Select(async addr =>
        {
            await semaphore.WaitAsync();
            try { return await GetBalanceStrictAsync(addr); }
            finally { semaphore.Release(); }
        });

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    private async Task<long> GetBalanceStrictAsync(string address)
    {
        var reply = await _http.GetFromJsonAsync<EsploraAddressCounts>($"{_baseUrl}/address/{address}");
        if (reply?.ChainStats is null)
            throw new InvalidOperationException("The explorer's reply had no chain statistics.");
        return reply.ChainStats.FundedTxoSum - reply.ChainStats.SpentTxoSum;
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

    /// <summary>Like <see cref="GetUtxosAsync(string)"/>, but a failure or an unreadable reply throws instead of reading as no coins.</summary>
    public async Task<List<UtxoInfo>> GetVerifiedUtxosAsync(string address)
    {
        var utxos = await _http.GetFromJsonAsync<List<EsploraUtxo>>($"{_baseUrl}/address/{address}/utxo")
            ?? throw new InvalidOperationException("The explorer's reply had no unspent outputs list.");
        return utxos.Select(u => new UtxoInfo
        {
            TxId = u.TxId,
            OutputIndex = (uint)u.Vout,
            AmountSatoshis = u.Value,
            ScriptPubKey = "",
            Confirmations = u.Status?.Confirmed == true ? 1 : 0,
        }).ToList();
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        var addressList = addresses.ToList();
        if (addressList.Count == 0) return [];

        var semaphore = new SemaphoreSlim(10);
        var tasks = addressList.Select(async addr =>
        {
            await semaphore.WaitAsync();
            try { return await GetUtxosAsync(addr); }
            finally { semaphore.Release(); }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var hex = Convert.ToHexString(rawTransaction).ToLower();
        Console.WriteLine($"[Esplora] Broadcasting to {_baseUrl}/tx — hex length: {hex.Length}");
        var response = await _http.PostAsync($"{_baseUrl}/tx",
            new StringContent(hex, System.Text.Encoding.UTF8, "text/plain"));

        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[Esplora] Response: {(int)response.StatusCode} {response.StatusCode} — {body[..Math.Min(200, body.Length)]}");

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Esplora] ✓ Broadcast success — txid: {body}");
            return body;
        }

        throw new Exception($"Broadcast failed: {body}");
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        try
        {
            var tx = await _http.GetFromJsonAsync<EsploraTx>($"{_baseUrl}/tx/{txId}");
            if (tx == null) return null;

            // Calculate confirmations from block height
            int confirmations = 0;
            if (tx.Status?.Confirmed == true && tx.Status.BlockHeight > 0)
            {
                try
                {
                    var currentHeight = await GetBlockHeightAsync();
                    confirmations = currentHeight > 0 ? currentHeight - tx.Status.BlockHeight + 1 : 1;
                }
                catch { confirmations = 1; }
            }

            return new TransactionInfo
            {
                TxId = tx.TxId,
                Confirmations = confirmations,
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
                    ScriptHex = v.ScriptPubKey,
                }).ToList() ?? [],
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The address's history, newest first, paged past the explorer's 25-transaction page up to
    /// <see cref="MaxTransactionsPerAddress"/>, then <paramref name="skip"/>/<paramref name="take"/> applied. A failure on the first
    /// page reads as no history (as before); a failure on a later page keeps the pages already read.
    /// </summary>
    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        var all = new List<TransactionInfo>();
        try
        {
            var page = await GetAddressHistoryPageAsync(address, null);
            all.AddRange(page.Transactions);
            var confirmedOnPage = page.Transactions.Count(t => t.Confirmations > 0);
            while (confirmedOnPage >= EsploraPageSize && page.LastConfirmedTxId is not null && all.Count < MaxTransactionsPerAddress)
            {
                try
                {
                    page = await GetAddressHistoryPageAsync(address, page.LastConfirmedTxId);
                }
                catch
                {
                    break;
                }
                all.AddRange(page.Transactions);
                confirmedOnPage = page.Transactions.Count;
            }
        }
        catch
        {
            return [];
        }

        if (all.Count > MaxTransactionsPerAddress)
            all.RemoveRange(MaxTransactionsPerAddress, all.Count - MaxTransactionsPerAddress);
        return all.Skip(skip).Take(take).ToList();
    }

    /// <summary>A guard against a source whose history never ends: far beyond any real address, still finite.</summary>
    public const int CompleteHistorySafetyCap = 200_000;

    /// <summary>
    /// Every transaction of the address for the CSV export (spec 031, research R1): pages to the end with no
    /// <see cref="MaxTransactionsPerAddress"/> cap, and throws on any failed page instead of returning a partial list.
    /// </summary>
    public async Task<List<TransactionInfo>> GetCompleteAddressHistoryAsync(string address, CancellationToken ct = default)
    {
        var all = new List<TransactionInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var page = await GetAddressHistoryPageAsync(address, null);
        Add(page.Transactions);
        var confirmedOnPage = page.Transactions.Count(t => t.Confirmations > 0);
        while (confirmedOnPage >= EsploraPageSize && page.LastConfirmedTxId is not null)
        {
            ct.ThrowIfCancellationRequested();
            page = await GetAddressHistoryPageAsync(address, page.LastConfirmedTxId);
            if (Add(page.Transactions) == 0 && page.Transactions.Count > 0)
                throw new InvalidOperationException("The explorer's history pages repeat instead of moving on.");
            if (all.Count > CompleteHistorySafetyCap)
                throw new InvalidOperationException("The explorer's history did not end.");
            confirmedOnPage = page.Transactions.Count;
        }
        return all;

        int Add(IEnumerable<TransactionInfo> txs)
        {
            var added = 0;
            foreach (var tx in txs)
                if (seen.Add(tx.TxId)) { all.Add(tx); added++; }
            return added;
        }
    }

    /// <summary>One lookup of the address's confirmed and pending counts and amounts. Strict: an incomplete reply throws.</summary>
    public async Task<Models.AddressSummary> GetAddressSummaryAsync(string address, bool recordFailure = true)
    {
        var reply = await _http.GetFromJsonAsync<EsploraAddressCounts>($"{_baseUrl}/address/{address}");
        if (reply?.ChainStats is null || reply.MempoolStats is null)
            throw new InvalidOperationException("The explorer's reply had no chain or mempool statistics.");
        return new Models.AddressSummary
        {
            Address = address,
            ConfirmedTxCount = reply.ChainStats.TxCount,
            PendingTxCount = reply.MempoolStats.TxCount,
            ConfirmedFunded = reply.ChainStats.FundedTxoSum,
            ConfirmedSpent = reply.ChainStats.SpentTxoSum,
            PendingFunded = reply.MempoolStats.FundedTxoSum,
            PendingSpent = reply.MempoolStats.SpentTxoSum,
            ReadAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// <c>GET /address/{a}/txs</c> (pending plus the newest 25 confirmed) or <c>GET /address/{a}/txs/chain/{after}</c> (the next 25
    /// confirmed). Strict: a failure throws instead of reading as an empty page.
    /// </summary>
    public async Task<Models.AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId)
    {
        var url = afterTxId is null ? $"{_baseUrl}/address/{address}/txs" : $"{_baseUrl}/address/{address}/txs/chain/{afterTxId}";
        var txs = await _http.GetFromJsonAsync<List<EsploraTx>>(url)
            ?? throw new InvalidOperationException("The explorer's reply had no transaction list.");

        var currentHeight = await GetBlockHeightAsync();
        var heights = new Dictionary<string, int>();
        string? lastConfirmed = null;
        foreach (var tx in txs)
        {
            if (tx.Status?.Confirmed != true) continue;
            lastConfirmed = tx.TxId;
            if (tx.Status.BlockHeight > 0) heights[tx.TxId] = tx.Status.BlockHeight;
        }

        return new Models.AddressHistoryPage(txs.Select(tx => ToInfo(tx, currentHeight)).ToList(), lastConfirmed, heights);
    }

    private static TransactionInfo ToInfo(EsploraTx tx, int currentHeight) => new()
    {
        TxId = tx.TxId,
        Confirmations = tx.Status?.Confirmed == true && tx.Status.BlockHeight > 0 && currentHeight > 0
            ? currentHeight - tx.Status.BlockHeight + 1
            : tx.Status?.Confirmed == true ? 1 : 0,
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
            ScriptHex = v.ScriptPubKey,
        }).ToList() ?? [],
    };

    public async Task<bool> HasAddressHistoryAsync(string address)
    {
        // No catch: a failure here must reach the caller instead of reading as an unused address.
        var counts = await _http.GetFromJsonAsync<EsploraAddressCounts>($"{_baseUrl}/address/{address}");
        if (counts?.ChainStats is null)
            throw new InvalidOperationException("The explorer's reply had no transaction counts.");
        return counts.ChainStats.TxCount + (counts.MempoolStats?.TxCount ?? 0) > 0;
    }

    public async Task<decimal> GetFeeRateAsync()
    {
        // DigiByte has very low fees — 1 sat/vB is usually enough
        return 1.0m;
    }

    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        var currency = fiatCurrency.ToLower();
        var url = _priceApiBaseUrl != null
            ? $"{_priceApiBaseUrl}/simple?currency={currency}"
            : $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={currency}";
        var data = await _priceHttp.GetFromJsonAsync<JsonElement>(url);
        if (data.TryGetProperty("digibyte", out var dgb) &&
            dgb.TryGetProperty(currency, out var price))
        {
            var result = price.GetDecimal();
            if (result > 0) return result;
        }
        throw new InvalidOperationException("CoinGecko returned no price data");
    }

    /// <summary>
    /// The tip height, shared by every caller for <see cref="TipHeightLifetime"/> and while a request is in flight, so one history
    /// load (and the Fallback's own height read) sends one tip request. 0 when the explorer could not answer; a 0 is never shared.
    /// </summary>
    public Task<int> GetBlockHeightAsync()
    {
        lock (_tipGate)
        {
            if (_tipTask is { } task)
            {
                var running = !task.IsCompleted;
                var fresh = task.IsCompletedSuccessfully && task.Result > 0 && DateTime.UtcNow - _tipReadAt < TipHeightLifetime;
                if (running || fresh) return task;
            }
            _tipReadAt = DateTime.UtcNow;
            _tipTask = ReadTipAsync();
            return _tipTask;
        }
    }

    private async Task<int> ReadTipAsync()
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

    /// <summary>Nullable, unlike <see cref="EsploraAddress"/>, so a reply without the counts can't read as zero transactions.</summary>
    private class EsploraAddressCounts
    {
        [JsonPropertyName("chain_stats")]
        public EsploraChainStats? ChainStats { get; set; }

        [JsonPropertyName("mempool_stats")]
        public EsploraChainStats? MempoolStats { get; set; }
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

        [JsonPropertyName("scriptpubkey")]
        public string? ScriptPubKey { get; set; }
    }
}
