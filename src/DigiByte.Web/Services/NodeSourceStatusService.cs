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

    public NodeSourceStatusService(INodeSourceState state, FallbackBlockchainService fallback)
    {
        _state = state;
        _fallback = fallback;
        _state.Changed += Raise;
        _fallback.AllSourcesFailedChanged += Raise;
    }

    public NodeSourceStatusView View =>
        NodeSourceStatus.Compute(_state.State, _state.Reason, _state.NodeWasActive, _fallback.AllSourcesFailed);

    /// <summary>True while a read has failed on every source. Pages use it to explain why sending is unavailable.</summary>
    public bool AllSourcesFailed => _fallback.AllSourcesFailed;

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        _state.Changed -= Raise;
        _fallback.AllSourcesFailedChanged -= Raise;
    }
}
