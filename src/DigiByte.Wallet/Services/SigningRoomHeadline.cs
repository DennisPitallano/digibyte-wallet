using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// One viewer-centric headline and at most one primary action for a signing room (specs/018-multisig-ux-polish, FR-003/FR-004/FR-019).
/// </summary>
public static class SigningRoomHeadline
{
    public static RoomHeadline For(RoomPhase phase, bool viewerSigned, bool viewerDeclined, int signedCount, int required,
        bool unreachable, bool isWatchOnly)
    {
        if (phase == RoomPhase.Broadcast)
            return new RoomHeadline("Sent", "The payment has been broadcast.", UxTone.Positive, PrimaryAction.View, "View");

        if (isWatchOnly)
            return new RoomHeadline("View only", "This device holds no co-signer key for this wallet.", UxTone.Neutral, PrimaryAction.None, null);

        var complete = phase == RoomPhase.Complete || (required > 0 && signedCount >= required);
        if (complete)
            return new RoomHeadline("Ready to broadcast", $"{signedCount} of {required} signatures collected.", UxTone.Positive, PrimaryAction.Broadcast, "Broadcast");

        if (unreachable)
            // Cancelling lives on the payment's own screen (specs/026-multisig-wallet-experience); the action opens it.
            return new RoomHeadline("Can't reach the required signatures", "Too many co-signers declined. You can cancel it from the payment's screen.",
                UxTone.Warning, PrimaryAction.GoToPending, "Open payment");

        if (viewerDeclined)
            return new RoomHeadline("You declined", "Others can still reach the required signatures.", UxTone.Neutral, PrimaryAction.None, null);

        if (viewerSigned)
        {
            var missing = Math.Max(1, required - signedCount);
            var title = missing == 1 ? "Waiting for 1 more signature" : $"Waiting for {missing} more signatures";
            return new RoomHeadline(title, "You have signed.", UxTone.Neutral, PrimaryAction.None, null);
        }

        return new RoomHeadline("Your signature is needed", $"{signedCount} of {required} signatures so far.", UxTone.Action, PrimaryAction.ReviewAndSign, "Review & sign");
    }
}
