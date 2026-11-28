namespace DigiByte.Web.Services;

/// <summary>Where a page behind <c>&lt;AuthGuard&gt;</c> goes.</summary>
public enum GateRoute
{
    /// <summary>A wallet is open in this session: show the page.</summary>
    Show,
    /// <summary>No wallet on this device yet.</summary>
    Welcome,
    /// <summary>Wallets exist but none is open: the PIN or passphrase is needed.</summary>
    Unlock,
}

/// <summary>
/// The one rule for whether a page that needs the wallet may show. It reads the wallet's own session
/// (<c>WalletService.IsUnlocked</c>, the decrypted keys in memory), never <see cref="AppState.IsWalletUnlocked"/>:
/// that flag is UI state and can disagree with the session after a reload or a lock, and a page shown over a locked
/// session crashes the first time it asks for a key.
/// </summary>
public static class UnlockGate
{
    public const string UnlockPath = "/unlock";
    public const string WelcomePath = "/welcome";

    public static GateRoute Decide(bool anyWallet, bool sessionUnlocked) =>
        !anyWallet ? GateRoute.Welcome
        : !sessionUnlocked ? GateRoute.Unlock
        : GateRoute.Show;

    /// <summary>The page to send the person to, or null to show the page.</summary>
    public static string? RedirectFor(GateRoute route) => route switch
    {
        GateRoute.Welcome => WelcomePath,
        GateRoute.Unlock => UnlockPath,
        _ => null,
    };
}
