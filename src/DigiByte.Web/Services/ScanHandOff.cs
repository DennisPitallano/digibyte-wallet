using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>What the scan panel does with a value it was given.</summary>
public enum HandOffOutcome
{
    /// <summary>Nothing usable: empty, or only white space and quotes.</summary>
    Nothing,
    /// <summary>More than <see cref="PastedCode.MaxLength"/> characters; kept in the field to be corrected.</summary>
    TooLong,
    /// <summary>A recovery phrase or private key in the menu's panel: cleared at once and never used (FR-027).</summary>
    SecretCleared,
    /// <summary>Text the menu cannot open: kept in the field with a message (User Story 1, scenario 6).</summary>
    Unknown,
    /// <summary>Hand <see cref="HandOffDecision.Value"/> to the page that opened the panel.</summary>
    HandOver,
}

/// <summary>The panel's decision. <see cref="Value"/> is the text to hand over or keep in the field.</summary>
public sealed record HandOffDecision(HandOffOutcome Outcome, string? Value);

/// <summary>
/// The scan panel's rules (specs/030-desktop-paste-scan), kept out of the component so they can be tested. The same
/// rules apply whether the value was pasted, typed, read from a picture, from the camera or from a shared screen.
/// </summary>
public static class ScanHandOff
{
    /// <param name="raw">The value as it arrived.</param>
    /// <param name="fromMenu">True for the menu's Paste / Scan, which routes the value itself.</param>
    /// <param name="keepOpenIfUnknown">True for typed, pasted and picture values; false for the camera, whose
    /// unknown codes the menu reports as it always has.</param>
    /// <param name="appBaseUri">This wallet's own address, to recognise its payment links.</param>
    public static HandOffDecision Decide(string? raw, bool fromMenu, bool keepOpenIfUnknown, string appBaseUri)
    {
        var value = PastedCode.Clean(raw);
        if (value is null)
            return PastedCode.IsTooLong(raw) ? new(HandOffOutcome.TooLong, raw) : new(HandOffOutcome.Nothing, null);

        if (fromMenu)
        {
            if (PastedCode.LooksSecret(value)) return new(HandOffOutcome.SecretCleared, null);
            if (keepOpenIfUnknown && ScanRouter.Route(value, appBaseUri).Kind == ScanTarget.Unknown)
                return new(HandOffOutcome.Unknown, value);
            return new(HandOffOutcome.HandOver, value);
        }

        // On a page's own scan button, this wallet's payment link becomes the digibyte: request it carries, without
        // its note, so every page that reads requests accepts it and no note reaches Send's on-chain memo (FR-011a).
        if (ScanRouter.TryOwnPaymentLink(value, appBaseUri, out var link))
            return new(HandOffOutcome.HandOver, link.ToBip21(includeNote: false));

        return new(HandOffOutcome.HandOver, value);
    }
}
