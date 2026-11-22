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

    /// <summary>The waiting redeem script, or null. Clears it.</summary>
    public string? TakeRedeemScript()
    {
        var hex = _redeemScriptHex;
        _redeemScriptHex = null;
        return hex;
    }
}
