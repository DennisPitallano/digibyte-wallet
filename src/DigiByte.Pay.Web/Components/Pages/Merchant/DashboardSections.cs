namespace DigiByte.Pay.Web.Components.Pages.Merchant;

/// <summary>One section of the merchant dashboard (specs/032-digipay-ui-refresh/data-model.md §3).</summary>
/// <param name="Key">Internal key used by the dashboard's markup.</param>
/// <param name="Slug">The URL segment: /merchant/{Slug}.</param>
/// <param name="BottomSlot">Position in the phone bottom menu (1–4), or 0 when the section is under More.</param>
/// <param name="Raised">The bottom menu's raised main button.</param>
/// <param name="Narrow">Forms and single-item pages use the wallet's narrow column (clarification, second pass).</param>
public sealed record DashboardSection(string Key, string Slug, string Label, string Icon, int BottomSlot, bool Raised, bool Narrow)
{
    public string Href => Key == "overview" ? "/merchant" : $"/merchant/{Slug}";
}

/// <summary>
/// The dashboard's sections: their addresses (contract C2), menu placement and layout width.
/// Order is the order of the wide-screen rail.
/// </summary>
public static class DashboardSections
{
    public static readonly IReadOnlyList<DashboardSection> All =
    [
        new("overview",  "overview",  "Overview",        "dashboard", 1, false, false),
        new("new",       "new",       "New payment",     "plus",      3, true,  true),
        new("sessions",  "sessions",  "Sessions",        "list",      2, false, false),
        new("links",     "links",     "Payment links",   "link",      4, false, false),
        new("analytics", "analytics", "Analytics",       "chart",     0, false, false),
        new("donation",  "donation",  "Donation button", "gift",      0, false, false),
        new("webhook",   "webhook",   "Webhook",         "bell",      0, false, false),
        new("stores",    "stores",    "Stores",          "store",     0, false, false),
        new("apikeys",   "api-keys",  "API keys",        "key",       0, false, false),
        new("audit",     "audit",     "Audit log",       "history",   0, false, false),
        new("pos",       "pos",       "POS mode",        "zap",       0, false, false),
        new("settings",  "settings",  "Settings",        "settings",  0, false, true),
    ];

    /// <summary>The phone bottom menu, left to right: Overview, Sessions, New payment (raised), Payment links. "More" follows.</summary>
    public static IReadOnlyList<DashboardSection> BottomMenu { get; } =
        All.Where(s => s.BottomSlot > 0).OrderBy(s => s.BottomSlot).ToArray();

    /// <summary>Sections listed in the More sheet.</summary>
    public static IReadOnlyList<DashboardSection> More { get; } = All.Where(s => s.BottomSlot == 0).ToArray();

    /// <summary>The section for a URL segment; null (or empty) segment is Overview; unknown is null.</summary>
    public static DashboardSection? ForSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return All[0];
        return All.FirstOrDefault(s => s.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    }

    public static DashboardSection ForKey(string key) =>
        All.FirstOrDefault(s => s.Key == key) ?? All[0];
}
