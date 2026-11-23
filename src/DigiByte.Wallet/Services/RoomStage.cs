using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Where a signing room stands on its three-step tracker, Invite, Sign, Broadcast, and when the host is shown the
/// invite up front (specs/026-multisig-wallet-experience, FR-027 and FR-029). Everything here is worked out from what
/// the room already reports; nothing new is sent or received.
/// </summary>
public static class RoomStage
{
    /// <param name="phase">What the room itself reports.</param>
    /// <param name="presentOrSigned">Co-signers who are online or have already signed (see <see cref="PresentOrSigned"/>).</param>
    /// <param name="signed">Signatures collected so far.</param>
    /// <param name="required">Signatures the payment needs.</param>
    public static RoomStageKind For(RoomPhase phase, int presentOrSigned, int signed, int required)
    {
        if (phase == RoomPhase.Broadcast) return RoomStageKind.Done;
        if (phase == RoomPhase.Complete || (required > 0 && signed >= required)) return RoomStageKind.Broadcast;
        return presentOrSigned < required ? RoomStageKind.Invite : RoomStageKind.Sign;
    }

    /// <summary>
    /// True when the invite belongs right under the headline: the viewer opened the room, it is still collecting
    /// signatures, and too few co-signers are here to reach the threshold. Otherwise the invite is one folded row.
    /// </summary>
    public static bool InviteProminent(bool isHost, int presentOrSigned, int required, RoomPhase phase) =>
        isHost && phase == RoomPhase.Open && presentOrSigned < required;

    /// <summary>How many co-signers are in play: online now, or already signed. Each is counted once.</summary>
    public static int PresentOrSigned(IEnumerable<SignerCardModel> cards) =>
        cards.Count(c => c.Status == SignerStatus.Signed || c.Presence == SignerPresence.Online);

    /// <summary>The tracker in words, for assistive technology.</summary>
    public static string Describe(RoomStageKind stage) => stage switch
    {
        RoomStageKind.Invite => "Step 1 of 3: Invite",
        RoomStageKind.Sign => "Step 2 of 3: Sign",
        RoomStageKind.Broadcast => "Step 3 of 3: Broadcast",
        _ => "All 3 steps done: payment sent",
    };
}
