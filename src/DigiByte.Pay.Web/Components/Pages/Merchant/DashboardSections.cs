namespace DigiByte.Pay.Web.Components.Pages.Merchant;

/// <summary>One section of the merchant dashboard (specs/032-digipay-ui-refresh/data-model.md §3).</summary>
/// <param name="Key">Internal key used by the dashboard's markup.</param>
/// <param name="Slug">The URL segment: /merchant/{Slug}.</param>
/// <param name="BottomSlot">Position in the phone bottom menu (1–4), or 0 when the section is under More.</param>
/// <param name="Raised">The bottom menu's raised main button.</param>
/// <param name="Narrow">Forms and single-item pages use the wallet's narrow column (clarification, second pass).</param>
/// <param name="Group">The heading the section is listed under in the rail and the More sheet.</param>
public sealed record DashboardSection(string Key, string Slug, string Label, string Icon, int BottomSlot, bool Raised, bool Narrow, string Group)
{
    public string Href => Key == "overview" ? "/merchant" : $"/merchant/{Slug}";
}

/// <summary>
/// The dashboard's sections: their addresses (contract C2), menu placement and layout width.
/// Order is the order of the wide-screen rail, grouped under <see cref="GroupNames"/>.
/// Labels can change; slugs never do, so bookmarks keep working (Sessions is labelled Payments).
/// </summary>
public static class DashboardSections
{
    public const string Payments = "Payments";
    public const string Insights = "Insights";
    public const string Developers = "Developers";
    public const string Account = "Account";

    /// <summary>The menu headings, in order.</summary>
    public static readonly IReadOnlyList<string> GroupNames = [Payments, Insights, Developers, Account];

    public static readonly IReadOnlyList<DashboardSection> All =
    [
        new("overview",  "overview",  "Overview",        "dashboard", 1, false, false, Payments),
        new("new",       "new",       "New payment",     "plus",      3, true,  true,  Payments),
        new("sessions",  "sessions",  "Payments",        "list",      2, false, false, Payments),
        new("links",     "links",     "Payment links",   "link",      4, false, false, Payments),
        new("pos",       "pos",       "POS tablets",     "zap",       0, false, false, Payments),
        new("donation",  "donation",  "Donation button", "gift",      0, false, false, Payments),
        new("analytics", "analytics", "Analytics",       "chart",     0, false, false, Insights),
        new("apikeys",   "api-keys",  "API keys",        "key",       0, false, false, Developers),
        new("webhook",   "webhook",   "Webhooks",        "bell",      0, false, false, Developers),
        new("stores",    "stores",    "Stores",          "store",     0, false, false, Account),
        new("settings",  "settings",  "Settings",        "settings",  0, false, true,  Account),
        new("audit",     "audit",     "Audit log",       "history",   0, false, false, Account),
    ];

    /// <summary>The phone bottom menu, left to right: Overview, Payments, New payment (raised), Payment links. "More" follows.</summary>
    public static IReadOnlyList<DashboardSection> BottomMenu { get; } =
        All.Where(s => s.BottomSlot > 0).OrderBy(s => s.BottomSlot).ToArray();

    /// <summary>Sections listed in the More sheet.</summary>
    public static IReadOnlyList<DashboardSection> More { get; } = All.Where(s => s.BottomSlot == 0).ToArray();

    /// <summary>The given sections under their headings, in menu order; empty groups are left out.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<DashboardSection> Sections)> Grouped(IEnumerable<DashboardSection> sections)
    {
        var list = sections.ToList();
        return GroupNames
            .Select(g => (g, (IReadOnlyList<DashboardSection>)list.Where(s => s.Group == g).ToArray()))
            .Where(x => x.Item2.Count > 0)
            .ToArray();
    }

    /// <summary>The section for a URL segment; null (or empty) segment is Overview; unknown is null.</summary>
    public static DashboardSection? ForSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return All[0];
        return All.FirstOrDefault(s => s.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    }

    public static DashboardSection ForKey(string key) =>
        All.FirstOrDefault(s => s.Key == key) ?? All[0];
}
