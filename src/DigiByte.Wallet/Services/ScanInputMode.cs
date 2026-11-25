namespace DigiByte.Wallet.Services;

/// <summary>The way into the scan panel that opens first.</summary>
public enum ScanEntry
{
    /// <summary>A field to paste or type a code into, plus picture paste and drop.</summary>
    Paste,
    /// <summary>The camera viewfinder.</summary>
    Camera,
}

/// <summary>
/// What the browser says about this device, read without asking for any permission.
/// <paramref name="HasCamera"/> is null when the browser would not say (Safari before camera permission, for one).
/// </summary>
public sealed record DeviceFacts(
    bool FinePointer,
    bool? HasCamera,
    bool CanCaptureScreen,
    bool CanReadClipboardText,
    bool CanReadClipboardImages);

/// <summary>
/// Chooses paste or camera first (specs/030-desktop-paste-scan, FR-001 to FR-004). On a desktop the code is almost
/// always already on the screen or copied, so paste comes first; on a phone the camera does.
/// </summary>
public static class ScanInputMode
{
    private const string PasteWord = "paste";
    private const string CameraWord = "camera";

    /// <summary>
    /// The person's own last choice on this device wins. Otherwise paste first on a mouse or trackpad device, or
    /// one known to have no camera; camera first on a touch device, including one whose camera is unknown, which
    /// keeps today's behaviour there.
    /// </summary>
    public static ScanEntry Decide(DeviceFacts facts, ScanEntry? remembered)
    {
        if (remembered is { } choice) return choice;
        if (facts.FinePointer) return ScanEntry.Paste;
        if (facts.HasCamera == false) return ScanEntry.Paste;
        return ScanEntry.Camera;
    }

    /// <summary>Reads a stored choice. Only the exact words written by <see cref="ToStored"/> count.</summary>
    public static ScanEntry? ParseRemembered(string? stored) => stored switch
    {
        PasteWord => ScanEntry.Paste,
        CameraWord => ScanEntry.Camera,
        _ => null,
    };

    /// <summary>The word stored for a choice.</summary>
    public static string ToStored(ScanEntry mode) => mode == ScanEntry.Paste ? PasteWord : CameraWord;
}
