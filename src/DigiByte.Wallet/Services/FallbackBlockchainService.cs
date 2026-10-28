using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Cascading fallback with smart routing and multiple explorer backends.
///
/// Without a node-source monitor, or with it Disabled (the feature is off), the order is exactly as it always was:
/// - Reads:  Explorer list (Blockbook, Esplora, etc.) → NodeApi as the last resort → Mock (development only)
/// - Writes: NodeApi first → all explorers in order
/// - Regtest: NodeApi only → Mock (no public explorer for regtest)
///
/// With our own node as a source (specs/005-wallet-own-node-source) the monitor decides:
/// - Node not active (starting, syncing, unavailable, stabilizing): explorers only for reads and sends. A node that is still
///   syncing answers address lookups with incomplete data, so it is never asked, not even as a last resort.
/// - Node active: see the route classes (<see cref="NodeRouteClass"/>).
/// </summary>
public class FallbackBlockchainService : IBlockchainService
{
    private readonly NodeApiBlockchainService _nodeApi;
    private readonly List<IBlockchainService> _explorers;
    private readonly MockBlockchainService? _mock;
    private readonly HttpClient _priceHttp;
    private readonly string? _priceApiBaseUrl;
    private readonly FiatRateProvider? _fx;
    private readonly bool _isDevelopment;
    private readonly INodeSourceState? _nodeState;
    private readonly INodeReadFailureSink? _nodeFailureSink;
    private bool _isRegtest;

