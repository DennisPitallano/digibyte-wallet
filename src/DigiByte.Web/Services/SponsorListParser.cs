using System.Text.Json;
using DigiByte.P2P.Shared.Sponsors;

namespace DigiByte.Web.Services;

public sealed record SponsorListParseResult(bool Readable, IReadOnlyList<SponsorEntry> Entries, int SkippedCount);

/// <summary>
/// Turns the sponsors list text into display entries. The text is public and self-submitted, so nothing here may throw
/// or trust the input: every entry is checked with <see cref="SponsorRules"/> on its own and a bad entry never affects the rest.
/// Order in the file is oldest first; the result is newest first. See specs/006-sponsors-page/data-model.md.
/// </summary>
public static class SponsorListParser
{
    private static readonly SponsorListParseResult Unreadable = new(false, [], 0);

    public static SponsorListParseResult Parse(string? json, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(json)) return Unreadable;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unreadable;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var v) || v != 1) return Unreadable;
            if (!root.TryGetProperty("sponsors", out var list) || list.ValueKind != JsonValueKind.Array) return Unreadable;

            var imageBase = (baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") + "images/";
            var items = list.EnumerateArray().ToList();
            // More than the cap: keep the newest (last in the file).
            if (items.Count > SponsorRules.MaxListEntries)
                items = items.GetRange(items.Count - SponsorRules.MaxListEntries, SponsorRules.MaxListEntries);

            var kept = new List<SponsorEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var skipped = 0;
            foreach (var item in items)
            {
                var entry = ParseEntry(item, imageBase, out var imageName);
                if (entry is null) { skipped++; continue; }
                var key = entry.Name.ToLowerInvariant() + "\n" + (entry.Link ?? "") + "\n" + (imageName ?? "");
                if (!seen.Add(key)) { skipped++; continue; }
                kept.Add(entry);
            }
            kept.Reverse();
            return new SponsorListParseResult(true, kept, skipped);
        }
        catch (Exception)
        {
            return Unreadable;
        }
    }

    private static SponsorEntry? ParseEntry(JsonElement item, string imageBase, out string? imageName)
    {
        imageName = null;
        if (item.ValueKind != JsonValueKind.Object) return null;
        if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) return null;
        if (!SponsorRules.TryNormalizeName(nameEl.GetString(), out var name)) return null;

        // A bad link or picture is dropped; the sponsor still appears with their name.
        string? link = null;
        if (item.TryGetProperty("link", out var linkEl) && linkEl.ValueKind == JsonValueKind.String &&
            SponsorRules.IsValidLink(linkEl.GetString()))
            link = linkEl.GetString();

        string? imageUrl = null;
        if (item.TryGetProperty("image", out var imgEl) && imgEl.ValueKind == JsonValueKind.String &&
            SponsorRules.IsValidListImageName(imgEl.GetString()))
        {
            imageName = imgEl.GetString();
            imageUrl = imageBase + imageName;
        }
        // The generated list counts each sponsor's donations; anything unreadable or out of range counts as one.
        var donations = 1;
        if (item.TryGetProperty("donations", out var countEl) && countEl.ValueKind == JsonValueKind.Number &&
            countEl.TryGetInt32(out var count) && count is >= 1 and <= 9999)
            donations = count;
        return new SponsorEntry(name, imageUrl, link, donations);
    }
}
