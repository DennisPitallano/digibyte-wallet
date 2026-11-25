namespace DigiByte.Pay.Web.Components.Shared.Ui;

/// <summary>
/// Where to send a merchant after Digi-ID sign-in (or a detail page's back button). Only local
/// dashboard paths are honoured, so a crafted ?return= can't send anyone to another site
/// (open-redirect guard, specs/032-digipay-ui-refresh/contracts/dashboard-routes.md).
/// </summary>
public static class ReturnUrlGuard
{
    public const string Default = "/merchant";

    /// <summary>The value when it is a safe local /merchant path, otherwise <see cref="Default"/>.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default;

        // Check the raw value and the decoded value: "%2F%2Fevil.example" must not slip through.
        string decoded;
        try { decoded = Uri.UnescapeDataString(value); }
        catch (UriFormatException) { return Default; }

        return IsSafe(value) && IsSafe(decoded) ? value : Default;
    }

    private static bool IsSafe(string v)
    {
        if (v.Length == 0 || v[0] != '/') return false;          // relative path only
        if (v.StartsWith("//") || v.StartsWith("/\\")) return false; // protocol-relative or backslash tricks
        if (v.Contains('\\') || v.Contains("://")) return false;  // no scheme or host anywhere
        if (v.Any(char.IsControl)) return false;

        var path = v.Split('?', '#')[0];
        if (!(path.Equals("/merchant", StringComparison.OrdinalIgnoreCase)
              || path.StartsWith("/merchant/", StringComparison.OrdinalIgnoreCase))) return false;
        if (path.Equals("/merchant/signin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/merchant/signin/", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
