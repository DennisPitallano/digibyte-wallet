namespace DigiByte.Wallet.Services;

/// <summary>
/// Decides whether our own node may be used as a blockchain source (specs/005-wallet-own-node-source).
/// Promotion is slow (the node must stay ready for the stable period) and demotion is immediate (the next
/// check that is not ready, or three failed node reads in a row). It never throws and never blocks a screen:
/// <see cref="CheckAsync"/> is meant to be called from a background loop.
/// </summary>
public sealed class NodeSourceMonitor : INodeSourceState, INodeReadFailureSink
{
    /// <summary>Consecutive failed node reads, while active, that demote the node without waiting for the next check.</summary>
    public const int ReadFailuresToDemote = 3;

    private readonly NodeSourceOptions _options;
    private readonly INodeHealthSource _health;
    private readonly IExplorerHeightSource _explorer;
    private readonly IClock _clock;
    private readonly object _gate = new();

    private NodeSourceState _state;
    private NodeNotReadyReason _reason;
    private DateTime? _readySince;
    private int _readFailures;

    public NodeSourceMonitor(NodeSourceOptions options, INodeHealthSource health, IExplorerHeightSource explorer, IClock clock)
    {
        _options = options;
        _health = health;
        _explorer = explorer;
        _clock = clock;
        _state = options.Enabled ? NodeSourceState.Unavailable : NodeSourceState.Disabled;
        _reason = options.Enabled ? NodeNotReadyReason.None : NodeNotReadyReason.Disabled;
    }

    public NodeSourceState State { get { lock (_gate) return _state; } }
    public NodeNotReadyReason Reason { get { lock (_gate) return _reason; } }
    public bool IsActive => State == NodeSourceState.Active;
    public bool NodeWasActive { get; private set; }
    public NodeReadinessSnapshot? LastSnapshot { get; private set; }

    public event Action? Changed;

    /// <summary>Runs one readiness check: the health report and an explorer height are fetched in parallel, within a fixed time budget.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_options.HealthTimeout + _options.ExplorerBudget);

            var healthTask = SafeHealthAsync(budget.Token);
            var heightTask = SafeHeightAsync(budget.Token);
            await Task.WhenAll(healthTask, heightTask);

            var snapshot = NodeReadiness.Evaluate(healthTask.Result, heightTask.Result, _options.HeightTolerance, _clock.UtcNow);
            Apply(snapshot);
        }
        catch (Exception)
        {
            // A check must never throw into the background loop; treat anything unexpected as the node being unavailable.
            Apply(NodeReadiness.Evaluate(null, null, _options.HeightTolerance, _clock.UtcNow));
        }
    }

    /// <summary>A node read failed (error, timeout or unreadable answer). Three in a row while active demote the node at once.</summary>
    public void ReportNodeReadFailure()
    {
        var changed = false;
        lock (_gate)
        {
            if (_state != NodeSourceState.Active)
                return;
            if (++_readFailures < ReadFailuresToDemote)
                return;

            _readFailures = 0;
            _readySince = null;
            changed = SetState(NodeSourceState.NotReady, NodeNotReadyReason.ReadFailures);
        }
        if (changed)
            Changed?.Invoke();
    }

    public void ReportNodeReadSuccess()
    {
        lock (_gate)
            _readFailures = 0;
    }

    // ---------------------------------------------------------------------------------------------------------

    private void Apply(NodeReadinessSnapshot snapshot)
    {
        var changed = false;
        lock (_gate)
        {
            LastSnapshot = snapshot;

            if (snapshot.Inconclusive)
                return;   // no explorer height: do not promote, do not demote

            if (!snapshot.Ready)
            {
                _readySince = null;
                _readFailures = 0;
                changed = SetState(NodeReadiness.StateFor(snapshot.Reason), snapshot.Reason);
            }
            else if (_state == NodeSourceState.Active)
            {
                _readFailures = 0;   // still ready: nothing to change
            }
            else if (_state == NodeSourceState.Stabilizing)
            {
                if (_readySince is { } since && snapshot.CheckedAt - since >= _options.StablePeriod)
                {
                    NodeWasActive = true;
                    _readFailures = 0;
                    changed = SetState(NodeSourceState.Active, NodeNotReadyReason.None);
                }
            }
            else
            {
                _readySince = snapshot.CheckedAt;
                changed = SetState(NodeSourceState.Stabilizing, NodeNotReadyReason.None);
            }
        }
        if (changed)
            Changed?.Invoke();
    }

    /// <summary>Must be called with the gate held. Returns true when the state or reason changed.</summary>
    private bool SetState(NodeSourceState state, NodeNotReadyReason reason)
    {
        if (_state == state && _reason == reason)
            return false;
        _state = state;
        _reason = reason;
        return true;
    }

    private async Task<NodeHealthResult> SafeHealthAsync(CancellationToken ct)
    {
        try { return await _health.GetAsync(ct); }
        catch (Exception) { return NodeHealthResult.Unreachable; }
    }

    private async Task<int?> SafeHeightAsync(CancellationToken ct)
    {
        try { return await _explorer.GetHeightAsync(ct); }
        catch (Exception) { return null; }
    }
}
