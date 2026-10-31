namespace DigiByte.Web.Services;

/// <summary>A validated sponsor as the page shows it. Built only from list entries that passed every rule.</summary>
/// <param name="Donations">How many verified donations this sponsor has made (1 when the list does not say).</param>
public sealed record SponsorEntry(string Name, string? ImageUrl, string? Link, int Donations = 1);

/// <summary>The sponsors in display order (newest first), with when they were fetched.</summary>
public sealed class SponsorList
{
    public required IReadOnlyList<SponsorEntry> Entries { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>Entries dropped for being invalid or duplicates (diagnostics only).</summary>
    public int SkippedCount { get; init; }
}

public enum SponsorLoadState
{
    /// <summary>Fetched and parsed just now.</summary>
    Fresh,
    /// <summary>Fetched and parsed just now, but no valid entries.</summary>
    FreshEmpty,
    /// <summary>The fetch failed; the last good copy is shown.</summary>
    Stale,
    /// <summary>The fetch failed and there is no copy.</summary>
    Unavailable,
}

public sealed class SponsorLoadResult
{
    public required SponsorLoadState State { get; init; }
    public SponsorList? List { get; init; }
}
