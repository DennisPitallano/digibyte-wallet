namespace DigiByte.Web.Services;

/// <summary>What a "Check for Updates" came back with. The text for each is in the locale files (<see cref="LocaleReasonKeys"/>).</summary>
public enum UpdateCheckResult
{
    UpToDate,
    Ready,
    Downloading,
    Failed,
}

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

    /// <summary>The result for each answer from the page script's <c>pwaCheckForUpdate</c>; anything unknown is up to date.</summary>
    public static UpdateCheckResult ResultOf(string? status) => status switch
    {
        "ready" => UpdateCheckResult.Ready,
        "downloading" => UpdateCheckResult.Downloading,
        "failed" => UpdateCheckResult.Failed,
        _ => UpdateCheckResult.UpToDate,
    };
}
