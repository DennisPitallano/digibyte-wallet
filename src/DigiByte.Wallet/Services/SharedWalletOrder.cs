using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The order of shared wallets on the list: those with something waiting for the viewer first, then the most
/// recently active (specs/026-multisig-wallet-experience, FR-015). "Active" comes from what is saved on this device,
/// so nothing new is tracked.
/// </summary>
public static class SharedWalletOrder
{
    /// <summary>Sorted once per load of the list; the list does not reorder while it is being looked at.</summary>
    public static IReadOnlyList<WalletCardModel> Sort(IEnumerable<WalletCardModel> cards) =>
        // Both sorts are stable: wallets that tie keep the order they came in.
        cards.OrderBy(c => c.NeedsYou > 0 ? 0 : 1).ThenByDescending(c => c.LastActivity).ToList();

    /// <summary>The latest thing known to have happened: the newest saved payment, or the wallet's own date if later.</summary>
    public static DateTime LastActivity(DateTime walletCreatedAt, IEnumerable<PendingMultisigTransaction> pending)
    {
        var last = walletCreatedAt;
        foreach (var item in pending)
        {
            if (item.UpdatedAt > last) last = item.UpdatedAt;
            if (item.CreatedAt > last) last = item.CreatedAt;
        }
        return last;
    }
}
