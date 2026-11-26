using System.Net;

namespace DigiByte.Wallet.Services;

/// <summary>A checked "return to the site" address: where to go, and the host to show on the button.</summary>
public sealed record SiteReturn(string Url, string Host);

/// <summary>
/// The optional <c>return</c> address a site (such as DigiPay) passes when it opens the wallet for a payment
/// (<c>/pay?return=…#digibyte:…</c>) or a Digi-ID sign-in (<c>/identity?uri=…&amp;return=…</c>). After the payment is
/// sent or the sign-in approved, the wallet offers a "Return to {host}" button; it never goes there by itself.
/// <para>
/// Rules: an absolute https address (http only for localhost, 127.0.0.1 and [::1]), no user name or password in it,
/// no spaces, control characters or backslashes. For Digi-ID the address must also be on the same site as the
/// request's callback (<see cref="ForDigiId"/>). Anything else gives null and the button is simply not shown.
/// </para>
/// </summary>
public static class SiteReturnUrl
{
    private const int MaxLength = 2048;

    /// <summary>
    /// A return address for a payment link. A digibyte: request names no site, so only the https / localhost rule
    /// applies; the host is shown on the button so the person can judge it.
    /// </summary>
    public static SiteReturn? ForPayment(string? value) =>
        Parse(value) is { } uri ? Make(uri) : null;

    /// <summary>
    /// A return address for a Digi-ID request whose callback is <paramref name="callbackUrl"/>
    /// (<c>DigiIdRequest.CallbackUrl</c>). Besides the payment rule, the return host must be the same site as the
    /// callback host: the same host, or the same last two labels (pay.dgbwallet.app and api.pay.dgbwallet.app are
    /// both dgbwallet.app). Localhost and IP addresses must match exactly. The port must be the address's default
    /// port or the callback's port, except on localhost / 127.0.0.1 / [::1], where any port is accepted so a local
    /// DigiPay (web on :5252, API on :5008) works.
    /// </summary>
    /// <remarks>
    /// "Last two labels" is a deliberate simplification of the registrable domain: it does not know public suffixes
    /// such as co.uk, so a.co.uk and b.co.uk would count as one site. The host is always shown on the button.
    /// </remarks>
    public static SiteReturn? ForDigiId(string? value, string? callbackUrl)
    {
        if (Parse(value) is not { } target) return null;
        if (string.IsNullOrWhiteSpace(callbackUrl)
            || !Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callback)
            || callback.Scheme is not ("https" or "http")
            || callback.UserInfo.Length > 0)
            return null;

        var targetHost = HostOf(target);
        var callbackHost = HostOf(callback);
        if (targetHost.Length == 0 || callbackHost.Length == 0 || callbackHost.EndsWith('.')) return null;

        if (IsLoopback(targetHost) || IsLoopback(callbackHost))
            return targetHost == callbackHost ? Make(target) : null; // any port on the same loopback host

        if (IsIpAddress(targetHost) || IsIpAddress(callbackHost))
        {
            if (targetHost != callbackHost) return null;
        }
        else if (targetHost != callbackHost && LastTwoLabels(targetHost) != LastTwoLabels(callbackHost))
        {
            return null;
        }

        return target.IsDefaultPort || target.Port == callback.Port ? Make(target) : null;
    }

    /// <summary>The https / localhost rule shared by both kinds of return address; null when it fails.</summary>
    private static Uri? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength) return null;
        foreach (var c in value)
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\') return null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        // The text itself must start with the scheme, so nothing that Uri quietly repairs is accepted.
        if (!value.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase)) return null;
        if (uri.UserInfo.Length > 0) return null; // https://dgbwallet.app@evil.com
        var host = HostOf(uri);
        if (host.Length == 0 || host.EndsWith('.')) return null;

        return uri.Scheme switch
        {
            "https" => uri,
            "http" when IsLoopback(host) => uri,
            _ => null,
        };
    }

    private static SiteReturn Make(Uri uri)
    {
        var host = HostOf(uri);
        return new SiteReturn(uri.AbsoluteUri, uri.IsDefaultPort ? host : $"{host}:{uri.Port}");
    }

    // The ASCII (punycode) form, lowercase: a look-alike written with other alphabets shows as xn--… on the button.
    // IPv6 keeps its brackets, as it is written in an address: [::1].
    private static string HostOf(Uri uri) => uri.HostNameType == UriHostNameType.IPv6
        ? "[" + uri.IdnHost.ToLowerInvariant() + "]"
        : uri.IdnHost.ToLowerInvariant();

    private static bool IsLoopback(string host) => host is "localhost" or "127.0.0.1" or "[::1]";

    private static bool IsIpAddress(string host) =>
        host.StartsWith('[') || IPAddress.TryParse(host, out _);

    private static string LastTwoLabels(string host)
    {
        var labels = host.Split('.');
        return labels.Length <= 2 ? host : labels[^2] + "." + labels[^1];
    }
}
