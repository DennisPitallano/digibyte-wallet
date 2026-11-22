using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Splits a shared wallet's saved payments the way the Pending list shows them: what needs the viewer, what waits
/// on others, and what was cancelled, each newest first (specs/026-multisig-wallet-experience, FR-018). A payment
/// that was sent is in no group: it lives in Activity (FR-018a).
/// "Needs you" is the same set <see cref="AttentionCounter"/> counts, so the list and the menu's number agree.
/// </summary>
public static class PendingGrouping
{
    /// <param name="localKey">The viewer's co-signer key, or null or empty for a view-only copy.</param>
    public static PendingGroups Group(IEnumerable<PendingMultisigTransaction> pending, string? localKey)
    {
        var needsYou = new List<PendingMultisigTransaction>();
        var waiting = new List<PendingMultisigTransaction>();
        var cancelled = new List<PendingMultisigTransaction>();

        // OrderByDescending is stable: payments created at the same moment keep the order they came in.
        foreach (var item in pending.OrderByDescending(p => p.CreatedAt))
        {
            switch (item.Status)
            {
                case PendingTxStatus.Broadcast:
                    break;
                case PendingTxStatus.Cancelled:
                    cancelled.Add(item);
                    break;
                // Anyone holding the payment can broadcast or retry it, with or without a key of their own.
                case PendingTxStatus.ReadyToFinalize:
                case PendingTxStatus.Failed:
                    needsYou.Add(item);
                    break;
                default:
                    var canSign = !string.IsNullOrEmpty(localKey) && !PendingStateClassifier.HasSigned(item, localKey);
                    (canSign ? needsYou : waiting).Add(item);
                    break;
            }
        }

        return new PendingGroups(needsYou, waiting, cancelled);
    }
}
