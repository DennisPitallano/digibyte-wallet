using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The active app wallet's id and public key, as the multisig pages need them to scope what they show.
/// The public key is only available while the wallet is unlocked; without it, only items that wallet already
/// owns are shown (older items can't be matched by key).
/// </summary>
public sealed class ActiveWalletContext(WalletService wallets)
{
    /// <summary>Id of the active wallet, or an empty string when none is active.</summary>
    public string WalletId => wallets.ActiveWallet?.Id ?? "";

    /// <summary>Display name of the active wallet.</summary>
    public string WalletName => wallets.ActiveWallet?.Name ?? "Wallet";

    /// <summary>Accent colour of the active wallet (the dot and the card edge on the wallet dashboard).</summary>
    public string WalletColor => wallets.ActiveWallet?.Color ?? "#0066FF";

    /// <summary>The active wallet's multisig public key, or null while locked or watch-only.</summary>
    public string? PublicKey
    {
        get
        {
            try { return wallets.IsUnlocked ? wallets.GetPublicKey() : null; }
            catch { return null; }
        }
    }
}
