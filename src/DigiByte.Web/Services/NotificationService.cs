namespace DigiByte.Web.Services;

/// <summary>
/// In-app toast notification system.
/// </summary>
public class NotificationService
{
    private readonly List<ToastNotification> _active = [];

    public IReadOnlyList<ToastNotification> Active => _active;
    public event Action? OnChange;

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        var toast = new ToastNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Message = message,
            Type = type,
            CreatedAt = DateTime.Now,
            DurationMs = durationMs,
        };

        _active.Add(toast);
        OnChange?.Invoke();

        _ = Task.Delay(durationMs).ContinueWith(_ =>
        {
            _active.Remove(toast);
            OnChange?.Invoke();
        });
    }

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
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}