    /// <summary>Time budget for one node read on a fast route (transaction lookup, fee, height) while the node is active.</summary>
    public TimeSpan FastNodeReadBudget { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Time budget for one node read on an address route, used only as the fallback; the node answers these by scanning (about 14 s).</summary>
    public TimeSpan AddressNodeReadBudget { get; set; } = TimeSpan.FromSeconds(20);

    // Track which explorers are currently failing to skip them temporarily
    private readonly Dictionary<int, DateTime> _explorerCooldowns = new();
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(2);

    // Service-level cache for read operations
    private readonly ConcurrentDictionary<string, (object Value, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan BalanceCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TxHistoryCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BlockHeightCacheTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FeeRateCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>True while a read has failed on every source (and no read has succeeded since). Drives the "can't reach the blockchain" state.</summary>
    public bool AllSourcesFailed { get; private set; }

    /// <summary>Raised when <see cref="AllSourcesFailed"/> changes.</summary>
    public event Action? AllSourcesFailedChanged;

    private void SetAllSourcesFailed(bool value)
    {
        if (AllSourcesFailed == value)
            return;
        AllSourcesFailed = value;
        AllSourcesFailedChanged?.Invoke();
    }

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
        MockBlockchainService? mock = null,
        FiatRateProvider? fx = null,
        INodeSourceState? nodeState = null,
        INodeReadFailureSink? failureSink = null)
    {
        _nodeState = nodeState;
        _nodeFailureSink = failureSink;
        _nodeApi = nodeApi;
        _explorers = explorers.ToList();
        _priceHttp = priceHttp;
        _priceApiBaseUrl = priceApiBaseUrl?.TrimEnd('/');
        _isDevelopment = isDevelopment;
        _mock = isDevelopment ? mock : null;
        _fx = fx;

        // When the active source flips, drop cached answers so one source's data is not served as if it came from the other.
        // (Spendable outputs are never cached: they must be fresh for signing.)
        if (_nodeState is not null)
            _nodeState.Changed += InvalidateCache;
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

    /// <summary>True when node use is off (no monitor or <see cref="NodeSourceState.Disabled"/>) and on regtest: today's order applies.</summary>
    private bool LegacyOrder => _nodeState is null || _nodeState.State == NodeSourceState.Disabled || _isRegtest;

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
    /// With our own node active (<see cref="NodeRouteClass"/>): fast routes try the node first, address routes keep explorers first
    /// and use the node as their fallback, history never uses the node.
    /// </summary>
    private async Task<T> TryRead<T>(Func<IBlockchainService, Task<T>> call, NodeRouteClass routeClass = NodeRouteClass.Address)
    {
        var result = await TryReadCore(call, routeClass);   // throws (after setting the flag) when every source failed
        SetAllSourcesFailed(false);
        return result;
    }

    private async Task<T> TryReadCore<T>(Func<IBlockchainService, Task<T>> call, NodeRouteClass routeClass)
    {
        if (ForceDemoMode && _isDevelopment && _mock != null)
        {
            IsDemoMode = true;
            ActiveBackend = "demo";
            return await call(_mock);
        }

        var nodeActive = !LegacyOrder && _nodeState!.IsActive;

        // Fast routes (transaction lookup, fee, height): the active node first. A failed or over-budget read is answered by an
        // explorer for this request and reported to the monitor; a null (not found) is not authoritative, so the explorers are asked too.
        if (nodeActive && routeClass == NodeRouteClass.Fast)
        {
            var viaNode = await TryNodeAsync(call, FastNodeReadBudget);
            if (viaNode.Answered)
                return viaNode.Value!;
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

        // 2. Fall back to own node (pruned — scantxoutset is slower but always works).
        // Only when node use is off: with the feature on, a node that is not active is never asked (see the class summary).
        if (LegacyOrder)
        {
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
        }
        else if (nodeActive && routeClass == NodeRouteClass.Address)
        {
            // Address balances and spendable outputs: explorers were tried first; the ready node is the fallback.
            var viaNode = await TryNodeAsync(call, AddressNodeReadBudget);
            if (viaNode.Answered)
                return viaNode.Value!;
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
        SetAllSourcesFailed(true);
        throw new InvalidOperationException(
            "All blockchain backends are unavailable. Node and all explorers failed.");
    }

    /// <summary>
    /// Asks our own node with a time budget. <c>Answered</c> is false (and the caller moves on to an explorer) when the node failed,
    /// ran over the budget, or returned null (not found, which is not authoritative). Errors and timeouts are reported to the monitor so
    /// repeated failures demote the node at once; a real answer (including not found) counts as a success.
    /// </summary>
    private async Task<(bool Answered, T? Value)> TryNodeAsync<T>(Func<IBlockchainService, Task<T>> call, TimeSpan budget)
    {
        try
        {
            Console.WriteLine("[Read] Trying node-api (active)...");
            var result = await call(_nodeApi).WaitAsync(budget);
            _nodeFailureSink?.ReportNodeReadSuccess();
            if (result is null)
            {
                Console.WriteLine("[Read] node-api: not found, asking the explorers");
                return (false, default);
            }
            IsDemoMode = false;
            ActiveBackend = "node-api";
            Console.WriteLine("[Read] ✓ node-api succeeded");
            return (true, result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Read] ✗ node-api failed: {ex.Message}");
            _nodeFailureSink?.ReportNodeReadFailure();
            return (false, default);
        }
    }

    /// <summary>
    /// For writes: NodeApi first (direct node), then all explorers in order.
    /// Never falls back to mock for broadcasts.
    /// </summary>
    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        var hexPreview = Convert.ToHexString(rawTransaction).ToLower();
        Console.WriteLine($"[Broadcast] Starting — tx size: {rawTransaction.Length} bytes, hex preview: {hexPreview[..Math.Min(40, hexPreview.Length)]}...");

        // Try own node first. With the feature on this only happens while the node is active; otherwise explorers go first
        // and the node is not used (a syncing node does not relay transactions). A rejected broadcast is not a failed read,
        // so it is not reported to the monitor.
        if (LegacyOrder || _nodeState!.IsActive)
        {
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
        TryRead(s => s.GetTransactionAsync(txId), NodeRouteClass.Fast);

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
        // History: explorers only while node use is on. The node cannot list full history (it only sees transactions that
        // still have unspent outputs), so it is never asked for it, not even as a last resort.
        CachedRead($"txs:{address}:{skip}:{take}", TxHistoryCacheTtl,
            s => s.GetAddressTransactionsAsync(address, skip, take), NodeRouteClass.History);

    public Task<decimal> GetFeeRateAsync() =>
        CachedRead("feerate", FeeRateCacheTtl, s => s.GetFeeRateAsync(), NodeRouteClass.Fast);

    /// <summary>
    /// Price fetch — independent of explorers, uses its own HttpClient (no Polly retries).
    /// Cached for 5 minutes.
    /// </summary>
    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        var cacheKey = $"price:{fiatCurrency.ToLower()}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return (decimal)cached.Value;

        // CoinGecko prices DGB directly in only some currencies; for the others convert the USD price.
        var native = DigiByte.Wallet.Models.FiatCurrencies.IsCoinGeckoNative(fiatCurrency);
        var currency = (native ? fiatCurrency : "USD").ToLower();
        var url = _priceApiBaseUrl != null
            ? $"{_priceApiBaseUrl}/simple?currency={currency}"
            : $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={currency}";

        var data = await _priceHttp.GetFromJsonAsync<JsonElement>(url);
        if (data.TryGetProperty("digibyte", out var dgb) &&
            dgb.TryGetProperty(currency, out var priceEl))
        {
            var price = priceEl.GetDecimal();
            if (!native)
            {
                var perUsd = _fx is null ? null : await _fx.GetPerUsdAsync(fiatCurrency);
                if (perUsd is null) throw new InvalidOperationException($"No exchange rate for {fiatCurrency.ToUpperInvariant()}");
                price *= perUsd.Value;
            }
            if (price > 0)
            {
                _cache[cacheKey] = (price, DateTime.UtcNow.AddMinutes(5));
                return price;
            }
        }

        throw new InvalidOperationException("Price API returned no data");
    }

    public Task<int> GetBlockHeightAsync() =>
        CachedRead("blockheight", BlockHeightCacheTtl, s => s.GetBlockHeightAsync(), NodeRouteClass.Fast);

    /// <summary>
    /// Invalidate all cached data — call after sending a transaction so balance refreshes immediately.
    /// </summary>
    public void InvalidateCache() => _cache.Clear();

    private async Task<T> CachedRead<T>(string key, TimeSpan ttl, Func<IBlockchainService, Task<T>> call,
        NodeRouteClass routeClass = NodeRouteClass.Address)
    {
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
            return (T)entry.Value;

        var result = await TryRead(call, routeClass);
        _cache[key] = (result!, DateTime.UtcNow + ttl);
        return result;
    }
}
