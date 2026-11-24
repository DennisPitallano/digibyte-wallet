using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// Carries a payment link's address and amount from <c>/pay</c> to Send (specs/030-desktop-paste-scan, FR-021).
/// In memory only and one-shot, like <see cref="ProposalHandoff"/>: the payment is never stored and never put in a
/// URL, so a reload of Send cannot send it to the web host. A new tab or a reload starts empty.
/// </summary>
public sealed class PaymentHandoff
{
    private PaymentLinkData? _waiting;

    /// <summary>Holds a payment for Send, replacing any that was waiting.</summary>
    public void Set(PaymentLinkData link) => _waiting = link;

    /// <summary>Returns the waiting payment and clears it, or null when none is waiting.</summary>
    public PaymentLinkData? Take()
    {
        var link = _waiting;
        _waiting = null;
        return link;
    }
}
