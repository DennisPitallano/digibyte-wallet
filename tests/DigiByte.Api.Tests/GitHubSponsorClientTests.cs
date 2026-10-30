using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Tests;

public class GitHubSponsorClientTests
{
    private const string Token = "ghp_SECRET_TOKEN_VALUE_123";
    private static readonly string Tx = new string('a', 64);

    private static (GitHubSponsorClient client, ScriptedHandler handler) Create(string? token = Token)
    {
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") };
        var options = Options.Create(new SponsorOptions { Repository = "owner/repo", Branch = "sponsors", GitHubToken = token });
        return (new GitHubSponsorClient(http, options, new MemoryCache(new MemoryCacheOptions())), handler);
    }

    [Fact]
    public async Task GetBranchHead_ReadsTheRefSha()
    {
        var (client, h) = Create();
        h.On("GET", "/repos/owner/repo/git/ref/heads/sponsors", HttpStatusCode.OK, """{"object":{"sha":"abc123"}}""");
        Assert.Equal("abc123", await client.GetBranchHeadAsync(default));
    }

    [Fact]
    public async Task CreateBranch_Created_ReturnsTrue_AndSendsRefAndSha()
    {
        var (client, h) = Create();
        h.On("POST", "/repos/owner/repo/git/refs", HttpStatusCode.Created, "{}");
        Assert.True(await client.CreateBranchAsync("submission/aaaa", "abc123", default));
        var body = JsonDocument.Parse(h.Requests.Single().Body!).RootElement;
        Assert.Equal("refs/heads/submission/aaaa", body.GetProperty("ref").GetString());
        Assert.Equal("abc123", body.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task CreateBranch_AlreadyExists_ReturnsFalse()
    {
        var (client, h) = Create();
        h.On("POST", "/repos/owner/repo/git/refs", HttpStatusCode.UnprocessableEntity, """{"message":"Reference already exists"}""");
        Assert.False(await client.CreateBranchAsync("submission/aaaa", "abc123", default));
    }

    [Fact]
    public async Task ReadClaim_NotFound_ReturnsNull_AndFound_ReturnsTheEntryId()
    {
        var (client, h) = Create();
        h.On("GET", $"/repos/owner/repo/contents/data/donations/{Tx}.txt", HttpStatusCode.NotFound, "{}");
        Assert.Null(await client.ReadClaimAsync(Tx, default));

        var (client2, h2) = Create();
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789ab\n"));
        h2.On("GET", $"/repos/owner/repo/contents/data/donations/{Tx}.txt", HttpStatusCode.OK, $$"""{"content":"{{content}}","encoding":"base64"}""");
        Assert.Equal("0123456789ab", await client2.ReadClaimAsync(Tx, default));
        Assert.Contains("ref=sponsors", h2.Requests.Single().Query);
    }

    [Fact]
    public async Task PutFile_SendsBase64ContentOnTheBranch()
    {
        var (client, h) = Create();
        h.On("PUT", "/repos/owner/repo/contents/data/entries/x.json", HttpStatusCode.Created, "{}");
        await client.PutFileAsync("submission/aaaa", "data/entries/x.json", "{\"a\":1}"u8.ToArray(), "msg", default);
        var body = JsonDocument.Parse(h.Requests.Single().Body!).RootElement;
        Assert.Equal("submission/aaaa", body.GetProperty("branch").GetString());
        Assert.Equal("msg", body.GetProperty("message").GetString());
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(Convert.FromBase64String(body.GetProperty("content").GetString()!)));
    }

    [Fact]
    public async Task DeleteFile_FetchesShaThenDeletes_AndSkipsMissingFiles()
    {
        var (client, h) = Create();
        h.On("GET", "/repos/owner/repo/contents/data/entries/x.json", HttpStatusCode.OK, """{"sha":"filesha"}""");
        h.On("DELETE", "/repos/owner/repo/contents/data/entries/x.json", HttpStatusCode.OK, "{}");
        await client.DeleteFileAsync("submission/rm", "data/entries/x.json", "msg", default);
        var del = h.Requests.Single(r => r.Method == "DELETE");
        Assert.Equal("filesha", JsonDocument.Parse(del.Body!).RootElement.GetProperty("sha").GetString());

        var (client2, h2) = Create();
        h2.On("GET", "/repos/owner/repo/contents/data/images/y.png", HttpStatusCode.NotFound, "{}");
        await client2.DeleteFileAsync("submission/rm", "data/images/y.png", "msg", default);
        Assert.DoesNotContain(h2.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task CreatePullRequest_OpensIntoSponsors_AndAddsLabels()
    {
        var (client, h) = Create();
        h.On("POST", "/repos/owner/repo/pulls", HttpStatusCode.Created, """{"number":42}""");
        h.On("POST", "/repos/owner/repo/issues/42/labels", HttpStatusCode.OK, "[]");
        var n = await client.CreatePullRequestAsync("submission/aaaa", "Sponsor submission: Alice", "body", ["sponsor-submission", "donation-verified"], default);
        Assert.Equal(42, n);
        var pr = JsonDocument.Parse(h.Requests.First(r => r.Path.EndsWith("/pulls")).Body!).RootElement;
        Assert.Equal("sponsors", pr.GetProperty("base").GetString());
        Assert.Equal("submission/aaaa", pr.GetProperty("head").GetString());
        var labels = JsonDocument.Parse(h.Requests.Last().Body!).RootElement.GetProperty("labels").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(["sponsor-submission", "donation-verified"], labels);
    }

    [Fact]
    public async Task CountOpenSubmissions_CountsOnlySubmissionBranches_AndIsCached()
    {
        var (client, h) = Create();
        h.On("GET", "/repos/owner/repo/pulls", HttpStatusCode.OK,
            """[{"head":{"ref":"submission/aaaa"}},{"head":{"ref":"submission/rm-bbbb"}},{"head":{"ref":"feature/other"}}]""");
        Assert.Equal(2, await client.CountOpenSubmissionsAsync(default));
        Assert.Equal(2, await client.CountOpenSubmissionsAsync(default));
        Assert.Single(h.Requests); // second call served from the 60-second cache
    }

    [Fact]
    public async Task Token_IsOnlyInTheAuthorizationHeader()
    {
        var (client, h) = Create();
        h.On("GET", "/repos/owner/repo/git/ref/heads/sponsors", HttpStatusCode.OK, """{"object":{"sha":"abc123"}}""");
        await client.GetBranchHeadAsync(default);
        var r = h.Requests.Single();
        Assert.Equal($"Bearer {Token}", r.Authorization);
        Assert.DoesNotContain(Token, r.Path + r.Query + (r.Body ?? ""));
    }

    [Fact]
    public async Task GitHubErrors_ThrowAnExceptionThatNeverContainsTheTokenOrBody()
    {
        var (client, h) = Create();
        h.On("GET", "/repos/owner/repo/git/ref/heads/sponsors", HttpStatusCode.Unauthorized, $$"""{"message":"Bad credentials {{Token}}"}""");
        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetBranchHeadAsync(default));
        Assert.DoesNotContain(Token, ex.ToString());
        Assert.DoesNotContain("Bad credentials", ex.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingToken_Throws_WithoutCallingGitHub(string? token)
    {
        var (client, h) = Create(token);
        await Assert.ThrowsAsync<GitHubApiException>(() => client.GetBranchHeadAsync(default));
        Assert.Empty(h.Requests);
    }

    // ── test double ──
    private sealed record Seen(string Method, string Path, string Query, string? Body, string? Authorization);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly List<(string Method, string Path, HttpStatusCode Status, string Body)> _routes = [];
        public List<Seen> Requests { get; } = [];

        public void On(string method, string path, HttpStatusCode status, string body) => _routes.Add((method, path, status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new Seen(request.Method.Method, path, request.RequestUri.Query, body, request.Headers.Authorization?.ToString()));
            var route = _routes.FirstOrDefault(r => r.Method == request.Method.Method && r.Path == path);
            return route.Method is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
        }
    }
}
