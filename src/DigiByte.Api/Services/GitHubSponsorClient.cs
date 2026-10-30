using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Services;

/// <summary>A GitHub call failed. The message carries only a status code: never the token, the response body, or submitted content.</summary>
public sealed class GitHubApiException(string message) : Exception(message);

/// <summary>The few GitHub operations a sponsor submission needs. Every write goes to a <c>submission/*</c> branch; nothing here can merge.</summary>
public interface IGitHubSponsorClient
{
    Task<string> GetBranchHeadAsync(CancellationToken ct);
    /// <returns>The entry id a donation transaction already backs, or null if the transaction is unused.</returns>
    Task<string?> ReadClaimAsync(string txId, CancellationToken ct);
    Task<string?> ReadFileTextAsync(string path, CancellationToken ct);
    Task<int> CountOpenSubmissionsAsync(CancellationToken ct);
    /// <returns>False when the branch already exists.</returns>
    Task<bool> CreateBranchAsync(string branch, string baseSha, CancellationToken ct);
    Task PutFileAsync(string branch, string path, byte[] content, string message, CancellationToken ct);
    Task DeleteFileAsync(string branch, string path, string message, CancellationToken ct);
    Task<int> CreatePullRequestAsync(string head, string title, string body, string[] labels, CancellationToken ct);
    Task DeleteBranchAsync(string branch, CancellationToken ct);
}

