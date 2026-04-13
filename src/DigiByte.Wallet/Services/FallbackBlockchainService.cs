using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Cascading fallback with smart routing and multiple explorer backends:
/// - Reads:  NodeApi (own pruned node) → Explorer list (Blockbook, Esplora, etc.) → Mock
/// - Writes: NodeApi first → all explorers in order
/// - Regtest: NodeApi only → Mock (no public explorer for regtest)
/// </summary>
public class FallbackBlockchainService : IBlockchainService
{
    private readonly NodeApiBlockchainService _nodeApi;
    private readonly List<IBlockchainService> _explorers;
    private readonly MockBlockchainService? _mock;
    private readonly HttpClient _priceHttp;
    private readonly string? _priceApiBaseUrl;
    private readonly bool _isDevelopment;
    private bool _isRegtest;

    // Track which explorers are currently failing to skip them temporarily
    private readonly Dictionary<int, DateTime> _explorerCooldowns = new();
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(2);

    // Service-level cache for read operations
    private readonly ConcurrentDictionary<string, (object Value, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan BalanceCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TxHistoryCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BlockHeightCacheTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FeeRateCacheTtl = TimeSpan.FromMinutes(30);

    public bool IsDemoMode { get; private set; }
    public bool ForceDemoMode { get; set; }
    public string ActiveBackend { get; private set; } = "starting";

    /// <summary>
    /// Names of all registered explorer backends for diagnostics.
    /// </summary>
    public IReadOnlyList<string> ExplorerNames => _explorers.Select(e =>
        e is BlockbookApiService bb ? bb.Name :
        e is BlockchainApiService ? "esplora" : e.GetType().Name).ToList();

    public FallbackBlockchainService(
        NodeApiBlockchainService nodeApi,
        IEnumerable<IBlockchainService> explorers,
        HttpClient priceHttp,
        bool isDevelopment,
        string? priceApiBaseUrl = null,
        MockBlockchainService? mock = null)
    {
        _nodeApi = nodeApi;
        _explorers = explorers.ToList();
        _priceHttp = priceHttp;
        _priceApiBaseUrl = priceApiBaseUrl?.TrimEnd('/');
        _isDevelopment = isDevelopment;
        _mock = isDevelopment ? mock : null;
    }

    public void SetNetwork(bool isTestnet)
    {
        foreach (var explorer in _explorers)
        {
            if (explorer is BlockchainApiService esplora)
                esplora.SetNetwork(isTestnet);
        }
    }

    public void SetNetworkMode(string mode)
    {
        _isRegtest = mode == "regtest";
        foreach (var explorer in _explorers)
        {
            if (explorer is BlockchainApiService esplora)
                esplora.SetNetwork(mode == "testnet");
        }
    }

    private bool IsOnCooldown(int index)
    {
        if (_explorerCooldowns.TryGetValue(index, out var until))
        {
            if (DateTime.UtcNow < until) return true;
            _explorerCooldowns.Remove(index);
        }
        return false;
    }

    /// <summary>
    /// For reads: Explorers first (address-indexed, fast) → NodeApi (pruned, slow scantxoutset) → mock.
    /// On regtest: NodeApi only → mock (no public explorer for regtest).
    /// </summary>
    private async Task<T> TryRead<T>(Func<IBlockchainService, Task<T>> call)
    {
        if (ForceDemoMode && _isDevelopment && _mock != null)
        {
            IsDemoMode = true;
            ActiveBackend = "demo";
            return await call(_mock);
        }

        if (!_isRegtest)
        {
            // 1. Try each explorer in order, skipping those on cooldown
            for (int i = 0; i < _explorers.Count; i++)
            {
                var name = _explorers[i] is BlockbookApiService bb ? bb.Name
                    : _explorers[i] is BlockchainApiService ? "esplora"
                    : $"explorer-{i}";

                if (IsOnCooldown(i))
                {
                    Console.WriteLine($"[Read] Skipping {name} (on cooldown)");
                    continue;
                }

                try
                {
                    var result = await call(_explorers[i]);
                    IsDemoMode = false;
                    ActiveBackend = name;
                    Console.WriteLine($"[Read] ✓ {name} succeeded");
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Read] ✗ {name} failed: {ex.Message}");
                    // Put this explorer on cooldown so we don't hammer it
                    _explorerCooldowns[i] = DateTime.UtcNow + CooldownDuration;
                }
            }
        }

        // 2. Fall back to own node (pruned — scantxoutset is slower but always works)
        try
        {
            Console.WriteLine("[Read] Trying node-api...");
            var result = await call(_nodeApi);
            IsDemoMode = false;
            ActiveBackend = "node-api";
            Console.WriteLine("[Read] ✓ node-api succeeded");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Read] ✗ node-api failed: {ex.Message}");
        }

        // 3. Mock fallback — development only
        if (_isDevelopment && _mock != null)
        {
            IsDemoMode = true;
            ActiveBackend = "demo";
            Console.WriteLine("[Read] Using mock (development mode)");
            return await call(_mock);
        }

