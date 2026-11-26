using System.Net;
using System.Security.Cryptography;
using System.Text;
using DigiByte.Server.Shared;

namespace DigiByte.Pay.Api.Auth;

/// <summary>
/// The address a per-client rate limit counts against. Pay.Web (Blazor Server) calls this API from its own server, so
/// without help every merchant signing in through it looks like one client and shares one allowance. When
/// <c>DigiPay:TrustedClientKey</c> is set on both apps, Pay.Web sends the visitor's address in
/// <see cref="AddressHeader"/> together with the key in <see cref="KeyHeader"/>; only then is that address used. A
/// missing or wrong key falls back to the caller's own address (<see cref="ClientAddress"/>), so nobody else can pick
/// the bucket they are counted in.
/// </summary>
public static class TrustedClient
{
    public const string AddressHeader = "X-DigiPay-Client-Address";
    public const string KeyHeader = "X-DigiPay-Client-Key";
    public const string ConfigKey = "DigiPay:TrustedClientKey";

    public static string AddressOf(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetService<IConfiguration>()?[ConfigKey];
        if (!string.IsNullOrEmpty(configured)
            && ctx.Request.Headers.TryGetValue(KeyHeader, out var sentKey)
            && KeyMatches(configured, sentKey.ToString())
            && IPAddress.TryParse(ctx.Request.Headers[AddressHeader].ToString().Trim(), out var visitor))
        {
            return "visitor:" + visitor;
        }
        return ClientAddress.Of(ctx);
    }

    private static bool KeyMatches(string expected, string sent) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(sent)));
}
