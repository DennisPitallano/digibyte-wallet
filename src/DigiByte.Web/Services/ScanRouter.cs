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
    /// <param name="sharedWalletId">The shared wallet that is open, if any. A payment code then starts a payment from
    /// that shared wallet, not from the personal one (specs/026-multisig-wallet-experience, FR-002a). Every other
    /// kind of code goes where it always goes.</param>
    public static ScanRoute Route(string? scanned, string? appBaseUri = null, string? sharedWalletId = null)
    {
        var data = scanned?.Trim();
        if (string.IsNullOrEmpty(data)) return Unknown;

        // Digi-ID, including the form the installed app's link handler uses.
        if (data.StartsWith("web+digiid://", StringComparison.OrdinalIgnoreCase))
            data = "digiid://" + data["web+digiid://".Length..];
        if (data.StartsWith("digiid://", StringComparison.OrdinalIgnoreCase))
            return new(ScanTarget.DigiId, "/identity?uri=" + Uri.EscapeDataString(data));

        if (RoomInvite(data, appBaseUri) is { } invite) return invite;

        // A payment: this wallet's own payment link, a digibyte: request or a bare address.
        if (Payment(data, appBaseUri) is { } payment)
            return new(ScanTarget.Payment, SendUrl(payment.Address, payment.AmountDgb, sharedWalletId));

        // The multisig pages' own classifier, for a proposal (payments were read above).
        if (MultisigSendHelper.ClassifyScan(data).Kind == ScanKind.Psbt)
            return new(ScanTarget.MultisigProposal, null, data);

        // Last, because a redeem script is "any plausible run of hex": everything more specific was ruled out above.
        if (MultisigSendHelper.TryParseRedeemScriptQr(data, out var redeemScript))
            return new(ScanTarget.MultisigRedeemScript, null, redeemScript);

        return Unknown;
    }

    private static readonly ScanRoute Unknown = new(ScanTarget.Unknown, null);

    /// <summary>
    /// True when <paramref name="scanned"/> is a code <see cref="Route"/> sends to Send: a bare address, a
    /// <c>digibyte:</c> request, or this wallet's payment link. <paramref name="payment"/> then holds the address and
    /// the amount (null unless more than 0) the route would have put in Send's address, and never a label or message,
    /// which must not reach Send's memo. The menu hands it to Send in memory instead of putting it in the address bar
    /// (specs/032-desktop-receive-everywhere, FR-001, FR-003, FR-005).
    /// </summary>
    public static bool TryPayment(string? scanned, string? appBaseUri, out PaymentLinkData payment)
    {
        payment = null!;
        if (Route(scanned, appBaseUri).Kind != ScanTarget.Payment) return false;
        payment = Payment(scanned!.Trim(), appBaseUri)!;
        return true;
    }

    /// <summary>
    /// The Send page a handed-over payment opens: the personal Send, or the open shared wallet's. It carries no payment;
    /// <paramref name="counter"/> only makes the address new when that Send is already open.
    /// </summary>
    public static string SendPagePath(string? sharedWalletId, int counter) =>
        (string.IsNullOrWhiteSpace(sharedWalletId) ? "/send" : $"/multisig/{Uri.EscapeDataString(sharedWalletId)}/send")
        + "?link=" + counter.ToString(CultureInfo.InvariantCulture);

    // The address and amount of a payment code, or null. The one place both Route and TryPayment read a payment from.
    private static PaymentLinkData? Payment(string data, string? appBaseUri)
    {
        // This wallet's own payment link (specs/030-desktop-paste-scan, FR-024). Any other web link goes nowhere.
        if (PayLink(data, appBaseUri) is { } link)
            return new PaymentLinkData(link.Address, link.AmountDgb is > 0 ? link.AmountDgb : null, null, null).WithPriceLockOf(link);

        // The multisig pages' own classifier: payment request or bare address.
        var result = MultisigSendHelper.ClassifyScan(data);
        if (result.Kind is ScanKind.Address or ScanKind.PaymentUri && result.Address is { } address && AddressValidator.IsValid(address.Trim()))
        {
            var payment = new PaymentLinkData(address.Trim(), result.AmountDgb is > 0 ? result.AmountDgb : null, null, null);
            // A bare request's price lock (spec 036, FR-027a): read by the link reader, which also takes a bare request.
            return result.Kind == ScanKind.PaymentUri && PaymentLink.TryRead(data, PriceLockBase, null, out var read)
                ? payment.WithPriceLockOf(read)
                : payment;
        }
        return null;
    }

    // Any base will do for reading a bare digibyte: request, which is not a web link.
    private static readonly Uri PriceLockBase = new("https://localhost/");

    // A payment opens the personal Send, or the open shared wallet's Send (FR-002a).
    private static string SendUrl(string address, decimal? amountDgb, string? sharedWalletId)
    {
        var send = string.IsNullOrWhiteSpace(sharedWalletId)
            ? "/send"
            : $"/multisig/{Uri.EscapeDataString(sharedWalletId)}/send";
        var url = send + "?address=" + Uri.EscapeDataString(address);
        if (amountDgb is { } amount && amount > 0)
            url += "&amount=" + amount.ToString(CultureInfo.InvariantCulture);
        return url;
    }

    // Only a web link: a bare digibyte: request keeps going through the classifier exactly as before.
    private static PaymentLinkData? PayLink(string data, string? appBaseUri) =>
        TryOwnPaymentLink(data, appBaseUri, out var link) ? link : null;

    /// <summary>
    /// True when <paramref name="data"/> is a valid payment link to this wallet (<c>{appBaseUri}pay#digibyte:…</c>).
    /// A bare <c>digibyte:</c> request is not a link and returns false; so does a look-alike site's link.
    /// </summary>
    public static bool TryOwnPaymentLink(string? data, string? appBaseUri, out PaymentLinkData link)
    {
        link = null!;
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(appBaseUri) || !Uri.TryCreate(appBaseUri, UriKind.Absolute, out var app)) return false;
        if (!data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !data.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (!PaymentLink.TryRead(data, app, null, out var read) || read is null) return false;
        link = read;
        return true;
    }

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
