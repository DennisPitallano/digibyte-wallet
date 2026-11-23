using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The one button a pending payment's card shows (specs/026-multisig-wallet-experience, FR-019). Nothing here signs,
/// broadcasts or retries: every button opens the payment's own screen, except "Open room", which opens its live room
/// (FR-019a).
/// </summary>
public static class PendingCardActions
{
    private static readonly PendingCardButton NoButton = new(PendingCardAction.None, null);
    private static readonly PendingCardButton ViewButton = new(PendingCardAction.View, "View");

    /// <param name="state">From <see cref="PendingStateClassifier"/>.</param>
    /// <param name="coinsSpent">Some of the coins this payment uses are no longer available, so it cannot go through.</param>
    /// <param name="isViewOnly">This device holds no co-signer key for the shared wallet.</param>
    public static PendingCardButton For(MultisigState state, bool coinsSpent, bool isViewOnly)
    {
        switch (state)
        {
            case MultisigState.Sent:
            case MultisigState.CancelledOrExpired:
                return NoButton;

            // A failed broadcast is retried as it is; whether its coins are still there is the network's answer.
            case MultisigState.Failed:
                return new(PendingCardAction.ReviewAndRetry, "Review & retry");

            // A room that is open can always be gone back to: it says itself when the payment cannot complete.
            case MultisigState.LiveRoom:
                return isViewOnly ? ViewButton : new(PendingCardAction.OpenRoom, "Open room");
        }

        if (coinsSpent) return new(PendingCardAction.ReviewAndCancel, "Review & cancel");

        return state switch
        {
            MultisigState.ReadyToBroadcast => new(PendingCardAction.ReviewAndBroadcast, "Review & broadcast"),
            MultisigState.NeedsYourSignature when !isViewOnly => new(PendingCardAction.ReviewAndSign, "Review & sign"),
            MultisigState.YouSigned when !isViewOnly => new(PendingCardAction.GetSignatures, "Get signatures"),
            _ => ViewButton,
        };
    }

    /// <summary>True for every button that leads to the payment's own screen (all but "Open room").</summary>
    public static bool OpensPaymentScreen(PendingCardAction action) =>
        action is not (PendingCardAction.None or PendingCardAction.OpenRoom);
}