        Console.WriteLine("[Read] ✗ ALL BACKENDS FAILED");
        throw new InvalidOperationException(
            "All blockchain backends are unavailable. Node and all explorers failed.");
    }

    /// <summary>
    /// For writes: NodeApi first (direct node), then all explorers in order.
    /// Never falls back to mock for broadcasts.
    /// </summary>
    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var hexPreview = Convert.ToHexString(rawTransaction).ToLower();
        Console.WriteLine($"[Broadcast] Starting — tx size: {rawTransaction.Length} bytes, hex preview: {hexPreview[..Math.Min(40, hexPreview.Length)]}...");

        // Try own node first
        try
        {
            Console.WriteLine("[Broadcast] Trying node-api...");
            var txId = await _nodeApi.BroadcastTransactionAsync(rawTransaction);
            Console.WriteLine($"[Broadcast] ✓ node-api succeeded — txid: {txId}");
            InvalidateCache();
            return txId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast] ✗ node-api failed: {ex.Message}");
        }

        // Try each explorer
        Exception? lastError = null;
        for (int i = 0; i < _explorers.Count; i++)
        {
            var name = _explorers[i] is BlockbookApiService bb ? bb.Name
                : _explorers[i] is BlockchainApiService ? "esplora"
                : $"explorer-{i}";
            try
            {
                Console.WriteLine($"[Broadcast] Trying {name}...");
                var txId = await _explorers[i].BroadcastTransactionAsync(rawTransaction);
                Console.WriteLine($"[Broadcast] ✓ {name} succeeded — txid: {txId}");
                InvalidateCache();
                return txId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Broadcast] ✗ {name} failed: {ex.Message}");
                lastError = ex;
            }
        }

        Console.WriteLine($"[Broadcast] ✗ ALL BACKENDS FAILED. Last error: {lastError?.Message}");
        throw new Exception($"Broadcast failed on all backends. Last error: {lastError?.Message}");
    }

    // Read operations — cached where appropriate
    public Task<long> GetBalanceAsync(string address) =>
        CachedRead($"bal:{address}", BalanceCacheTtl, s => s.GetBalanceAsync(address));

    public Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        // Materialize once so we can build a stable cache key and avoid re-enumeration
        var list = addresses as IList<string> ?? addresses.ToList();
        var key = $"bal-multi:{string.Join(",", list.OrderBy(a => a))}";
        return CachedRead(key, BalanceCacheTtl, s => s.GetBalanceAsync(list));
    }

    // UTXOs are NOT cached — they must be fresh for transaction signing
    public Task<List<UtxoInfo>> GetUtxosAsync(string address) =>
        TryRead(s => s.GetUtxosAsync(address));

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) =>
        TryRead(s => s.GetUtxosAsync(addresses));

    public Task<TransactionInfo?> GetTransactionAsync(string txId) =>
        TryRead(s => s.GetTransactionAsync(txId));

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
        CachedRead($"txs:{address}:{skip}:{take}", TxHistoryCacheTtl,
            s => s.GetAddressTransactionsAsync(address, skip, take));

    public Task<decimal> GetFeeRateAsync() =>
        CachedRead("feerate", FeeRateCacheTtl, s => s.GetFeeRateAsync());

    /// <summary>
    /// Price fetch — independent of explorers, uses its own HttpClient (no Polly retries).
    /// Cached for 5 minutes.
    /// </summary>
    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        var cacheKey = $"price:{fiatCurrency.ToLower()}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return (decimal)cached.Value;

        var currency = fiatCurrency.ToLower();
        var url = _priceApiBaseUrl != null
            ? $"{_priceApiBaseUrl}/simple?currency={currency}"
            : $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={currency}";

        var data = await _priceHttp.GetFromJsonAsync<JsonElement>(url);
        if (data.TryGetProperty("digibyte", out var dgb) &&
            dgb.TryGetProperty(currency, out var priceEl))
        {
            var price = priceEl.GetDecimal();
            if (price > 0)
            {
                _cache[cacheKey] = (price, DateTime.UtcNow.AddMinutes(5));
                return price;
            }
        }

        throw new InvalidOperationException("Price API returned no data");
    }

    public Task<int> GetBlockHeightAsync() =>
        CachedRead("blockheight", BlockHeightCacheTtl, s => s.GetBlockHeightAsync());

    /// <summary>
    /// Invalidate all cached data — call after sending a transaction so balance refreshes immediately.
    /// </summary>
    public void InvalidateCache() => _cache.Clear();

    private async Task<T> CachedRead<T>(string key, TimeSpan ttl, Func<IBlockchainService, Task<T>> call)
    {
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
            return (T)entry.Value;

        var result = await TryRead(call);
        _cache[key] = (result!, DateTime.UtcNow + ttl);
        return result;
    }
}
