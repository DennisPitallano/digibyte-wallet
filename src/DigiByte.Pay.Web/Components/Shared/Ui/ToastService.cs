namespace DigiByte.Pay.Web.Components.Shared.Ui;

public enum ToastTone { Success, Info, Warning, Error }

/// <summary>One notice on screen.</summary>
public sealed record ToastMessage(Guid Id, ToastTone Tone, string Text, string? ActionLabel, Func<Task>? Action, DateTime ShownAt);

/// <summary>
/// Wallet-style notices for DigiPay (specs/032-digipay-ui-refresh/data-model.md §6).
/// Success and info notices disappear after 3 seconds; warnings and errors stay until
/// dismissed or acted on. The same text shown again within 2 seconds is merged into the
/// existing notice instead of stacking. Never pass exception text here.
/// Scoped: one per circuit (one per browser tab).
/// </summary>
public sealed class ToastService : IDisposable
{
    public static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(2);

    private readonly List<ToastMessage> _items = new();
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _disposed = new();

    public ToastService() : this(TimeProvider.System) { }

    public ToastService(TimeProvider time) => _time = time;

    /// <summary>Raised whenever the list of notices changes.</summary>
    public event Action? Changed;

    public IReadOnlyList<ToastMessage> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }

    public void Success(string text) => Show(ToastTone.Success, text);
    public void Info(string text) => Show(ToastTone.Info, text);
    public void Warning(string text, string? actionLabel = null, Func<Task>? action = null) => Show(ToastTone.Warning, text, actionLabel, action);
    public void Error(string text, string? actionLabel = null, Func<Task>? action = null) => Show(ToastTone.Error, text, actionLabel, action);

    public Guid Show(ToastTone tone, string text, string? actionLabel = null, Func<Task>? action = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A notice needs text.", nameof(text));

        var now = _time.GetUtcNow().UtcDateTime;
        ToastMessage message;
        lock (_gate)
        {
            var existing = _items.FirstOrDefault(t => t.Tone == tone && t.Text == text && now - t.ShownAt < MergeWindow);
            if (existing is not null)
            {
                // Merge: keep one notice and restart its timer.
                message = existing with { ShownAt = now };
                _items[_items.IndexOf(existing)] = message;
            }
            else
            {
                message = new ToastMessage(Guid.NewGuid(), tone, text, actionLabel, action, now);
                _items.Add(message);
            }
        }

        Changed?.Invoke();
        if (tone is ToastTone.Success or ToastTone.Info) _ = AutoDismissAsync(message.Id, message.ShownAt);
        return message.Id;
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate) removed = _items.RemoveAll(t => t.Id == id) > 0;
        if (removed) Changed?.Invoke();
    }

    /// <summary>Runs a notice's action (for example Retry) and removes the notice.</summary>
    public async Task RunActionAsync(Guid id)
    {
        ToastMessage? message;
        lock (_gate) message = _items.FirstOrDefault(t => t.Id == id);
        Dismiss(id);
        if (message?.Action is not null) await message.Action();
    }

    private async Task AutoDismissAsync(Guid id, DateTime shownAt)
    {
        try { await Task.Delay(AutoDismissAfter, _time, _disposed.Token); }
        catch (OperationCanceledException) { return; }

        bool removed;
        lock (_gate)
        {
            // A merged notice restarted its timer; only the latest timer removes it.
            removed = _items.RemoveAll(t => t.Id == id && t.ShownAt == shownAt) > 0;
        }
        if (removed) Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }
}
