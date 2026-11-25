namespace DigiByte.Pay.Web.Components.Shared.Ui;

/// <summary>
/// C# mirror of the precedence in wwwroot/theme.js (specs/032-digipay-ui-refresh/data-model.md §1–§2),
/// so the rules are covered by unit tests and server code can reason about them. The browser
/// script is what actually applies the theme; keep the two in step.
/// </summary>
public static class ThemeResolver
{
    public const string SiteKey = "dp-theme";
    public const string PosKey = "digipay.pos.theme:v1";

    /// <summary>
    /// The theme to show ("light" or "dark").
    /// </summary>
    /// <param name="path">Request path, for example "/pay/ses_x".</param>
    /// <param name="query">Query string including or excluding the leading '?'.</param>
    /// <param name="savedSite">Value stored under <see cref="SiteKey"/>, or null.</param>
    /// <param name="savedPos">Value stored under <see cref="PosKey"/>, or null.</param>
    /// <param name="deviceDark">Whether the device prefers a dark colour scheme.</param>
    public static string Resolve(string path, string? query, string? savedSite, string? savedPos, bool deviceDark)
    {
        var shop = ShopOverride(path, query);
        if (shop is not null) return shop;

        var mode = Mode(path, savedSite, savedPos);
        return mode == "system" ? (deviceDark ? "dark" : "light") : mode;
    }

    /// <summary>The saved choice that applies on this page: "light", "dark" or "system".</summary>
    public static string Mode(string path, string? savedSite, string? savedPos)
    {
        if (IsPosPage(path)) return IsMode(savedPos) ? savedPos! : "dark";
        return IsMode(savedSite) ? savedSite! : "system";
    }

    /// <summary>A shop's ?theme=light|dark on /pay pages, or null when absent, invalid or on another page.</summary>
    public static string? ShopOverride(string path, string? query)
    {
        if (!IsPayPage(path) || string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq];
            if (!key.Equals("theme", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Uri.UnescapeDataString(eq < 0 ? "" : part[(eq + 1)..]).ToLowerInvariant();
            return value is "light" or "dark" ? value : null;
        }
        return null;
    }

    public static bool IsPayPage(string path)
    {
        var p = (path ?? "/").ToLowerInvariant();
        return p == "/pay" || p.StartsWith("/pay/", StringComparison.Ordinal);
    }

    public static bool IsPosPage(string path)
    {
        var p = (path ?? "/").ToLowerInvariant();
        return p == "/pos" || p.StartsWith("/pos/", StringComparison.Ordinal);
    }

    private static bool IsMode(string? value) => value is "light" or "dark" or "system";
}
