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
public class FallbackBlockchainService : IBlockchainService, IAddressSummarySource
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
    private readonly IBlockchainService? _gatewaySource;
    private readonly INodeSourceState? _gatewayState;
    private readonly INodeReadFailureSink? _gatewayFailureSink;
    private readonly GatewayPosition _gatewayPosition;
    private bool _isRegtest;

    // How each source has answered reads in this session (for the status views). Keys: "explorer-0", "explorer-1", ..., "node", "gateway".
    private readonly ConcurrentDictionary<string, SourceCounters> _counters = new();

    private volatile string? _lastAnsweredKey;

    private void Note(string key, bool success)
    {
        var c = _counters.GetOrAdd(key, _ => new SourceCounters());
        if (success)
        {
            c.Success();
            _lastAnsweredKey = key;
        }
        else
        {
            c.Failure();
        }
    }

    /// <summary>Time budget for one node read on a fast route (transaction lookup, fee, height) while the node is active.</summary>
    public TimeSpan FastNodeReadBudget { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Time budget for one node read on an address route, used only as the fallback; the node answers these by scanning (about 14 s).</summary>
    public TimeSpan AddressNodeReadBudget { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Time budget for one read through the SPV gateway while it is active (specs/011-p2p-spv-gateway).</summary>
    public TimeSpan GatewayReadBudget { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// True when the SPV gateway is configured, not on regtest, and its monitor says it is active. Never waits on the gateway's health:
    /// the monitor decides in the background, so no read or send is held up by it.
    /// </summary>
    private bool GatewayActive => _gatewaySource is not null && _gatewayState is { IsActive: true } && !_isRegtest;

    // Track which explorers are currently failing to skip them temporarily
    private readonly Dictionary<int, DateTime> _explorerCooldowns = new();
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// One cache slot: a finished answer with its expiry, or a read still in flight that concurrent callers share (spec 023,
    /// FR-002). <see cref="Generation"/> is the cache generation when the read started; a read from an older generation (one
    /// that started before <see cref="InvalidateCache"/>) is never stored, so nothing from before a send is served after it.
    /// </summary>
    private sealed class CacheEntry
    {
        public object? Value;
        public DateTime ExpiresAt;
        public Task<object?>? Pending;
        public long Generation;
    }

    // Service-level cache for read operations
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly object _cacheGate = new();
    private long _cacheGeneration;
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
        INodeReadFailureSink? failureSink = null,
        IBlockchainService? gatewaySource = null,
        INodeSourceState? gatewayState = null,
        INodeReadFailureSink? gatewayFailureSink = null,
        GatewayPosition gatewayPosition = GatewayPosition.Backup)
    {
        _nodeState = nodeState;
        _nodeFailureSink = failureSink;
        _gatewaySource = gatewaySource;
        _gatewayState = gatewayState;
        _gatewayFailureSink = gatewayFailureSink;
        _gatewayPosition = gatewayPosition;
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
        if (_gatewayState is not null)
            _gatewayState.Changed += InvalidateCache;
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
    /// The sources reads go to, in the order they are asked right now, with each one's state and how it has answered in this session.
    /// A source that is configured but not ready is listed after them as "Standby". It reads state only and asks nothing.
    /// </summary>
    public IReadOnlyList<ReadSourceEntry> DescribeReadSources()
    {
        var inUse = new List<ReadSourceEntry>();
        var standby = new List<ReadSourceEntry>();
        var now = DateTime.UtcNow;
        var lastKey = ActiveBackend == "demo" ? "demo" : _lastAnsweredKey;

        ReadSourceEntry Make(string key, string name, ReadSourceKind kind, bool used, string status, ReadSourceTone tone, string? note, DateTime? cooldown = null)
        {
            _counters.TryGetValue(key, out var c);
            return new ReadSourceEntry(key, name, kind, used ? "" : "Standby", used, status, tone, note, key == lastKey,
                c?.Successes ?? 0, c?.Failures ?? 0, c?.LastSuccessUtc, c?.LastFailureUtc, cooldown);
        }

        ReadSourceEntry? gateway = null;
        if (_gatewaySource is not null && _gatewayState is not null)
        {
            var active = GatewayActive;
            var (status, tone) = StateWords(_gatewayState.State);
            gateway = Make("gateway", "Light-client gateway", ReadSourceKind.Gateway, active, status, tone,
                active
                    ? (_gatewayPosition == GatewayPosition.First
                        ? "Asked before the explorers. Every answer is checked against headers the wallet verified."
                        : "Asked only after the explorers could not answer. Every answer is checked against headers the wallet keeps.")
                    : "Not used until it has stayed level with the chain for the steady period.");
        }
        if (gateway is { InUse: true } && _gatewayPosition == GatewayPosition.First)
            inUse.Add(gateway);

        if (!_isRegtest)
        {
            for (var i = 0; i < _explorers.Count; i++)
            {
                var name = _explorers[i] is BlockbookApiService ? "Blockbook explorer" : _explorers[i] is BlockchainApiService ? "Esplora explorer" : $"Explorer {i + 1}";
                var cooling = _explorerCooldowns.TryGetValue(i, out var until) && until > now;
                inUse.Add(Make($"explorer-{i}", name, ReadSourceKind.Explorer, true,
                    cooling ? "cooling down" : "ready", cooling ? ReadSourceTone.Bad : ReadSourceTone.Good,
                    cooling ? "Failed recently, so it is skipped for a short while." : "A public block explorer.", cooling ? until : null));
            }
        }

        // Our own node: a fallback (or first for quick lookups) while active; with node use off it is the last resort, after the gateway.
        var gatewayBeforeNode = LegacyOrder && gateway is { InUse: true } && _gatewayPosition == GatewayPosition.Backup;
        if (gatewayBeforeNode)
            inUse.Add(gateway!);
        if (LegacyOrder)
        {
            inUse.Add(Make("node", "Own node", ReadSourceKind.OwnNode, true, "last resort", ReadSourceTone.Waiting,
                "Node use is off, so nothing checks that it is ready. It is asked only when every other source failed."));
        }
        else if (_nodeState is { IsActive: true } active)
        {
            var (status, tone) = StateWords(active.State);
            inUse.Add(Make("node", "Own node", ReadSourceKind.OwnNode, true, status, tone,
                "Asked first for quick lookups (transactions, fees, height) and after the explorers for balances."));
        }
        else if (_nodeState is not null)
        {
            var (status, tone) = StateWords(_nodeState.State);
            standby.Add(Make("node", "Own node", ReadSourceKind.OwnNode, false, status, tone,
                "Not used until it has stayed level with the chain for the steady period."));
        }

        if (gateway is { InUse: true } && _gatewayPosition == GatewayPosition.Backup && !gatewayBeforeNode)
            inUse.Add(gateway);
        else if (gateway is { InUse: false })
            standby.Add(gateway);

        if (_isDevelopment && _mock is not null)
            inUse.Add(Make("demo", "Demo data", ReadSourceKind.Demo, true, "development only", ReadSourceTone.Off, "Used only when nothing else answers, in development."));

        var ordered = new List<ReadSourceEntry>();
        for (var i = 0; i < inUse.Count; i++)
            ordered.Add(inUse[i] with { Role = i == 0 ? "Primary" : $"Fallback {i}" });
        ordered.AddRange(standby);
        return ordered;
    }

    private static (string Status, ReadSourceTone Tone) StateWords(NodeSourceState state) => state switch
    {
        NodeSourceState.Active => ("active", ReadSourceTone.Good),
        NodeSourceState.Stabilizing => ("stabilizing", ReadSourceTone.Waiting),
        NodeSourceState.NotReady => ("not ready", ReadSourceTone.Waiting),
        NodeSourceState.Unavailable => ("unreachable", ReadSourceTone.Bad),
        _ => ("off", ReadSourceTone.Off),
    };

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

        // The SPV gateway when it is first in line: only while its monitor says it is active. A failed, slow or empty answer is
        // reported to the monitor and the usual sources are asked as if it were not there.
        if (GatewayActive && _gatewayPosition == GatewayPosition.First)
        {
            var viaGateway = await TryGatewayAsync(call);
            if (viaGateway.Answered)
                return viaGateway.Value!;
        }

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
                    Note($"explorer-{i}", true);
                    Console.WriteLine($"[Read] ✓ {name} succeeded");
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Read] ✗ {name} failed: {ex.Message}");
                    // Put this explorer on cooldown so we don't hammer it
                    Note($"explorer-{i}", false);
                    _explorerCooldowns[i] = DateTime.UtcNow + CooldownDuration;
                }
            }
        }

        // With node use off nothing checks that the node is ready, while the gateway (when active) has a readiness check and its answers
        // are checked against headers the wallet keeps, so the gateway is asked first and the node is the last resort.
        var gatewayAsked = false;
        if (LegacyOrder && GatewayActive && _gatewayPosition == GatewayPosition.Backup)
        {
            gatewayAsked = true;
            var viaGatewayFirst = await TryGatewayAsync(call);
            if (viaGatewayFirst.Answered)
                return viaGatewayFirst.Value!;
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
                Note("node", true);
                Console.WriteLine("[Read] ✓ node-api succeeded");
                return result;
            }
            catch (Exception ex)
            {
                Note("node", false);
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

        // The SPV gateway as a backup: asked only after the explorers (and the node, where it applies) could not answer.
        if (GatewayActive && _gatewayPosition == GatewayPosition.Backup && !gatewayAsked)
        {
            var viaGateway = await TryGatewayAsync(call);
            if (viaGateway.Answered)
                return viaGateway.Value!;
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
            Note("node", true);
            Console.WriteLine("[Read] ✓ node-api succeeded");
            return (true, result);
        }
        catch (Exception ex)
        {
            Note("node", false);
            Console.WriteLine($"[Read] ✗ node-api failed: {ex.Message}");
            _nodeFailureSink?.ReportNodeReadFailure();
            return (false, default);
        }
    }

    /// <summary>
    /// Asks the SPV gateway with a time budget. <c>Answered</c> is false when it failed, ran over the budget or returned null (not found,
    /// which is not authoritative). Failures are reported to the gateway's monitor so repeated ones demote it at once.
    /// </summary>
    private async Task<(bool Answered, T? Value)> TryGatewayAsync<T>(Func<IBlockchainService, Task<T>> call)
    {
        try
        {
            Console.WriteLine("[Read] Trying spv-gateway (active)...");
            var result = await call(_gatewaySource!).WaitAsync(GatewayReadBudget);
            _gatewayFailureSink?.ReportNodeReadSuccess();
            if (result is null)
            {
                Console.WriteLine("[Read] spv-gateway: not found, asking the other sources");
                return (false, default);
            }
            IsDemoMode = false;
            ActiveBackend = "spv-gateway";
            Note("gateway", true);
            Console.WriteLine("[Read] ✓ spv-gateway succeeded");
            return (true, result);
        }
        catch (GatewayDeclinedException ex)
        {
            // The gateway cannot answer this one correctly (no wallet record yet, headers behind): not a failure, so the monitor is not told.
            Console.WriteLine($"[Read] spv-gateway declined: {ex.Message}");
            return (false, default);
        }
        catch (Exception ex)
        {
            Note("gateway", false);
            Console.WriteLine($"[Read] ✗ spv-gateway failed: {ex.Message}");
            _gatewayFailureSink?.ReportNodeReadFailure();
            return (false, default);
        }
    }

    /// <summary>Sends a transaction through the SPV gateway. Returns null (and moves on) when it could not.</summary>
    private async Task<string?> TryGatewayBroadcastAsync(byte[] rawTransaction)
    {
        try
        {
            Console.WriteLine("[Broadcast] Trying spv-gateway...");
            var txId = await _gatewaySource!.BroadcastTransactionAsync(rawTransaction);
            Console.WriteLine($"[Broadcast] ✓ spv-gateway succeeded — txid: {txId}");
            InvalidateCache();
            ActiveBackend = "spv-gateway";
            LastBroadcast = (_gatewaySource as IBroadcastReporter)?.LastBroadcast;
            return txId;
        }
        catch (Exception ex)
        {
            // A rejected broadcast is not a failed read, so the monitor is not told.
            Console.WriteLine($"[Broadcast] ✗ spv-gateway failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// For writes: NodeApi first (direct node), then all explorers in order.
    /// Never falls back to mock for broadcasts.
    /// </summary>
    /// <summary>
    /// What the gateway said about the last send, only when the gateway carried it; null otherwise (so a send through an explorer or the
    /// node shows nothing extra).
    /// </summary>
    public BroadcastReport? LastBroadcast { get; private set; }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        LastBroadcast = null;
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

        // The SPV gateway when it is first in line and active.
        if (GatewayActive && _gatewayPosition == GatewayPosition.First)
        {
            var viaGateway = await TryGatewayBroadcastAsync(rawTransaction);
            if (viaGateway is not null)
                return viaGateway;
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

        // The SPV gateway as a backup: relays to several peers when every explorer refused.
        if (GatewayActive && _gatewayPosition == GatewayPosition.Backup)
        {
            var viaGateway = await TryGatewayBroadcastAsync(rawTransaction);
            if (viaGateway is not null)
                return viaGateway;
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

    /// <summary>
    /// History: explorers only while node use is on. The node cannot list full history (it only sees transactions that still
    /// have unspent outputs), so it is never asked for it, not even as a last resort. The address's full list is cached under
    /// one key whatever page size the caller wants, and <paramref name="skip"/>/<paramref name="take"/> are applied after the
    /// read (spec 023, FR-003), so pages asking for different amounts share one fetch.
    /// </summary>
    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        var all = await CachedRead($"txs:{address}", TxHistoryCacheTtl,
            s => s.GetAddressTransactionsAsync(address, 0, int.MaxValue), NodeRouteClass.History);
        return all.Skip(skip).Take(take).ToList();
    }

    /// <summary>
    /// The gateway's one-pass wallet history (spec 023, FR-020), only while the gateway would answer history reads: active and first,
    /// or active as the backup with every explorer cooling down. Null otherwise, and the per-address path runs as before. The call
    /// keeps the gateway's time budget and reports to its monitor like any other gateway read.
    /// </summary>
    public IWalletHistorySource? TryGetWalletHistorySource()
    {
        if (ForceDemoMode || _gatewaySource is not IWalletHistorySource source || !GatewayActive)
            return null;
        var answersHistory = _gatewayPosition == GatewayPosition.First
            || (_explorers.Count > 0 && Enumerable.Range(0, _explorers.Count).All(IsOnCooldown));
        return answersHistory ? new GatewayWalletHistory(this, source) : null;
    }

    private sealed class GatewayWalletHistory(FallbackBlockchainService owner, IWalletHistorySource source) : IWalletHistorySource
    {
        public async Task<List<TransactionInfo>> GetWalletTransactionsAsync(IReadOnlyList<string> addresses)
        {
            try
            {
                var result = await source.GetWalletTransactionsAsync(addresses).WaitAsync(owner.GatewayReadBudget);
                owner._gatewayFailureSink?.ReportNodeReadSuccess();
                owner.IsDemoMode = false;
                owner.ActiveBackend = "spv-gateway";
                owner.Note("gateway", true);
                return result;
            }
            catch (GatewayDeclinedException)
            {
                throw; // not a failure: the gateway can't answer this one correctly
            }
            catch (Exception)
            {
                owner.Note("gateway", false);
                owner._gatewayFailureSink?.ReportNodeReadFailure();
                throw;
            }
        }
    }

    // ── Address summaries and history pages (spec 023) ──

    /// <summary>
    /// Routes to the first explorer that can give summaries and is not cooling down, with the same cooldown and counters as
    /// other address reads; with <paramref name="recordFailure"/> false (address discovery) a failure leaves no cooldown and no
    /// outage state, matching <see cref="HasAddressHistoryAsync"/>. Not cached: <see cref="WalletHistorySync"/> keeps the
    /// snapshot. Throws <see cref="SummaryUnavailableException"/> on regtest, in demo mode, while the gateway is active and
    /// first (so a gateway-first wallet's addresses never go to the explorer), when no explorer can give summaries, or when
    /// every one failed; the caller then uses the full-read path.
    /// </summary>
    public Task<Models.AddressSummary> GetAddressSummaryAsync(string address, bool recordFailure = true) =>
        RouteSummaryAsync(s => s.GetAddressSummaryAsync(address, recordFailure), recordFailure);

    /// <summary>A history page from the same explorer route as <see cref="GetAddressSummaryAsync"/>. Not cached.</summary>
    public Task<Models.AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId) =>
        RouteSummaryAsync(s => s.GetAddressHistoryPageAsync(address, afterTxId), recordFailure: true);

    private async Task<T> RouteSummaryAsync<T>(Func<IAddressSummarySource, Task<T>> call, bool recordFailure)
    {
        if (_isRegtest || ForceDemoMode)
            throw new SummaryUnavailableException("Address summaries are not available on this network.");
        if (GatewayActive && _gatewayPosition == GatewayPosition.First)
            throw new SummaryUnavailableException("The gateway answers this wallet's reads first.");

        Exception? lastError = null;
        var any = false;
        for (var i = 0; i < _explorers.Count; i++)
        {
            if (_explorers[i] is not IAddressSummarySource source) continue;
            any = true;
            if (IsOnCooldown(i)) continue;
            try
            {
                var result = await call(source);
                Note($"explorer-{i}", true);
                return result;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (recordFailure)
                {
                    Note($"explorer-{i}", false);
                    _explorerCooldowns[i] = DateTime.UtcNow + CooldownDuration;
                }
            }
        }

        throw new SummaryUnavailableException(
            any ? "No explorer could give this address's summary." : "No explorer can give address summaries.", lastError);
    }

    /// <summary>
    /// Explorers only: the node can't answer this. Unlike other reads it isn't cached, because a retry must see a payment made
    /// moments ago. A failure also sets no cooldown and no all-sources-failed state: address discovery makes many lookups at
    /// once, and a rate limit on those must not take the explorer away from balance and send, or report an outage.
    /// </summary>
    public async Task<bool> HasAddressHistoryAsync(string address)
    {
        if (ForceDemoMode && _isDevelopment && _mock != null)
            return await ((IBlockchainService)_mock).HasAddressHistoryAsync(address);

        Exception? lastError = null;
        if (!_isRegtest)
        {
            for (int i = 0; i < _explorers.Count; i++)
            {
                if (IsOnCooldown(i)) continue;
                try
                {
                    return await _explorers[i].HasAddressHistoryAsync(address);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
        }

        if (_isDevelopment && _mock != null)
            return await ((IBlockchainService)_mock).HasAddressHistoryAsync(address);

        throw new InvalidOperationException("No explorer could say whether this address has been used.", lastError);
    }

    /// <summary>
    /// Explorers first, then the node on regtest, with node use off, or when it is active, as for other address reads. Unlike
    /// <see cref="GetUtxosAsync(string)"/> it isn't cached, sets no cooldown or outage state, and throws when no source answers,
    /// so a coin can't look spent because a source was down (FR-007, FR-013).
    /// </summary>
    public async Task<List<UtxoInfo>> GetVerifiedUtxosAsync(string address)
    {
        if (ForceDemoMode && _isDevelopment && _mock != null)
            return await ((IBlockchainService)_mock).GetVerifiedUtxosAsync(address);

        Exception? lastError = null;
        if (GatewayActive && _gatewayPosition == GatewayPosition.First)
        {
            try
            {
                var viaGateway = await _gatewaySource!.GetVerifiedUtxosAsync(address).WaitAsync(GatewayReadBudget);
                _gatewayFailureSink?.ReportNodeReadSuccess();
                return viaGateway;
            }
            catch (Exception ex)
            {
                _gatewayFailureSink?.ReportNodeReadFailure();
                lastError = ex;
            }
        }
        if (!_isRegtest)
        {
            for (int i = 0; i < _explorers.Count; i++)
            {
                if (IsOnCooldown(i)) continue;
                try
                {
                    return await _explorers[i].GetVerifiedUtxosAsync(address);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
        }

        var gatewayAsked = false;
        if (LegacyOrder && GatewayActive && _gatewayPosition == GatewayPosition.Backup)
        {
            // Node use is off: the gateway has a readiness check and checked answers, the node has neither, so the gateway goes first.
            gatewayAsked = true;
            try
            {
                var viaGateway = await _gatewaySource!.GetVerifiedUtxosAsync(address).WaitAsync(GatewayReadBudget);
                _gatewayFailureSink?.ReportNodeReadSuccess();
                return viaGateway;
            }
            catch (Exception ex)
            {
                _gatewayFailureSink?.ReportNodeReadFailure();
                lastError = ex;
            }
        }

        if (LegacyOrder || _nodeState!.IsActive)
        {
            try
            {
                return await _nodeApi.GetVerifiedUtxosAsync(address);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (GatewayActive && _gatewayPosition == GatewayPosition.Backup && !gatewayAsked)
        {
            try
            {
                var viaGateway = await _gatewaySource!.GetVerifiedUtxosAsync(address).WaitAsync(GatewayReadBudget);
                _gatewayFailureSink?.ReportNodeReadSuccess();
                return viaGateway;
            }
            catch (Exception ex)
            {
                _gatewayFailureSink?.ReportNodeReadFailure();
                lastError = ex;
            }
        }

        // No demo-data fallback here, even in development: demo data never holds the coin being proven, so it would make a
        // real coin read as spent. Demo mode is only used when it was forced (above).
        throw new InvalidOperationException("No source could list this address's unspent coins.", lastError);
    }

    public Task<decimal> GetFeeRateAsync() =>
        CachedRead("feerate", FeeRateCacheTtl, s => s.GetFeeRateAsync(), NodeRouteClass.Fast);

    /// <summary>
    /// Price fetch — independent of explorers, uses its own HttpClient (no Polly retries).
    /// Cached for 5 minutes.
    /// </summary>
    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        var cacheKey = $"price:{fiatCurrency.ToLower()}";
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Pending is null && cached.ExpiresAt > DateTime.UtcNow)
                return (decimal)cached.Value!;
        }

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
                lock (_cacheGate)
                    _cache[cacheKey] = new CacheEntry { Value = price, ExpiresAt = DateTime.UtcNow.AddMinutes(5), Generation = _cacheGeneration };
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
    /// <remarks>
    /// Also drops reads still in flight and bumps the generation, so a read that started before this call can't fill the cache
    /// afterwards and a read that starts after it never joins one from before (spec 023, FR-023).
    /// </remarks>
    public void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _cache.Clear();
        }
    }

    /// <summary>
    /// A finished, unexpired answer is returned at once. Otherwise concurrent callers share one read (FR-002). A failed read is
    /// removed, never cached, so the next call asks again.
    /// </summary>
    private async Task<T> CachedRead<T>(string key, TimeSpan ttl, Func<IBlockchainService, Task<T>> call,
        NodeRouteClass routeClass = NodeRouteClass.Address)
    {
        CacheEntry entry;
        TaskCompletionSource<object?> owner;
        Task<object?>? shared = null;
        lock (_cacheGate)
        {
            entry = null!;
            owner = null!;
            if (_cache.TryGetValue(key, out var existing))
            {
                if (existing.Pending is null && DateTime.UtcNow < existing.ExpiresAt)
                    return (T)existing.Value!;
                if (existing.Pending is not null && existing.Generation == _cacheGeneration)
                    shared = existing.Pending;
            }

            if (shared is null)
            {
                owner = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry = new CacheEntry { Pending = owner.Task, Generation = _cacheGeneration };
                _cache[key] = entry;
            }
        }

        if (shared is not null)
            return (T)(await shared)!;

        try
        {
            var result = await TryRead(call, routeClass);
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    if (entry.Generation == _cacheGeneration)
                    {
                        entry.Value = result;
                        entry.ExpiresAt = DateTime.UtcNow + ttl;
                        entry.Pending = null;
                    }
                    else
                    {
                        _cache.Remove(key);
                    }
                }
            }
            owner.SetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _cache.Remove(key);
            }
            owner.SetException(ex);
            _ = owner.Task.Exception; // observed: a failure nobody else waited on is not an unobserved task exception
            throw;
        }
    }
}
