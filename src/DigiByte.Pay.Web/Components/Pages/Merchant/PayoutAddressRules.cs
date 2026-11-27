using DigiByte.Crypto.Addresses;

namespace DigiByte.Pay.Web.Components.Pages.Merchant;

/// <summary>
/// The Dashboard's own check of a DD payout address before it is shown back or sent (specs/034 FR-002, constitution
/// "Address handling"). Same rules as Pay.Api's <c>PayoutAddressService.ValidateFormat</c>, using DigiByte.Crypto's
/// validators; whether the address is one of DigiPay's own is only known to the server.
/// </summary>
public static class PayoutAddressRules
{
    /// <summary>A plain message when the address can't be used, or null when it looks right.</summary>
    public static string? Problem(string? address, string network)
    {
        var a = address?.Trim();
        if (string.IsNullOrEmpty(a)) return "Paste the DigiDollar address from your own wallet.";
        if (!a.All(char.IsAsciiLetterOrDigit)) return "That isn't a DigiDollar address: it has spaces or symbols in it.";
        if (DigiDollarAddressValidator.TryDecode(a, null, out var detected, out _))
            return detected == network ? null : $"That's a {detected} DigiDollar address, but this store is on {network}.";
        if (AddressValidator.IsValid(a)) return "That's a DGB address. Payouts need a DigiDollar (DD) address from your wallet's DigiDollar tab.";
        var looksDd = (a.StartsWith("DD", StringComparison.Ordinal) || a.StartsWith("TD", StringComparison.Ordinal) || a.StartsWith("RD", StringComparison.Ordinal))
                      && a.Length is >= 40 and <= 60;
        return looksDd ? "That address has a typo: its checksum doesn't match. Copy it again from your wallet." : "That isn't a DigiDollar address.";
    }

    /// <summary>Server reasons (contract C1) in plain words.</summary>
    public static string Reason(string? reason) => reason switch
    {
        "format" => "That isn't a DigiDollar address.",
        "checksum" => "That address has a typo: its checksum doesn't match.",
        "wrong_network" => "That DigiDollar address is for a different network than this store.",
        "dgb_address" => "That's a DGB address. Payouts need a DigiDollar (DD) address.",
        "digipay_address" => "That's one of DigiPay's own DigiDollar addresses. Use an address from your own wallet.",
        _ => "That address can't be used for payouts.",
    };

    /// <summary>Start, middle and end of an address, so the first and last 6 characters can be highlighted (FR-003).</summary>
    public static (string Start, string Middle, string End) Split(string address) =>
        address.Length <= 12 ? (address, "", "") : (address[..6], address[6..^6], address[^6..]);
}
