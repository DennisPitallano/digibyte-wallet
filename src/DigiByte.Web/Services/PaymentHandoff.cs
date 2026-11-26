using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// Carries a payment link's address and amount from <c>/pay</c> to Send (specs/030-desktop-paste-scan, FR-021).
/// In memory only and one-shot, like <see cref="ProposalHandoff"/>: the payment is never stored and never put in a
/// URL, so a reload of Send cannot send it to the web host. A new tab or a reload starts empty.
/// The menu's Paste / Scan hands every payment over this way too, to the personal Send or to an open shared wallet's
/// Send (specs/032-desktop-receive-everywhere, FR-001 to FR-004), so a payment names the Send it is for.
/// </summary>
public sealed class PaymentHandoff
{
    private PaymentLinkData? _waiting;
    private string? _for;
    private SiteReturn? _returnTo;

    /// <summary>
    /// Holds a payment for the personal Send (<paramref name="sharedWalletId"/> null or blank) or for that shared
    /// wallet's Send, replacing any that was waiting. <paramref name="returnTo"/> is the checked "return to the
    /// site" address a payment link came with (<c>/pay?return=…</c>), offered once the payment is sent.
    /// </summary>
    public void Set(PaymentLinkData link, string? sharedWalletId = null, SiteReturn? returnTo = null)
    {
        _waiting = link;
        _for = Normalize(sharedWalletId);
        _returnTo = returnTo;
    }

    /// <summary>
    /// Returns the waiting payment when it is for this Send (null: the personal one), or null. What was waiting is
    /// cleared either way, so a payment meant for another Send never turns up later.
    /// </summary>
    public PaymentLinkData? Take(string? sharedWalletId = null) => Take(sharedWalletId, out _);

    /// <summary>
    /// <see cref="Take(string?)"/>, also giving the return address the payment came with (null when it had none or
    /// the payment is not for this Send). It travels with the payment, so it survives Unlock the same way.
    /// </summary>
    public PaymentLinkData? Take(string? sharedWalletId, out SiteReturn? returnTo)
    {
        var link = _waiting;
        var mine = link is not null && string.Equals(_for, Normalize(sharedWalletId), StringComparison.Ordinal);
        returnTo = mine ? _returnTo : null;
        _waiting = null;
        _for = null;
        _returnTo = null;
        return mine ? link : null;
    }

    private static string? Normalize(string? id) => string.IsNullOrWhiteSpace(id) ? null : id;
}
