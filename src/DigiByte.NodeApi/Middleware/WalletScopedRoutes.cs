using System.Text.RegularExpressions;

namespace DigiByte.NodeApi.Middleware;

/// <summary>
/// Classifies /api/digidollar routes as wallet-scoped (they read or spend the node's shared
/// DigiDollar wallet and must only be reachable by DigiPay with X-NodeApi-Key) or public
/// (public chain data or status, safe for the browser wallet).
///
/// Every DD route must appear in exactly one list; a test fails if a route is added to
/// <c>MapDigiDollarEndpoints</c> without being classified here.
/// </summary>
public static class WalletScopedRoutes
{
    public const string DdPathPrefix = "/api/digidollar";

    /// <summary>Route templates (relative to <see cref="DdPathPrefix"/>) that touch the node wallet.</summary>
    public static readonly string[] WalletScopedTemplates =
    [
        "/address/new",
        "/addresses",
        "/balance",
        "/balance/{address}",
        "/unspent",
        "/transactions",
        "/send",
        "/send-many",
        "/mint",
        "/redeem",
        "/positions",
        "/positions/{positionId}/redemption",
    ];

    /// <summary>Route templates (relative to <see cref="DdPathPrefix"/>) that are public by design.</summary>
    public static readonly string[] PublicTemplates =
    [
        "/deployment",
        "/stats",
        "/oracle/price",
        "/protection",
        "/health",
        "/address/validate",
        "/collateral/estimate",
        // Wallet-facing, address-scoped read surface (POST bodies only, never URL paths).
        "/addresses/utxos",
        "/addresses/history",
        "/tx/{txid}/status",
    ];

    private static readonly Regex[] WalletScopedPatterns = WalletScopedTemplates.Select(ToRegex).ToArray();

    public static bool IsKnownWalletScopedTemplate(string template) =>
        WalletScopedTemplates.Contains(template, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownPublicTemplate(string template) =>
        PublicTemplates.Contains(template, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is a concrete wallet-scoped DigiDollar route.</summary>
    public static bool IsWalletScoped(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!path.StartsWith(DdPathPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rel = path[DdPathPrefix.Length..].TrimEnd('/');
        return WalletScopedPatterns.Any(r => r.IsMatch(rel));
    }

    private static Regex ToRegex(string template)
    {
        var pattern = "^" + Regex.Replace(Regex.Escape(template), @"\\\{[^}]+\}", "[^/]+") + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
