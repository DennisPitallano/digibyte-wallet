namespace DigiByte.Web.Services;

/// <summary>
/// Hand-overs between the menu's app-wide Scan and the page a code belongs to.
/// </summary>
public sealed class ShellActions
{
    // A redeem script the app-wide Scan read, waiting for the Import multisig page. In memory only and taken once:
    // it is not put in an address (it can be 2,000 characters) and not stored.
    private string? _redeemScriptHex;

    /// <summary>Raised when a scanned redeem script arrives, for an Import multisig page that is already open.</summary>
    public event Action? RedeemScriptArrived;

    public void OfferRedeemScript(string redeemScriptHex)
    {
        _redeemScriptHex = redeemScriptHex;
        RedeemScriptArrived?.Invoke();
    }

    /// <summary>
    /// Raised when a page asks for the shared-wallet switcher (the context line on a shared wallet's pages). The menu
    /// owns the one switcher and opens it: a sheet opened from inside the page would sit under the menu.
    /// </summary>
    public event Action? SwitcherRequested;

    public void RequestSwitcher() => SwitcherRequested?.Invoke();

    /// <summary>The waiting redeem script, or null. Clears it.</summary>
    public string? TakeRedeemScript()
    {
        var hex = _redeemScriptHex;
        _redeemScriptHex = null;
        return hex;
    }
}
