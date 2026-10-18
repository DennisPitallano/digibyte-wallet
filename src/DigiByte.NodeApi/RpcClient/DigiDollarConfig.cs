namespace DigiByte.NodeApi.RpcClient;

/// <summary>
/// Configuration for DigiDollar RPC surface.
/// When Enabled=false, all /api/digidollar/* endpoints return 503 Service Unavailable.
/// </summary>
public class DigiDollarConfig
{
    /// <summary>
    /// Master feature flag. When false, no DD endpoints will execute.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Name of the descriptor wallet loaded on the node that holds DD keys.
    /// DD RPCs are routed through /wallet/{WalletName} on the node.
    /// </summary>
    public string WalletName { get; set; } = "digipay-hot";

    /// <summary>
    /// Minimum DGB balance to maintain in the DD wallet for paying transaction fees.
    /// When below this, /api/digidollar/health reports reserve_healthy=false and warnings are logged.
    /// </summary>
    public decimal MinDgbFeeReserveDgb { get; set; } = 100m;

    /// <summary>
    /// Optional shared secret required in the X-NodeApi-Key header on write endpoints
    /// (send, mint, redeem, address/new). When null/empty, write endpoints are unprotected.
    /// Set via NODEAPI_WRITE_KEY environment variable in production.
    /// </summary>
    public string? WriteKey { get; set; }

    /// <summary>SQLite file for the anonymous usage counters (per-day, per-event counts only).</summary>
    public string MetricsDbPath { get; set; } = "data/metrics.db";

    /// <summary>
    /// Origins allowed to POST usage events (the wallet's origin). Empty means no cross-origin caller is accepted.
    /// Set via NODEAPI_WALLET_ORIGINS (comma separated) in containers.
    /// </summary>
    public string[] WalletOrigins { get; set; } = [];

    /// <summary>
    /// SQLite file for the DigiDollar output index (public chain data only). Created on first run when Enabled.
    /// Set via DIGIDOLLAR_INDEX_DB in containers (mount a persistent volume).
    /// </summary>
    public string IndexDbPath { get; set; } = "data/dd-index.db";

    /// <summary>
    /// First block the index walks. Defaults: mainnet 23,869,440 (DigiDollar activation); testnet26 has no fixed
    /// default here, so set it (0 scans everything).
    /// </summary>
    public int? IndexStartHeight { get; set; }

    /// <summary>Most blocks the indexer applies per pass before refreshing the mempool overlay and status.</summary>
    public int IndexBlocksPerPass { get; set; } = 500;

    /// <summary>Pause between passes once the index has caught up with the node tip.</summary>
    public TimeSpan IndexPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Interval for the background fee reserve monitor to poll DGB balance.
    /// </summary>
    public TimeSpan FeeReserveCheckInterval { get; set; } = TimeSpan.FromSeconds(60);
}
