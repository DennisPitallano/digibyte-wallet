using System.Net;
using System.Net.Sockets;

namespace DigiByte.Server.Shared;

/// <summary>
/// The destination rules from contracts/webhook-url-policy.md. <see cref="Validate"/> checks the URL itself (no DNS);
/// <see cref="ValidateAsync"/> adds a resolution check for hostnames; the connect-time check lives in <see cref="GuardedConnector"/>.
/// </summary>
public static class OutboundUrlGuard
{
    public const int MaxUrlLength = 2048;

    /// <summary>Returns null when the URL is acceptable, else the short reason.</summary>
    public static OutboundReason? Validate(string? url, OutboundUrlPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength || !url.Contains("://", StringComparison.Ordinal))
            return OutboundReason.InvalidUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return OutboundReason.InvalidUrl;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return OutboundReason.MustUseHttps;
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return OutboundReason.AddressNotAllowed;
        if (!policy.AllowedPorts.Contains(uri.Port))
            return OutboundReason.AddressNotAllowed;

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (host.Length == 0) return OutboundReason.InvalidUrl;

        if (IsLocalhostName(host))
            return policy.AllowLoopback ? null : OutboundReason.AddressNotAllowed;

        if (IPAddress.TryParse(host, out var ip))
            return IsBlockedAddress(ip, policy.AllowLoopback) ? OutboundReason.AddressNotAllowed : null;

        return null;
    }

    /// <summary>
    /// <see cref="Validate"/> plus a DNS check, for write time (webhook registration). A hostname that does not resolve passes here;
    /// the connect-time check still protects every request.
    /// </summary>
    public static async Task<OutboundReason?> ValidateAsync(string? url, OutboundUrlPolicy policy, IOutboundResolver resolver, CancellationToken ct = default)
    {
        var basic = Validate(url, policy);
        if (basic is not null) return basic;

        var host = new Uri(url!).DnsSafeHost.TrimEnd('.');
        if (IsLocalhostName(host) || IPAddress.TryParse(host, out _))
            return null;

        IPAddress[] addresses;
        try { addresses = await resolver.ResolveAsync(host, ct); }
        catch (Exception ex) when (ex is SocketException or ArgumentException) { return null; }

        return addresses.Any(a => IsBlockedAddress(a, policy.AllowLoopback)) ? OutboundReason.AddressNotAllowed : null;
    }

    private static bool IsLocalhostName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for every address a server-side fetch must never reach (loopback unless <paramref name="allowLoopback"/>).</summary>
    public static bool IsBlockedAddress(IPAddress ip, bool allowLoopback)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 127) return !allowLoopback;
            return b[0] == 0                                           // 0.0.0.0/8 unspecified / "this network"
                || b[0] == 10                                          // private
                || (b[0] == 172 && (b[1] & 0xF0) == 16)                // 172.16/12
                || (b[0] == 192 && b[1] == 168)                        // 192.168/16
                || (b[0] == 169 && b[1] == 254)                        // link-local incl. 169.254.169.254 metadata
                || (b[0] == 100 && (b[1] & 0xC0) == 64)                // CGNAT 100.64/10
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)             // 192.0.0.0/24 protocol assignments
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)             // TEST-NET-1
                || (b[0] == 198 && (b[1] & 0xFE) == 18)                // benchmarking 198.18/15
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)          // TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)           // TEST-NET-3
                || b[0] >= 224;                                        // multicast, reserved, broadcast
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(ip)) return !allowLoopback;
            if (ip.Equals(IPAddress.IPv6Any)) return true; // ::
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC                                          // fc00::/7 unique local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80)                        // fe80::/10 link-local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0)                        // fec0::/10 site-local (deprecated)
                || b[0] == 0xFF                                                   // multicast
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) // 2001:db8::/32 documentation
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) // 64:ff9b::/32 NAT64 (can embed private IPv4)
                || (b[0] == 0x20 && b[1] == 0x02);                                // 2002::/16 6to4 (embeds IPv4)
        }

        return true; // unknown family
    }
}
