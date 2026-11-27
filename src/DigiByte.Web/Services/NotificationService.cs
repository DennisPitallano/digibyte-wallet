namespace DigiByte.Web.Services;

/// <summary>
/// In-app toast notification system.
/// </summary>
public class NotificationService
{
    private readonly List<ToastNotification> _active = [];

    public IReadOnlyList<ToastNotification> Active => _active;
    public event Action? OnChange;

    /// <param name="onTap">Optional: tapping the message does this and dismisses it (spec 036, FR-012).</param>
    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000, Action? onTap = null)
    {
        // The same message is never stacked: a repeat while it is still on screen is dropped.
        if (_active.Any(t => t.Message == message && t.Type == type)) return;

        var toast = new ToastNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Message = message,
            Type = type,
            CreatedAt = DateTime.Now,
            DurationMs = durationMs,
            OnTap = onTap,
        };

        _active.Add(toast);
        OnChange?.Invoke();

        // A duration of zero or less means the message stays until the person dismisses it.
        if (durationMs <= 0) return;

        _ = Task.Delay(durationMs).ContinueWith(_ =>
        {
            _active.Remove(toast);
            OnChange?.Invoke();
        });
    }

    /// <summary>
    /// A message that stays until it is dismissed, for a failure the person must not miss
    /// (specs/026-multisig-wallet-experience, FR-024).
    /// </summary>
    public void Sticky(string message, ToastType type = ToastType.Error) => Show(message, type, 0);

    public void Success(string message) => Show(message, ToastType.Success);
    public void Error(string message) => Show(message, ToastType.Error, 5000);
    public void Warning(string message) => Show(message, ToastType.Warning, 4000);
    public void Info(string message) => Show(message, ToastType.Info);

    public void Dismiss(string id)
    {
        _active.RemoveAll(t => t.Id == id);
        OnChange?.Invoke();
    }
}

public class ToastNotification
{
    public required string Id { get; init; }
    public required string Message { get; init; }
    public required ToastType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
    public int DurationMs { get; init; } = 3000;

    /// <summary>What tapping the message does, if anything.</summary>
    public Action? OnTap { get; init; }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}
