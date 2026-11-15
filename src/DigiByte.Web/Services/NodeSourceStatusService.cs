using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// What the UI shows about our own node: combines the monitor state with the fallback service's "every source failed" flag
/// and the wording in <see cref="NodeSourceStatus"/>. Raises <see cref="Changed"/> from background threads; components must
/// marshal to the UI thread (<c>InvokeAsync</c>).
/// </summary>
public sealed class NodeSourceStatusService : IDisposable
{
    private readonly INodeSourceState _state;
    private readonly FallbackBlockchainService _fallback;
    private readonly INodeSourceState? _gateway;

    public NodeSourceStatusService(INodeSourceState state, FallbackBlockchainService fallback, INodeSourceState? gateway = null)
    {
        _state = state;
        _fallback = fallback;
        _gateway = gateway;
        _state.Changed += Raise;
        if (_gateway is not null)
            _gateway.Changed += Raise;
        _fallback.AllSourcesFailedChanged += Raise;
    }

    public NodeSourceStatusView View
    {
        get
        {
            var view = NodeSourceStatus.Compute(_state.State, _state.Reason, _state.NodeWasActive, _fallback.AllSourcesFailed);
            return _gateway is null
                ? view
                : NodeSourceStatus.WithGateway(view, _gateway.State, _gateway.Reason, _fallback.ActiveBackend == "spv-gateway");
        }
    }

    /// <summary>True while a read has failed on every source. Pages use it to explain why sending is unavailable.</summary>
    public bool AllSourcesFailed => _fallback.AllSourcesFailed;

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        _state.Changed -= Raise;
        if (_gateway is not null)
            _gateway.Changed -= Raise;
        _fallback.AllSourcesFailedChanged -= Raise;
    }
}
