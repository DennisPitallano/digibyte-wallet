namespace DigiByte.Wallet.Services;

/// <summary>
/// Cascading fallback with smart routing:
/// - Reads (balance, txs): Explorer first → NodeApi → mock
/// - Writes (broadcast): NodeApi first → Explorer
/// - Regtest: NodeApi only → mock (no public explorer for regtest)
/// </summary>
public class FallbackBlockchainService : IBlockchainService
{
    private readonly NodeApiBlockchainService _nodeApi;
    private readonly BlockchainApiService _explorer;
    private readonly MockBlockchainService _mock;
    private bool _isRegtest;

    public bool IsDemoMode { get; private set; }
    public bool ForceDemoMode { get; set; }
    public string ActiveBackend { get; private set; } = "starting";

    public FallbackBlockchainService(
        NodeApiBlockchainService nodeApi,
        BlockchainApiService explorer,
        MockBlockchainService mock)
    {
        _nodeApi = nodeApi;
        _explorer = explorer;
        _mock = mock;
    }

    public void SetNetwork(bool isTestnet) => _explorer.SetNetwork(isTestnet);

    public void SetNetworkMode(string mode)
    {
        _isRegtest = mode == "regtest";
        _explorer.SetNetwork(mode == "testnet");
    }

    /// <summary>
    /// For reads: Explorer first (has address indexing), then NodeApi, then mock.
    /// On regtest: NodeApi first (no public explorer).
    /// </summary>
    private async Task<T> TryRead<T>(Func<IBlockchainService, Task<T>> call)
    {
        if (ForceDemoMode)
        {
            IsDemoMode = true;
            ActiveBackend = "demo";
            return await call(_mock);
        }

        if (!_isRegtest)
        {
            // Mainnet/Testnet: try public explorer first (full address index)
            try
            {
                var result = await call(_explorer);
                IsDemoMode = false;
                ActiveBackend = "explorer";
                return result;
            }
            catch { }
        }

        // Try our own Node API
        try
        {
            var result = await call(_nodeApi);
            IsDemoMode = false;
            ActiveBackend = "node-api";
            return result;
        }
        catch { }

        // Fall back to mock
        IsDemoMode = true;
        ActiveBackend = "demo";
        return await call(_mock);
    }

    /// <summary>
    /// For writes: NodeApi first (direct node access), then explorer.
    /// Never falls back to mock.
    /// </summary>
    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        try { return await _nodeApi.BroadcastTransactionAsync(rawTransaction); }
        catch (Exception ex)
        {
            try { return await _explorer.BroadcastTransactionAsync(rawTransaction); }
            catch { throw new Exception($"Broadcast failed: {ex.Message}"); }
        }
    }

    // Read operations — explorer first on mainnet/testnet
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
