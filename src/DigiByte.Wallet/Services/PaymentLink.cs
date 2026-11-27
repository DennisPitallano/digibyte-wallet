using System.Globalization;
using DigiByte.Crypto.Addresses;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>A payment read from a payment link or a <c>digibyte:</c> request, after every check passed.</summary>
/// <param name="FiatAmount">With <paramref name="FiatCurrency"/> and <paramref name="PriceExpiresAt"/>: the price lock of a
/// request made in the requester's currency (spec 036, FR-027a). All three or none.</param>
public sealed record PaymentLinkData(string Address, decimal? AmountDgb, string? Label, string? Message,
    decimal? FiatAmount = null, string? FiatCurrency = null, DateTime? PriceExpiresAt = null)
{
    /// <summary>A copy carrying <paramref name="other"/>'s price lock.</summary>
    public PaymentLinkData WithPriceLockOf(PaymentLinkData? other) => other is null ? this
        : this with { FiatAmount = other.FiatAmount, FiatCurrency = other.FiatCurrency, PriceExpiresAt = other.PriceExpiresAt };

    /// <summary>
    /// The same <c>digibyte:</c> request text <see cref="PaymentRequest.ToUri"/> makes. With
    /// <paramref name="includeNote"/> false the label and message are left out, so a note never reaches a screen
    /// that would publish it (Send's memo is written on-chain).
    /// </summary>
    public string ToBip21(bool includeNote = true) => new PaymentRequest
    {
        Id = "link",
        Address = Address,
        AmountDgb = AmountDgb,
        Label = includeNote ? Label : null,
        Message = includeNote ? Message : null,
        // The price lock is not a note: it always goes along, so Send can say when the price expires.
        FiatAmount = FiatAmount,
        FiatCurrency = FiatCurrency,
        ExpiresAt = PriceExpiresAt,
    }.ToUri();
}

/// <summary>
/// Payment links (specs/030-desktop-paste-scan, FR-019 to FR-024): <c>{wallet}/pay#digibyte:…</c>. Everything about
/// the payment is after the #, which browsers never send to the web host. A link is read only when every check
/// passes; when one fails, nothing at all is taken from it.
/// </summary>
public static class PaymentLink
{
    private const string Scheme = "digibyte:";
    private const string WebScheme = "web+digibyte:";
    private const int MaxAddressLength = 128;
    private const int MaxNoteLength = 256;
    private const decimal MaxAmount = 21_000_000_000m;
    private const int MaxDecimals = 8;

    /// <summary>The link for a payment request: this wallet's pay page with the request after the #.</summary>
    public static string Create(Uri walletBase, PaymentRequest request) =>
        PayPageUrl(walletBase) + "#" + request.ToUri();

    /// <summary>
    /// Reads a payment link to this wallet (<paramref name="walletBase"/>: same scheme, host, port, and the pay
    /// page), or a bare <c>digibyte:</c> / <c>web+digibyte:</c> request (escaped once or not). The address must be
    /// valid for <paramref name="network"/> (any DigiByte network when null), the amount, if given, must be more
    /// than 0, less than 21,000,000,000 and have at most 8 decimals with a point, and label and message at most
    /// 256 characters once control characters are removed.
    /// </summary>
    public static bool TryRead(string? text, Uri walletBase, Network? network, out PaymentLinkData? data)
    {
        data = null;
        var request = RequestText(text?.Trim() ?? "", walletBase);
        if (request is null) return false;

        var query = "";
        var address = request;
        var q = request.IndexOf('?');
        if (q >= 0)
        {
            address = request[..q];
            query = request[(q + 1)..];
        }

        address = address.Trim();
        if (address.Length is 0 or > MaxAddressLength) return false;
        if (!AddressValidator.IsValid(address, network)) return false;

        decimal? amount = null;
        string? label = null, message = null;
        string? fiat = null, currency = null, expires = null;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = parts[0].ToLowerInvariant();
            // The price lock is read as a group below: a fault in it drops it, never the link (spec 036, FR-027a).
            if (PriceLockFields.IsKey(key))
            {
                var raw = parts.Length == 2 ? parts[1] : "";
                if (key == PriceLockFields.FiatKey) fiat = raw;
                else if (key == PriceLockFields.CurrencyKey) currency = raw;
                else expires = raw;
                continue;
            }
            var value = parts.Length == 2 ? Unescape(parts[1]) : "";
            if (value is null) return false;
            switch (key)
            {
                case "amount":
                    if (!TryAmount(value, out var parsed)) return false;
                    amount = parsed;
                    break;
                case "label":
                    if (!TryNote(value, out label)) return false;
                    break;
                case "message":
                    if (!TryNote(value, out message)) return false;
                    break;
            }
        }

        data = PriceLockFields.TryRead(fiat, currency, expires, out var fiatAmount, out var code, out var expiresAt)
            ? new PaymentLinkData(address, amount, label, message, fiatAmount, code, expiresAt)
            : new PaymentLinkData(address, amount, label, message);
        return true;
    }

    /// <summary>
    /// The Send page for a link, used where a scanned payment's route is built. <c>/pay</c> itself never uses it:
    /// it hands the payment over in memory so nothing appears in a URL.
    /// </summary>
    public static string SendPath(PaymentLinkData data)
    {
        var path = "/send?address=" + Uri.EscapeDataString(data.Address);
        if (data.AmountDgb is { } amount) path += "&amount=" + FormatAmount(amount);
        return path;
    }

    /// <summary>An amount as a link writes it: a point, no trailing zeros.</summary>
    public static string FormatAmount(decimal amount) => amount.ToString("0.########", CultureInfo.InvariantCulture);

    private static string PayPageUrl(Uri walletBase) => walletBase.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/pay";

    // The digibyte: request inside the text, or null when the text is not one of the accepted forms.
    private static string? RequestText(string text, Uri walletBase)
    {
        if (text.Length == 0) return null;

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var hash = text.IndexOf('#');
            if (hash < 0) return null;
            if (!Uri.TryCreate(text[..hash], UriKind.Absolute, out var link)) return null;
            if (link.Scheme != walletBase.Scheme
                || !string.Equals(link.Host, walletBase.Host, StringComparison.OrdinalIgnoreCase)
                || link.Port != walletBase.Port
                || link.Query.Length > 0
                || !string.Equals(link.AbsolutePath, new Uri(PayPageUrl(walletBase)).AbsolutePath, StringComparison.Ordinal))
                return null;
            text = text[(hash + 1)..];
        }

        // The protocol handler may deliver the request escaped once.
        if (!IsRequest(text) && Unescape(text) is { } unescaped && IsRequest(unescaped)) text = unescaped;
        if (!IsRequest(text)) return null;

        return text.StartsWith(WebScheme, StringComparison.OrdinalIgnoreCase)
            ? text[WebScheme.Length..]
            : text[Scheme.Length..];
    }

    private static bool IsRequest(string text) =>
        text.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) || text.StartsWith(WebScheme, StringComparison.OrdinalIgnoreCase);

    private static string? Unescape(string value)
    {
        try { return Uri.UnescapeDataString(value); }
        catch (UriFormatException) { return null; }
    }

    // Digits and one point only: "1,5" is not a link amount and must never read as 15.
    private static bool TryAmount(string value, out decimal amount)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) return false;
        if (amount <= 0 || amount >= MaxAmount) return false;
        return amount.Scale <= MaxDecimals || decimal.Round(amount, MaxDecimals) == amount;
    }

    private static bool TryNote(string value, out string? note)
    {
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray());
        note = clean.Length == 0 ? null : clean;
        return clean.Length <= MaxNoteLength;
    }
}
