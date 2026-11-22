namespace DigiByte.Wallet.Services;

/// <summary>
/// Where the Multisig shortcut on a wallet's home leads (specs/026-multisig-wallet-experience, FR-009): back to the
/// shared wallet used last, straight into the only one, or to the list. Which one was used last is the one new thing
/// this feature saves: a shared wallet's id, per personal wallet, on this device only.
/// </summary>
public static class SharedWalletShortcut
{
    /// <summary>The preference key holding the last-used shared wallet of one personal wallet.</summary>
    public static string StorageKey(string personalWalletId) => "pref:multisigLastUsed:" + personalWalletId;

    /// <param name="lastUsedId">What was remembered, if anything. It may be stale.</param>
    /// <param name="availableIds">The active wallet's shared wallets right now.</param>
    /// <returns>The shared wallet to open, or null to open the list.</returns>
    public static string? Target(string? lastUsedId, IReadOnlyCollection<string> availableIds)
    {
        if (!string.IsNullOrWhiteSpace(lastUsedId) && availableIds.Contains(lastUsedId, StringComparer.Ordinal))
            return lastUsedId;
        return availableIds.Count == 1 ? availableIds.First() : null;
    }
}
