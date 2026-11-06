using System.Net;
using Microsoft.AspNetCore.Http;

namespace DigiByte.Server.Shared;

/// <summary>
/// The caller's address for rate limiting. The services sit behind Railway's proxy, which APPENDS the real client address to
/// X-Forwarded-For; everything to its left is whatever the caller claimed. So the rightmost entry is the client and a spoofed header
/// cannot reset a limit (specs/008-security-hardening, T011/T012). Without the header the socket address is used.
/// </summary>
public static class ClientAddress
{
    private const string ItemKey = "client-address";

    /// <summary>
    /// Call at the very start of the pipeline, BEFORE the forwarded-headers middleware, which rewrites the connection address and strips
    /// the header entry it consumed. <see cref="Of"/> then returns the captured value.
    /// </summary>
    public static void Capture(HttpContext context) => context.Items[ItemKey] = Compute(context);

    public static string Of(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var v) && v is string s ? s : Compute(context);

    private static string Compute(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(last) && IPAddress.TryParse(last, out var ip)) return ip.ToString();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