/// <summary>
/// GitHub REST client for the sponsors branch. The bot token comes from <see cref="SponsorOptions.GitHubToken"/> (environment only)
/// and is sent only in the Authorization header. When a call fails, the server log gets GitHub's own status, message and rate-limit and
/// permission headers (never the token and never submitted content) so an operator can tell a rate limit from a missing permission;
/// the exception and the visitor only ever see a status code. See specs/006-sponsors-page/contracts/submission-api.md.
/// </summary>
public sealed class GitHubSponsorClient(
    HttpClient http,
    IOptions<SponsorOptions> options,
    IMemoryCache cache,
    ILogger<GitHubSponsorClient> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IGitHubSponsorClient
{
    private const string OpenCountKey = "sponsors:open-submissions";
    private static readonly TimeSpan OpenCountTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(10);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private SponsorOptions Settings => options.Value;
    private string Repo => Settings.Repository;

    public async Task<string> GetBranchHeadAsync(CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{Repo}/git/ref/heads/{Settings.Branch}", null, ct);
        await EnsureAsync(response, "read the sponsors branch head");
        using var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.GetProperty("object").GetProperty("sha").GetString()
               ?? throw new GitHubApiException("GitHub answer was unreadable");
    }

    public async Task<string?> ReadClaimAsync(string txId, CancellationToken ct)
    {
        var text = await ReadFileAtAsync($"data/donations/{txId.ToLowerInvariant()}.txt", Settings.Branch, ct);
        var id = text?.Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public Task<string?> ReadFileTextAsync(string path, CancellationToken ct) => ReadFileAtAsync(path, Settings.Branch, ct);

    private async Task<string?> ReadFileAtAsync(string path, string reference, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{Repo}/contents/{path}?ref={Uri.EscapeDataString(reference)}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(response, "read a file");
        using var doc = await ReadJsonAsync(response, ct);
        var content = doc.RootElement.GetProperty("content").GetString() ?? "";
        return Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
    }

    public async Task<int> CountOpenSubmissionsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(OpenCountKey, out int cached)) return cached;
        using var response = await SendAsync(HttpMethod.Get, $"repos/{Repo}/pulls?state=open&base={Uri.EscapeDataString(Settings.Branch)}&per_page=100", null, ct);
        await EnsureAsync(response, "list open pull requests");
        using var doc = await ReadJsonAsync(response, ct);
        var count = doc.RootElement.EnumerateArray()
            .Count(pr => pr.GetProperty("head").GetProperty("ref").GetString()?.StartsWith("submission/", StringComparison.Ordinal) == true);
        cache.Set(OpenCountKey, count, OpenCountTtl);
        return count;
    }

    public async Task<bool> CreateBranchAsync(string branch, string baseSha, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{Repo}/git/refs", new { @ref = $"refs/heads/{branch}", sha = baseSha }, ct);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return false;
        await EnsureAsync(response, "create the submission branch");
        cache.Remove(OpenCountKey);
        return true;
    }

    public async Task PutFileAsync(string branch, string path, byte[] content, string message, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Put, $"repos/{Repo}/contents/{path}",
            new { message, content = Convert.ToBase64String(content), branch }, ct);
        await EnsureAsync(response, "commit a file");
    }

    public async Task DeleteFileAsync(string branch, string path, string message, CancellationToken ct)
    {
        string sha;
        using (var get = await SendAsync(HttpMethod.Get, $"repos/{Repo}/contents/{path}?ref={Uri.EscapeDataString(branch)}", null, ct))
        {
            if (get.StatusCode == HttpStatusCode.NotFound) return;
            await EnsureAsync(get, "read a file to delete");
            using var doc = await ReadJsonAsync(get, ct);
            sha = doc.RootElement.GetProperty("sha").GetString() ?? throw new GitHubApiException("GitHub answer was unreadable");
        }
        using var response = await SendAsync(HttpMethod.Delete, $"repos/{Repo}/contents/{path}", new { message, sha, branch }, ct);
        await EnsureAsync(response, "delete a file");
    }

    public async Task<int> CreatePullRequestAsync(string head, string title, string body, string[] labels, CancellationToken ct)
    {
        int number;
        using (var response = await SendAsync(HttpMethod.Post, $"repos/{Repo}/pulls", new { title, head, @base = Settings.Branch, body }, ct))
        {
            await EnsureAsync(response, "open the pull request");
            using var doc = await ReadJsonAsync(response, ct);
            number = doc.RootElement.GetProperty("number").GetInt32();
        }
        cache.Remove(OpenCountKey);
        // Labels help the maintainer filter; a labelling failure must not undo a pull request that exists.
        try
        {
            using var labelResponse = await SendAsync(HttpMethod.Post, $"repos/{Repo}/issues/{number}/labels", new { labels }, ct);
        }
        catch (Exception) { }
        return number;
    }

    public async Task DeleteBranchAsync(string branch, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"repos/{Repo}/git/refs/heads/{branch}", null, ct);
        cache.Remove(OpenCountKey);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.UnprocessableEntity) return;
        await EnsureAsync(response, "delete the submission branch");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = Settings.GitHubToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("GitHub credential is not configured: set the environment variable Sponsors__GitHubToken on the API host");
            throw new GitHubApiException("GitHub credential is not configured");
        }

        var response = await SendOnceAsync(method, path, body, token, ct);
        // GitHub asks callers that hit a rate limit to wait for Retry-After and try again. Do that once, and only for a short wait.
        if (WaitHint(response) is { } wait && wait <= MaxRetryWait)
        {
            logger.LogWarning("GitHub asked us to wait {Seconds}s (status {Status}); retrying once", (int)wait.TotalSeconds, (int)response.StatusCode);
            response.Dispose();
            await _delay(wait, ct);
            response = await SendOnceAsync(method, path, body, token, ct);
        }
        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }

    /// <summary>How long GitHub asks us to wait, for a rate-limit answer (429, or 403 with Retry-After); otherwise null.</summary>
    private static TimeSpan? WaitHint(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests && response.StatusCode != HttpStatusCode.Forbidden) return null;
        var wait = response.Headers.RetryAfter?.Delta;
        return wait is { } w && w > TimeSpan.Zero ? w : null;
    }

    private async Task EnsureAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await ReadErrorMessageAsync(response);
        logger.LogWarning(
            "GitHub could not {Operation}: answered {Status} \"{Message}\" (rate limit remaining {Remaining}, retry after {RetryAfter}, permissions this call accepts {Accepted}, token scopes {Scopes})",
            operation, (int)response.StatusCode, message,
            Header(response, "x-ratelimit-remaining"), Header(response, "retry-after"),
            Header(response, "x-accepted-github-permissions"), Header(response, "x-oauth-scopes"));
        throw new GitHubApiException($"GitHub answered {(int)response.StatusCode}");
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "-";

    /// <summary>GitHub's own error text (for example "Resource not accessible by personal access token"), made safe for a log line.</summary>
    private async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (text.Length > 4000) text = text[..4000];
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { } msg) text = msg;
            }
            catch (JsonException) { }
            var token = Settings.GitHubToken;
            if (!string.IsNullOrEmpty(token)) text = text.Replace(token, "[token]");
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('"', '\'');
            return text.Length > 300 ? text[..300] : text;
        }
        catch (Exception)
        {
            return "(no readable message)";
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
}
