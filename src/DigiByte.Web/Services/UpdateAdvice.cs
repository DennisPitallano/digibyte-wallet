namespace DigiByte.Web.Services;

/// <summary>
/// When a downloaded update may be applied without asking. Applying reloads the page, which locks the wallet and
/// discards anything half-entered, so it is only done unasked where there is nothing to interrupt.
/// </summary>
public static class UpdateAdvice
{
    /// <summary>
    /// True on the Welcome and Unlock screens while no wallet is unlocked. Everywhere else the user is asked,
    /// including the create and recover flows, where a reload would throw away a recovery phrase on screen.
    /// </summary>
    public static bool ApplyWithoutAsking(string? path, bool walletUnlocked)
    {
        if (walletUnlocked) return false;
        var p = (path ?? "").TrimEnd('/').ToLowerInvariant();
        return p is "/unlock" or "/welcome";
    }

    /// <summary>What "Check for Updates" tells the user for each answer from the page script.</summary>
    public static string Message(string? status) => status switch
    {
        "ready" => "Update found. Reloading…",
        "downloading" => "Update found. Still downloading; it will be offered when ready.",
        "failed" => "Could not check. Try again.",
        _ => "Already up to date ✓",
    };
}
