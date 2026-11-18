namespace DigiByte.Web.Services;

/// <summary>
/// Carries a scanned proposal from one multisig's pending page to another's when the user accepts the "open that wallet"
/// offer (specs/022-multisig-proposal-scan-import, research R5). In memory only and one-shot: the proposal is never stored
/// and never put in a URL, and is handed over only to the wallet it was meant for.
/// </summary>
public sealed class ProposalHandoff
{
    private string? _walletId;
    private string? _psbtBase64;

    public void Set(string walletId, string psbtBase64)
    {
        _walletId = walletId;
        _psbtBase64 = psbtBase64;
    }

    /// <summary>Returns the waiting proposal if it was meant for <paramref name="walletId"/> and clears it; otherwise null (and keeps it).</summary>
    public string? Take(string walletId)
    {
        if (_psbtBase64 is null || !string.Equals(_walletId, walletId, StringComparison.Ordinal))
            return null;
        var psbt = _psbtBase64;
        _walletId = null;
        _psbtBase64 = null;
        return psbt;
    }
}
