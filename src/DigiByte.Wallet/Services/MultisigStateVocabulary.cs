using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The one set of labels, icons and primary actions for every multisig state, so the same state reads the same on
/// every screen (specs/018-multisig-ux-polish, FR-010). Icon values are short keys the UI maps to drawings.
/// </summary>
public static class MultisigStateVocabulary
{
    public static StateInfo For(MultisigState state) => state switch
    {
        MultisigState.NeedsYourSignature => new("Needs your signature", "pen", UxTone.Action, PrimaryAction.Sign, "Sign"),
        MultisigState.YouSigned => new("You signed", "check", UxTone.Neutral, PrimaryAction.View, "View"),
        MultisigState.WaitingForOthers => new("Waiting for others", "clock", UxTone.Neutral, PrimaryAction.View, "View"),
        MultisigState.ReadyToBroadcast => new("Ready to broadcast", "send", UxTone.Positive, PrimaryAction.Broadcast, "Broadcast"),
        MultisigState.LiveRoom => new("Live room", "live", UxTone.Action, PrimaryAction.OpenRoom, "Open room"),
        MultisigState.Sent => new("Sent", "check-circle", UxTone.Positive, PrimaryAction.None, null),
        MultisigState.CancelledOrExpired => new("Cancelled or expired", "x", UxTone.Neutral, PrimaryAction.None, null),
        MultisigState.Failed => new("Failed", "alert", UxTone.Warning, PrimaryAction.None, null),
        _ => new("Unknown", "clock", UxTone.Neutral, PrimaryAction.None, null),
    };
}
