namespace DigiByte.Web.Layout;

/// <summary>Which menu a page gets.</summary>
public enum ShellKind
{
    /// <summary>Wallet, Activity, Scan, Request, More.</summary>
    Personal,
    /// <summary>Overview, Activity, Scan, Pending, More, all acting on one shared wallet.</summary>
    Shared,
}

/// <summary>The shared-wallet tab a page belongs to.</summary>
public enum SharedSection
{
    /// <summary>A page of the shared wallet that is no tab's own, such as Send.</summary>
    None,
    Overview,
    Activity,
    Pending,
}

/// <summary>
/// Whether a page is inside one shared wallet, decided from its address alone so the menu and the page can never
/// disagree, including after a reload or a link straight to the page (specs/026-multisig-wallet-experience, FR-002
/// and FR-006). This is the only place that knows which addresses are "inside a shared wallet".
/// </summary>
public sealed record ShellContext(ShellKind Kind, string? SharedWalletId, SharedSection Section)
{
    public static readonly ShellContext Personal = new(ShellKind.Personal, null, SharedSection.None);

    // First segments under /multisig that are pages of their own, not a shared wallet's id.
    private static readonly string[] Reserved = ["create", "import", "room", "sign-room"];

    public bool IsShared => Kind == ShellKind.Shared;

    /// <param name="path">A base-relative address. Leading and trailing slashes, a query and a fragment are ignored.
    /// It must not have been lower-cased: a shared wallet's id is returned exactly as written.</param>
    public static ShellContext Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Personal;

        var clean = path.Split('?', '#')[0].Trim().Trim('/');
        var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("multisig", StringComparison.OrdinalIgnoreCase)) return Personal;
        if (Reserved.Contains(parts[1], StringComparer.OrdinalIgnoreCase)) return Personal;

        var id = Uri.UnescapeDataString(parts[1]);
        var section = parts.Length == 2
            ? SharedSection.Overview
            : parts[2].ToLowerInvariant() switch
            {
                "activity" => SharedSection.Activity,
                "pending" => SharedSection.Pending,
                _ => SharedSection.None,
            };
        return new ShellContext(ShellKind.Shared, id, section);
    }
}
