namespace DigiByte.Wallet.Services;

/// <summary>Where the wallet stands with our own node (specs/005-wallet-own-node-source).</summary>
public enum NodeSourceState
{
    /// <summary>Node use is switched off: the wallet behaves as it did before the feature.</summary>
    Disabled,
    /// <summary>The node could not be reached or its report could not be read.</summary>
    Unavailable,
    /// <summary>The node answered but is not ready (starting, syncing, out of step, ...).</summary>
    NotReady,
    /// <summary>The node reported ready and must stay ready for the stable period before it is used.</summary>
    Stabilizing,
    /// <summary>The node has been ready for the stable period and is used according to the route classes.</summary>
    Active,
}

public enum NodeNotReadyReason
{
    None,
    Disabled,
    Unreachable,
    Starting,
    Syncing,
    Unhealthy,
    Txindex,
    Version,
    HeightDrift,
    Malformed,
    ReadFailures,
}

/// <summary>
/// How a call is routed while the node is active. <c>Fast</c> calls try the node first; <c>Address</c> calls (the node answers
/// by scanning its whole set of spendable outputs, about 14 s) try the explorers first and use the node only as a fallback;
/// <c>History</c> never uses the node (it cannot list full address history).
/// </summary>
public enum NodeRouteClass
{
    Fast,
    Address,
    History,
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>What one request to the node's <c>/api/health</c> returned, before it is judged.</summary>
public sealed record NodeHealthResult(
    bool Reachable,
    bool Malformed,
    string? Status = null,
    int? Blocks = null,
    int? Headers = null,
    int? Lag = null,
    int? Peers = null,
    bool Txindex = false,
    bool? VersionOk = null,
    string? NodeVersion = null,
    string? RecommendedVersion = null)
{
    public static NodeHealthResult Unreachable { get; } = new(false, false);
    public static NodeHealthResult Unreadable { get; } = new(true, true);
}

/// <summary>Reads the node's health report. Implementations never throw.</summary>
public interface INodeHealthSource
{
    Task<NodeHealthResult> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Gets the chain height from a public explorer. Returns null when no explorer answered; never throws.</summary>
public interface IExplorerHeightSource
{
    Task<int?> GetHeightAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lets the fallback service tell the monitor about node reads, so repeated failures demote the node at once.</summary>
public interface INodeReadFailureSink
{
    void ReportNodeReadFailure();
    void ReportNodeReadSuccess();
}

/// <summary>The outcome of judging one health report (and an explorer height) against the ready predicate.</summary>
public sealed record NodeReadinessSnapshot(
    DateTime CheckedAt,
    NodeHealthResult? Health,
    int? ExplorerHeight,
    int? HeightDelta,
    bool Ready,
    bool Inconclusive,
    NodeNotReadyReason Reason);

/// <summary>Read-only view of the monitor used by the fallback service and the UI.</summary>
public interface INodeSourceState
{
    NodeSourceState State { get; }
    NodeNotReadyReason Reason { get; }
    bool IsActive { get; }
    /// <summary>True once the node has been <see cref="NodeSourceState.Active"/> at any time in this session.</summary>
    bool NodeWasActive { get; }
    NodeReadinessSnapshot? LastSnapshot { get; }
    event Action? Changed;
}
