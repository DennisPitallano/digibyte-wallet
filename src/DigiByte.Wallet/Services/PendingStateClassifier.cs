using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Turns a saved proposal plus the viewer into one shared state (specs/018-multisig-ux-polish, FR-010).</summary>
public static class PendingStateClassifier
{
    /// <param name="localKey">The viewer's co-signer key, or null for a watch-only viewer.</param>
    /// <param name="hasLiveRoom">True when a signing room is open for this proposal.</param>
    public static MultisigState Classify(PendingMultisigTransaction pending, string? localKey, bool hasLiveRoom)
    {
        switch (pending.Status)
        {
            case PendingTxStatus.Broadcast: return MultisigState.Sent;
            case PendingTxStatus.Cancelled: return MultisigState.CancelledOrExpired;
            case PendingTxStatus.Failed: return MultisigState.Failed;
        }

        if (hasLiveRoom) return MultisigState.LiveRoom;
        if (pending.Status == PendingTxStatus.ReadyToFinalize) return MultisigState.ReadyToBroadcast;

        if (string.IsNullOrEmpty(localKey)) return MultisigState.WaitingForOthers;
        return HasSigned(pending, localKey) ? MultisigState.YouSigned : MultisigState.NeedsYourSignature;
    }

    internal static bool HasSigned(PendingMultisigTransaction pending, string localKey) =>
        pending.SignedBy.Contains(localKey, StringComparer.OrdinalIgnoreCase);
}
