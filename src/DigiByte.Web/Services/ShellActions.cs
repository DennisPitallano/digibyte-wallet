namespace DigiByte.Web.Services;

/// <summary>
/// Lets a page open the app-wide Scan and More that the menu owns. Home's own Scan and More buttons call this, so
/// they open exactly what the menu's buttons open: one scanner, one More list.
/// </summary>
public sealed class ShellActions
{
    public event Action? ScanRequested;
    public event Action? MoreRequested;

    public void OpenScan() => ScanRequested?.Invoke();
    public void OpenMore() => MoreRequested?.Invoke();
}
