namespace DigiByte.Web.Services;

/// <summary>
/// Lets a page take over the whole screen. The signing room hides the bottom menu while a room is live or showing
/// its result, so its main button owns the bottom edge (specs/026-multisig-wallet-experience, FR-028). The room's
/// join screen shares the same address and keeps the menu, which is why the address alone cannot decide this.
/// </summary>
public sealed class ShellChrome
{
    /// <summary>True while the bottom menu must not be shown.</summary>
    public bool HideNav { get; private set; }

    /// <summary>Raised when <see cref="HideNav"/> changes, so the layout draws again.</summary>
    public event Action? Changed;

    public void SetHideNav(bool hide)
    {
        if (HideNav == hide) return;
        HideNav = hide;
        Changed?.Invoke();
    }
}
