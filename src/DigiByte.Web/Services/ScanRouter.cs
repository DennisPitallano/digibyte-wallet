using System.Globalization;
using DigiByte.Crypto.Addresses;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>What a scanned QR code turned out to be.</summary>
public enum ScanTarget
{
    /// <summary>A DigiByte address or payment request: open Send with it filled in.</summary>
    Payment,
    /// <summary>A Digi-ID login request: open Digi-ID, which asks before signing in.</summary>
    DigiId,
    /// <summary>A multisig proposal (PSBT) from a co-signer: open the multisig it spends from, which adds it.</summary>
    MultisigProposal,
    /// <summary>A multisig's redeem script, from its backup panel: open Import multisig with it filled in.</summary>
    MultisigRedeemScript,
    /// <summary>An invite to a multisig room (create together, or sign together): open that room.</summary>
    RoomInvite,
    /// <summary>None of these. Nothing is opened; the user is told what the scanner understands.</summary>
    Unknown,
}

/// <summary>
/// Where a scanned code leads. <see cref="Url"/> is set when the code alone decides the page (payment, Digi-ID, room
/// invite). <see cref="Payload"/> carries a proposal or a redeem script, which are handed over in memory and never
/// put in an address.
/// </summary>
public sealed record ScanRoute(ScanTarget Kind, string? Url, string? Payload = null);

/// <summary>
/// The one place that decides what the app-wide Scan button does with a code, so the menu's Scan and Home's Scan
/// can never disagree. A code the wallet does not understand goes nowhere: it is never pasted into Send as if it
/// were an address, and a web link is only followed when it is this wallet's own room invite.
/// </summary>
public static class ScanRouter
{
    /// <param name="scanned">The text the scanner read.</param>
    /// <param name="appBaseUri">This wallet's own address (for example https://dgbwallet.app/), to recognise its room invites.</param>
    public static ScanRoute Route(string? scanned, string? appBaseUri = null)
    {
        var data = scanned?.Trim();
        if (string.IsNullOrEmpty(data)) return Unknown;

        // Digi-ID, including the form the installed app's link handler uses.
        if (data.StartsWith("web+digiid://", StringComparison.OrdinalIgnoreCase))
            data = "digiid://" + data["web+digiid://".Length..];
        if (data.StartsWith("digiid://", StringComparison.OrdinalIgnoreCase))
            return new(ScanTarget.DigiId, "/identity?uri=" + Uri.EscapeDataString(data));

        if (RoomInvite(data, appBaseUri) is { } invite) return invite;

        // The multisig pages' own classifier: payment request, bare address or proposal.
        var result = MultisigSendHelper.ClassifyScan(data);
        switch (result.Kind)
        {
            case ScanKind.Psbt:
                return new(ScanTarget.MultisigProposal, null, data);
            case ScanKind.Address or ScanKind.PaymentUri when result.Address is { } address && AddressValidator.IsValid(address.Trim()):
                var url = "/send?address=" + Uri.EscapeDataString(address.Trim());
                if (result.AmountDgb is { } amount && amount > 0)
                    url += "&amount=" + amount.ToString(CultureInfo.InvariantCulture);
                return new(ScanTarget.Payment, url);
        }

        // Last, because a redeem script is "any plausible run of hex": everything more specific was ruled out above.
        if (MultisigSendHelper.TryParseRedeemScriptQr(data, out var redeemScript))
            return new(ScanTarget.MultisigRedeemScript, null, redeemScript);

        return Unknown;
    }

    private static readonly ScanRoute Unknown = new(ScanTarget.Unknown, null);

    // The two room pages and nothing else; the invite code is letters and digits only.
    private static readonly string[] RoomPaths = ["/multisig/room", "/multisig/sign-room"];

    private static ScanRoute? RoomInvite(string data, string? appBaseUri)
    {
        if (string.IsNullOrEmpty(appBaseUri)) return null;
        if (!Uri.TryCreate(data, UriKind.Absolute, out var link) || !Uri.TryCreate(appBaseUri, UriKind.Absolute, out var app)) return null;
        if (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp) return null;
        // Only this wallet's own address: a look-alike site's link is not followed.
        if (link.Scheme != app.Scheme || !string.Equals(link.Host, app.Host, StringComparison.OrdinalIgnoreCase) || link.Port != app.Port) return null;

        var path = link.AbsolutePath.TrimEnd('/');
        if (!RoomPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return null;

        var code = System.Web.HttpUtility.ParseQueryString(link.Query)["code"]?.Trim() ?? "";
        if (code.Length is 0 or > 16 || !code.All(char.IsAsciiLetterOrDigit)) return null;
        return new(ScanTarget.RoomInvite, $"{path.ToLowerInvariant()}?code={code.ToUpperInvariant()}");
    }
}
