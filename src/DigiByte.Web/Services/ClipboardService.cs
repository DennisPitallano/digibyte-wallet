using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// One place for "copy to clipboard": writes the text, gives a short haptic tick where the device supports it,
/// and shows a "Copied" toast unless the caller already shows its own confirmation.
/// </summary>
public sealed class ClipboardService(IJSRuntime js, NotificationService notifications)
{
    /// <summary>Copies <paramref name="text"/>. Throws when the browser refuses, as the raw clipboard call does.</summary>
    public async Task CopyAsync(string text, bool toast = true, string message = "Copied")
    {
        await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
        try { await js.InvokeVoidAsync("domHelpers.haptic", 10); } catch { }
        if (toast) notifications.Success(message);
    }
}
