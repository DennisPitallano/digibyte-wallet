using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigiByte.P2P.Shared.Sponsors;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Services;

/// <summary>One form submission as it reaches the server (after the endpoint has read the multipart body).</summary>
public sealed class SponsorSubmissionRequest
{
    public SponsorSubmissionType Type { get; init; }
    public string? Name { get; init; }
    public string? Link { get; init; }
    public string? TxId { get; init; }
    public bool Consent { get; init; }
    public string? Website { get; init; }
    public byte[]? Image { get; init; }
}

/// <summary>
/// Turns a valid, donation-backed submission into one pull request on the sponsors branch, and nothing else. It never merges,
/// never writes to <c>main</c>, and never publishes: a maintainer reviews every pull request. It logs only the branch prefix,
/// the type and the outcome, never the token, the submitted content or the full transaction ID.
/// See specs/006-sponsors-page/contracts/submission-api.md for the order of steps.
/// </summary>
public sealed class SponsorSubmissionService(
    IGitHubSponsorClient github,
    IDonationVerifier donations,
    IOptions<SponsorOptions> options,
    ILogger<SponsorSubmissionService> logger)
{
    private static readonly JsonSerializerOptions EntryJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static SponsorSubmissionResult Of(SponsorSubmissionState state, params string[] fields) =>
        new() { State = state, Fields = fields.Length > 0 ? fields : null };

    public async Task<SponsorSubmissionResult> SubmitAsync(SponsorSubmissionRequest request, CancellationToken ct)
    {
        // A hidden field only bots fill in: look successful, do nothing.
        if (!string.IsNullOrWhiteSpace(request.Website)) return Of(SponsorSubmissionState.Created);

        try
        {
            var result = request.Type == SponsorSubmissionType.Remove
                ? await RemoveAsync(request, ct)
                : await AddAsync(request, ct);
            Log(request, result.State);
            return result;
        }
        catch (Exception ex) when (ex is GitHubApiException or DonationLookupUnavailableException or HttpRequestException or TaskCanceledException)
        {
            // These messages are status codes and fixed phrases only (no token, no submitted content), so they are safe to log.
            logger.LogWarning("Sponsor submission {Type} could not complete: {ErrorType}: {Reason}", request.Type, ex.GetType().Name, ex.Message);
            Log(request, SponsorSubmissionState.Unavailable);
            return Of(SponsorSubmissionState.Unavailable);
        }
    }

    // ── add ──

    /// <summary>True when a listed sponsor already has this name (ignoring case and extra spaces). A missing or unreadable list does not block.</summary>
    private async Task<bool> NameIsListedAsync(string name, CancellationToken ct)
    {
        var text = await github.ReadFileTextAsync("data/sponsors.json", ct);
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("sponsors", out var list) || list.ValueKind != JsonValueKind.Array) return false;
            var key = SponsorRules.NameKey(name);
            foreach (var entry in list.EnumerateArray())
                if (entry.TryGetProperty("name", out var n) && n.GetString() is { } listed && SponsorRules.NameKey(listed) == key) return true;
        }
        catch (JsonException) { }
        return false;
    }

    private async Task<SponsorSubmissionResult> AddAsync(SponsorSubmissionRequest request, CancellationToken ct)
    {
        var invalid = new List<string>();
        if (!request.Consent) invalid.Add("consent");
        if (!SponsorRules.TryNormalizeName(request.Name, out var name)) invalid.Add("name");
        var link = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim();
        if (link is not null && !SponsorRules.IsValidLink(link)) invalid.Add("link");
        if (!SponsorRules.IsValidTxId(request.TxId)) invalid.Add("txid");

        var imageType = SponsorImageType.None;
        var image = request.Image is { Length: > 0 } ? request.Image : null;
        if (image is not null)
        {
            imageType = SponsorRules.DetectImageType(image);
            if (imageType == SponsorImageType.None || !SponsorRules.IsImageSizeOk(image.Length)) invalid.Add("image");
        }
        if (invalid.Count > 0) return Of(SponsorSubmissionState.Invalid, [.. invalid]);

        var txId = request.TxId!.ToLowerInvariant();
        if (await github.ReadClaimAsync(txId, ct) is not null) return Of(SponsorSubmissionState.AlreadyUsed);
        if (await NameIsListedAsync(name, ct)) return Of(SponsorSubmissionState.NameTaken, "name");
        if (await github.CountOpenSubmissionsAsync(ct) >= options.Value.MaxOpenSubmissions) return Of(SponsorSubmissionState.QueueFull);

        var check = await donations.VerifyAsync(txId, ct);
        if (!check.Ok) return new SponsorSubmissionResult { State = SponsorSubmissionState.NotVerified, Reason = check.Reason };

        var id = SponsorRules.ComputeEntryId(name, link, image ?? []);
        var imageName = image is null ? null : $"{id}.{SponsorRules.ImageExtension(imageType)}";
        var entryJson = JsonSerializer.Serialize(new EntryFile(id, name, imageName, link), EntryJson) + "\n";

        var branch = $"submission/{SponsorRules.BranchSuffix(txId)}";
        var sha = await github.GetBranchHeadAsync(ct);
        if (!await github.CreateBranchAsync(branch, sha, ct)) return Of(SponsorSubmissionState.AlreadyUsed);

        try
        {
            await github.PutFileAsync(branch, $"data/entries/{id}.json", Encoding.UTF8.GetBytes(entryJson), $"Add sponsor entry {id}", ct);
            await github.PutFileAsync(branch, $"data/donations/{txId}.txt", Encoding.UTF8.GetBytes(id + "\n"), $"Claim donation for entry {id}", ct);
            if (image is not null)
                await github.PutFileAsync(branch, $"data/images/{imageName}", image, $"Add picture for entry {id}", ct);

            await github.CreatePullRequestAsync(branch, $"Sponsor submission: {SanitiseForTitle(name)}", AddBody(txId, id, name, link, imageName),
                ["sponsor-submission", "donation-verified"], ct);
        }
        catch
        {
            await TryDeleteBranchAsync(branch);
            throw;
        }
        return Of(SponsorSubmissionState.Created);
    }

    // ── remove ──

    private async Task<SponsorSubmissionResult> RemoveAsync(SponsorSubmissionRequest request, CancellationToken ct)
    {
        if (!SponsorRules.IsValidTxId(request.TxId)) return Of(SponsorSubmissionState.Invalid, "txid");
        var txId = request.TxId!.ToLowerInvariant();

        // The claim file proves which entry this donation created; a transaction that never created one opens nothing.
        var entryId = await github.ReadClaimAsync(txId, ct);
        if (entryId is null || !IsEntryId(entryId)) return Of(SponsorSubmissionState.Invalid, "txid");
        if (await github.CountOpenSubmissionsAsync(ct) >= options.Value.MaxOpenSubmissions) return Of(SponsorSubmissionState.QueueFull);

        var entryPath = $"data/entries/{entryId}.json";
        var entryText = await github.ReadFileTextAsync(entryPath, ct);
        // The claim outlives the entry, so a second removal request finds a claim but no entry: nothing left to remove.
        if (entryText is null) return Of(SponsorSubmissionState.Invalid, "txid");
        string? name = null, imageName = null;
        {
            try
            {
                using var doc = JsonDocument.Parse(entryText);
                name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                var img = doc.RootElement.TryGetProperty("image", out var i) ? i.GetString() : null;
                if (SponsorRules.IsValidListImageName(img)) imageName = img;
            }
            catch (JsonException) { }
        }

        var branch = $"submission/rm-{SponsorRules.BranchSuffix(txId)}";
        var sha = await github.GetBranchHeadAsync(ct);
        if (!await github.CreateBranchAsync(branch, sha, ct)) return Of(SponsorSubmissionState.AlreadyUsed);

        try
        {
            await github.DeleteFileAsync(branch, entryPath, $"Remove sponsor entry {entryId}", ct);
            if (imageName is not null)
                await github.DeleteFileAsync(branch, $"data/images/{imageName}", $"Remove picture for entry {entryId}", ct);

            var shown = name is not null && SponsorRules.TryNormalizeName(name, out var n2) ? SanitiseForTitle(n2) : entryId;
            await github.CreatePullRequestAsync(branch, $"Sponsor removal request: {shown}",
                $"Removal requested with the donation transaction that created entry `{entryId}` (`{txId}`).\n\n" +
                "Merging this pull request removes the entry and its picture. The donation claim file stays, so the transaction cannot be used again.",
                ["sponsor-submission", "removal-request"], ct);
        }
        catch
        {
            await TryDeleteBranchAsync(branch);
            throw;
        }
        return Of(SponsorSubmissionState.Created);
    }

    // ── helpers ──

    private static string AddBody(string txId, string id, string name, string? link, string? imageName) =>
        $"""
        Sponsor submission (add). Everything below is public.

        - Entry id: `{id}`
        - Name (shown as plain text): `{name.Replace('`', '\'')}`
        - Link: {(link is null ? "none" : $"`{link}`")}
        - Picture: {(imageName is null ? "none" : $"`{imageName}`")}
        - Donation transaction: `{txId}` (checked automatically: found on the explorers, enough confirmations, paid to a project donation address, at least the minimum amount, not used before)
        - Consent: the submitter confirmed they are the person named, or have that person's permission to be listed.

        Maintainer: check that the name, link and picture are acceptable, then merge. Close without merging to reject; closing frees the transaction.
        """;

    private static string SanitiseForTitle(string name)
    {
        var sb = new StringBuilder();
        var lastWasSpace = false;
        foreach (var c in name)
        {
            if (char.IsControl(c) && !char.IsWhiteSpace(c)) continue; // drop control characters
            if (char.IsWhiteSpace(c)) { if (!lastWasSpace) sb.Append(' '); lastWasSpace = true; continue; }
            sb.Append(c);
            lastWasSpace = false;
        }
        return sb.ToString().Trim();
    }

    private static bool IsEntryId(string id) => id.Length == 12 && id.All(c => Uri.IsHexDigit(c) && !char.IsUpper(c));

    private async Task TryDeleteBranchAsync(string branch)
    {
        try { await github.DeleteBranchAsync(branch, CancellationToken.None); }
        catch (Exception) { /* best effort */ }
    }

    private void Log(SponsorSubmissionRequest request, SponsorSubmissionState state)
    {
        var prefix = SponsorRules.IsValidTxId(request.TxId) ? SponsorRules.BranchSuffix(request.TxId!)[..8] : "-";
        logger.LogInformation("Sponsor submission {Type} {Prefix} -> {State}", request.Type, prefix, state);
    }

    private sealed record EntryFile(string Id, string Name, string? Image, string? Link);
}
