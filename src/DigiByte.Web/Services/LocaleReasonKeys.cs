using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The locale key for each reason a wallet service gives (spec 028). Screens translate by reason, never by matching the
/// English sentence, so rewording the English cannot silently break a translation.
/// </summary>
public static class LocaleReasonKeys
{
    /// <summary>Key for why a new receive address could not be handed out. The too-many-unused keys take the gap limit as {0}.</summary>
    public static string For(ReceiveAddressFailure reason) => reason switch
    {
        ReceiveAddressFailure.SingleAddress => "receive.error.single_address",
        ReceiveAddressFailure.SaveFailed => "receive.error.save_failed",
        ReceiveAddressFailure.TooManyUnusedPastLastUsed => "receive.error.too_many_unused.seed",
        ReceiveAddressFailure.TooManyUnusedPastLastPaid => "receive.error.too_many_unused.paid",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>Key for the node banner, or null when there is no banner.</summary>
    public static string? For(NodeBanner banner) => banner switch
    {
        NodeBanner.None => null,
        NodeBanner.NodeFallback => "banner.node.fallback",
        NodeBanner.Unreachable => "banner.node.unreachable",
        _ => throw new ArgumentOutOfRangeException(nameof(banner), banner, null),
    };

    /// <summary>Key for what "Check for Updates" tells the user.</summary>
    public static string For(UpdateCheckResult result) => result switch
    {
        UpdateCheckResult.Ready => "settings.update.ready",
        UpdateCheckResult.Downloading => "settings.update.downloading",
        UpdateCheckResult.Failed => "settings.update.failed",
        UpdateCheckResult.UpToDate => "settings.update.none",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
