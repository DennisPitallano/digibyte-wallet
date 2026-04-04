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
    private readonly bool _isDevelopment;
    private bool _isRegtest;

    // Track which explorers are currently failing to skip them temporarily
    private readonly Dictionary<int, DateTime> _explorerCooldowns = new();
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(2);

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
        bool isDevelopment,
        MockBlockchainService? mock = null)
    {
        _nodeApi = nodeApi;
        _explorers = explorers.ToList();
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
                if (IsOnCooldown(i)) continue;

                try
                {
                    var result = await call(_explorers[i]);
                    IsDemoMode = false;
                    ActiveBackend = _explorers[i] is BlockbookApiService bb ? bb.Name
                        : _explorers[i] is BlockchainApiService ? "esplora"
                        : $"explorer-{i}";
                    return result;
                }
                catch
                {
                    // Put this explorer on cooldown so we don't hammer it
                    _explorerCooldowns[i] = DateTime.UtcNow + CooldownDuration;
                }
            }
        }

        // 2. Fall back to own node (pruned — scantxoutset is slower but always works)
        try
        {
            var result = await call(_nodeApi);
            IsDemoMode = false;
            ActiveBackend = "node-api";
            return result;
        }
        catch { }

        // 3. Mock fallback — development only
        if (_isDevelopment && _mock != null)
        {
            IsDemoMode = true;
            ActiveBackend = "demo";
            return await call(_mock);
        }

        throw new InvalidOperationException(
            "All blockchain backends are unavailable. Node and all explorers failed.");
    }

    /// <summary>
    /// For writes: NodeApi first (direct node), then all explorers in order.
    /// Never falls back to mock for broadcasts.
    /// </summary>
    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        // Try own node first
        try
        {
            return await _nodeApi.BroadcastTransactionAsync(rawTransaction);
        }
        catch { }

        // Try each explorer
        Exception? lastError = null;
        for (int i = 0; i < _explorers.Count; i++)
        {
            try
            {
                return await _explorers[i].BroadcastTransactionAsync(rawTransaction);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new Exception($"Broadcast failed on all backends. Last error: {lastError?.Message}");
    }

    // Read operations — own node first, then explorers in order
    public Task<long> GetBalanceAsync(string address) =>
        TryRead(s => s.GetBalanceAsync(address));

    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) =>
        TryRead(s => s.GetBalanceAsync(addresses));

    public Task<List<UtxoInfo>> GetUtxosAsync(string address) =>
        TryRead(s => s.GetUtxosAsync(address));

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) =>
        TryRead(s => s.GetUtxosAsync(addresses));

    public Task<TransactionInfo?> GetTransactionAsync(string txId) =>
        TryRead(s => s.GetTransactionAsync(txId));

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
        TryRead(s => s.GetAddressTransactionsAsync(address, skip, take));

    public Task<decimal> GetFeeRateAsync() =>
        TryRead(s => s.GetFeeRateAsync());

    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") =>
        TryRead(s => s.GetDgbPriceAsync(fiatCurrency));

    public Task<int> GetBlockHeightAsync() =>
        TryRead(s => s.GetBlockHeightAsync());
}
