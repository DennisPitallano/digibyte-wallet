using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The one status line on a shared wallet's card: the most urgent thing about its saved payments
/// (specs/026-multisig-wallet-experience, FR-012). In order: needs your signature, ready to broadcast, a broadcast
/// that failed, a live room, waiting for others. Worked out from what is saved on this device; no network call.
/// </summary>
public static class WalletCardStatus
{
    private static readonly WalletCardStatusLine Nothing = new(WalletCardStatusKind.None, 0);

    /// <param name="pending">The shared wallet's saved payments.</param>
    /// <param name="localKey">The viewer's co-signer key, or null or empty for a view-only copy.</param>
    /// <param name="liveRoomTxId">The payment of the signing room this device is in, if any. A room the device has
    /// not joined is unknown to it, so it can never be shown.</param>
    /// <param name="coinsSpent">Says, by payment id, whether its coins are known to be already spent. Such a payment
    /// cannot go through and says nothing about its wallet here. Null when nothing is known.</param>
    public static WalletCardStatusLine For(IEnumerable<PendingMultisigTransaction> pending, string? localKey, string? liveRoomTxId,
        Func<string, bool>? coinsSpent = null)
    {
        var items = pending
            .Where(p => p.Status is not (PendingTxStatus.AwaitingSignatures or PendingTxStatus.ReadyToFinalize) || coinsSpent?.Invoke(p.Id) != true)
            .ToList();
        var open = items.Where(p => p.Status == PendingTxStatus.AwaitingSignatures).ToList();

        var needsSignature = string.IsNullOrEmpty(localKey) ? 0 : open.Count(p => !PendingStateClassifier.HasSigned(p, localKey));
        if (needsSignature > 0) return new(WalletCardStatusKind.NeedsSignature, needsSignature);

        var ready = items.Count(p => p.Status == PendingTxStatus.ReadyToFinalize);
        if (ready > 0) return new(WalletCardStatusKind.ReadyToBroadcast, ready);

        var failed = items.Count(p => p.Status == PendingTxStatus.Failed);
        if (failed > 0) return new(WalletCardStatusKind.FailedBroadcast, failed);

        if (!string.IsNullOrEmpty(liveRoomTxId)
            && open.Any(p => !string.IsNullOrEmpty(p.UnsignedTxId) && string.Equals(p.UnsignedTxId, liveRoomTxId, StringComparison.OrdinalIgnoreCase)))
            return new(WalletCardStatusKind.LiveRoom, 1);

        if (open.Count > 0)
        {
            var newest = open.OrderByDescending(p => p.CreatedAt).First();
            return new(WalletCardStatusKind.WaitingForOthers, Math.Max(1, newest.RequiredSignatures - newest.SignatureCount));
        }

        return Nothing;
    }
}
