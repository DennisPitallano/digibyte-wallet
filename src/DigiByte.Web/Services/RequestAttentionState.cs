namespace DigiByte.Web.Services;

/// <summary>
/// How many paid payment requests the person has not opened yet, for the Request tab's count (spec 036, FR-014a).
/// It goes down as each one is opened. Comes from requests saved on this device; asks the network nothing.
/// </summary>
public sealed class RequestAttentionState : IDisposable
{
    private readonly RequestWatcher _watcher;

    public RequestAttentionState(RequestWatcher watcher)
    {
        _watcher = watcher;
        _watcher.Changed += OnWatcherChanged;
    }

    /// <summary>Paid or overpaid requests not opened since they were paid.</summary>
    public int Count => _watcher.PaidUnopened.Count;

    /// <summary>The count may have changed.</summary>
    public event Action? Changed;

    private void OnWatcherChanged() => Changed?.Invoke();

    public void Dispose() => _watcher.Changed -= OnWatcherChanged;
}
