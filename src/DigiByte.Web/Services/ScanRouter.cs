using System.Globalization;
using DigiByte.Crypto.Addresses;
using DigiByte.Wallet.Models;

namespace DigiByte.Web.Services;

/// <summary>What a scanned QR code turned out to be.</summary>
public enum ScanTarget
{
    /// <summary>A DigiByte address or payment request: open Send with it filled in.</summary>
    Payment,
    /// <summary>A Digi-ID login request: open Digi-ID, which asks before signing in.</summary>
    DigiId,
    /// <summary>Neither. Nothing is opened; the user is told what the scanner understands.</summary>
    Unknown,
}

/// <summary>Where a scanned code leads. <see cref="Url"/> is null for <see cref="ScanTarget.Unknown"/>.</summary>
public sealed record ScanRoute(ScanTarget Kind, string? Url);

/// <summary>
/// The one place that decides what the app-wide Scan button does with a code, so the menu's Scan and Home's Scan
/// can never disagree. A code the wallet does not understand goes nowhere: it is never pasted into Send as if it
/// were an address.
/// </summary>
public static class ScanRouter
{
    public static ScanRoute Route(string? scanned)
    {
        var data = scanned?.Trim();
        if (string.IsNullOrEmpty(data)) return new(ScanTarget.Unknown, null);

        // Digi-ID, including the form the installed app's link handler uses.
        if (data.StartsWith("web+digiid://", StringComparison.OrdinalIgnoreCase))
            data = "digiid://" + data["web+digiid://".Length..];
        if (data.StartsWith("digiid://", StringComparison.OrdinalIgnoreCase))
            return new(ScanTarget.DigiId, "/identity?uri=" + Uri.EscapeDataString(data));

        // A payment request (digibyte:address?amount=...) or a bare address.
        var request = PaymentRequest.FromUri(data);
        var address = (request?.Address ?? data).Trim();
        if (!AddressValidator.IsValid(address)) return new(ScanTarget.Unknown, null);

        var url = "/send?address=" + Uri.EscapeDataString(address);
        if (request?.AmountDgb is { } amount && amount > 0)
            url += "&amount=" + amount.ToString(CultureInfo.InvariantCulture);
        return new(ScanTarget.Payment, url);
    }
}
