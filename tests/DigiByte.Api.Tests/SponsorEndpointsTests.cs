using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Api.Endpoints;
using DigiByte.Api.Services;
using DigiByte.P2P.Shared.Sponsors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DigiByte.Api.Tests;

/// <summary>The in-process API: health smoke test, then the submission endpoint's status mapping and its secrecy guarantees.</summary>
public class SponsorEndpointsTests
{
    private const string Secret = "ghp_SECRET_BOT_TOKEN_9876";
    private static readonly string Tx = new('c', 64);

    private static Factory NewFactory(int perHour = 1000) => new(perHour);

    private static MultipartFormDataContent Form(string name = "Alice", string txid = "", bool consent = true, string type = "add",
        byte[]? image = null, string? link = "https://alice.example")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(type), "type" },
            { new StringContent(name), "name" },
            { new StringContent(txid.Length == 0 ? Tx : txid), "txid" },
            { new StringContent(consent ? "true" : "false"), "consent" },
        };
        if (link is not null) form.Add(new StringContent(link), "link");
        if (image is not null)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new("image/png");
            form.Add(file, "image", "pic.png");
        }
        return form;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    // ── T030 smoke: the test host itself works ──

    [Fact]
    public async Task TestHost_ServesHealth()
    {
        using var f = NewFactory();
        var r = await f.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // ── status mapping ──

    [Fact]
    public async Task ValidAdd_Returns201_Created()
    {
        using var f = NewFactory();
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("created", (await Body(r)).GetProperty("state").GetString());
        Assert.Contains("Pr submission/", f.GitHub.Calls.Last());
    }

    [Fact]
    public async Task MissingConsent_Returns400_Invalid_WithFieldNames()
    {
        using var f = NewFactory();
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form(consent: false));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await Body(r);
        Assert.Equal("invalid", body.GetProperty("state").GetString());
        Assert.Equal("consent", body.GetProperty("fields")[0].GetString());
    }

    [Fact]
    public async Task UnknownType_Returns400()
    {
        using var f = NewFactory();
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form(type: "explode"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task BodyOver300KB_Returns413()
    {
        using var f = NewFactory();
        var big = new byte[400 * 1024];
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form(image: big));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Empty(f.GitHub.Calls);
    }

    [Fact]
    public async Task PictureOver200KB_UnderBodyLimit_Returns413()
    {
        using var f = NewFactory();
        var png = new byte[SponsorRules.ImageMaxBytes + 10];
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form(image: png));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
    }

    [Fact]
    public async Task UsedDonation_Returns409()
    {
        using var f = NewFactory();
        f.GitHub.Claim = "0123456789ab";
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form());
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("already_used", (await Body(r)).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData(DonationRejection.NotFound, "not_found")]
    [InlineData(DonationRejection.TooFewConfirmations, "too_few_confirmations")]
    [InlineData(DonationRejection.WrongRecipient, "wrong_recipient")]
    [InlineData(DonationRejection.BelowMinimum, "below_minimum")]
    public async Task FailedDonationCheck_Returns422_WithSnakeCaseReason(DonationRejection reason, string expected)
    {
        using var f = NewFactory();
        f.Verifier.Result = DonationCheck.Rejected(reason);
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form());
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        var body = await Body(r);
        Assert.Equal("not_verified", body.GetProperty("state").GetString());
        Assert.Equal(expected, body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task FullQueue_Returns503()
    {
        using var f = NewFactory();
        f.GitHub.OpenCount = 50;
        var r = await f.CreateClient().PostAsync("/api/sponsors/submissions", Form());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("queue_full", (await Body(r)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task ExplorersDown_AndGitHubDown_Return502()
    {
        using var f1 = NewFactory();
        f1.Verifier.Throw = new DonationLookupUnavailableException("down");
        Assert.Equal(HttpStatusCode.BadGateway, (await f1.CreateClient().PostAsync("/api/sponsors/submissions", Form())).StatusCode);

        using var f2 = NewFactory();
        f2.GitHub.FailOnHead = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await f2.CreateClient().PostAsync("/api/sponsors/submissions", Form())).StatusCode);
    }

    // ── rate limit ──

    [Fact]
    public async Task FourthRequestInAnHour_Returns429_WithRetryAfter()
    {
        using var f = NewFactory(perHour: 3);
        var client = f.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/sponsors/submissions", Form(txid: new string((char)('a' + i), 64)))).StatusCode);
        var fourth = await client.PostAsync("/api/sponsors/submissions", Form(txid: new string('f', 64)));
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.True(fourth.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limited", (await Body(fourth)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task RateLimit_UsesTheRightmostForwardedAddress_SoSpoofedLeadingEntriesDoNotHelp()
    {
        using var f = NewFactory(perHour: 1);
        var client = f.CreateClient();
        HttpRequestMessage Req(string xff, string tx) => new(HttpMethod.Post, "/api/sponsors/submissions")
        {
            Content = Form(txid: tx),
            Headers = { { "X-Forwarded-For", xff } },
        };
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Req("1.1.1.1, 203.0.113.9", new string('a', 64)))).StatusCode);
        // Same real client (rightmost), different spoofed leading address: still limited.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Req("9.9.9.9, 203.0.113.9", new string('b', 64)))).StatusCode);
        // A different real client has its own allowance.
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Req("1.1.1.1, 203.0.113.10", new string('d', 64)))).StatusCode);
    }

    // ── secrecy ──

    [Fact]
    public async Task Token_NameLinkAndFullTxid_NeverAppearInResponsesOrLogs()
    {
        using var f = NewFactory();
        var client = f.CreateClient();
        var responses = new List<string>();
        foreach (var form in new[] { Form(name: "Zorro", txid: Tx), Form(name: "Zorro", txid: Tx, consent: false) })
            responses.Add(await (await client.PostAsync("/api/sponsors/submissions", form)).Content.ReadAsStringAsync());
        f.GitHub.FailOnHead = true;
        responses.Add(await (await client.PostAsync("/api/sponsors/submissions", Form(name: "Zorro", txid: new string('e', 64)))).Content.ReadAsStringAsync());

        var everything = string.Join("\n", responses) + "\n" + string.Join("\n", f.Logs.Messages);
        Assert.DoesNotContain(Secret, everything);
        Assert.DoesNotContain("Zorro", everything);
        Assert.DoesNotContain("alice.example", everything);
        Assert.DoesNotContain(Tx, everything);
        Assert.DoesNotContain(new string('e', 64), everything);
    }

    // ── test host ──

    private sealed class Factory(int perHour) : WebApplicationFactory<Program>
    {
        public FakeGitHub GitHub { get; } = new();
        public FakeVerifier Verifier { get; } = new();
        public CapturingLogs Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Sponsors:PerVisitorPerHour", perHour.ToString());
            builder.UseSetting("Sponsors:GitHubToken", Secret);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitHubSponsorClient>();
                services.RemoveAll<IDonationVerifier>();
                services.AddSingleton<IGitHubSponsorClient>(GitHub);
                services.AddSingleton<IDonationVerifier>(Verifier);
                services.AddLogging(l => l.AddProvider(Logs));
            });
        }
    }

    public sealed class FakeVerifier : IDonationVerifier
    {
        public DonationCheck Result { get; set; } = DonationCheck.Passed;
        public Exception? Throw { get; set; }
        public Task<DonationCheck> VerifyAsync(string txId, CancellationToken ct) =>
            Throw is not null ? throw Throw : Task.FromResult(Result);
    }

    public sealed class FakeGitHub : IGitHubSponsorClient
    {
        public List<string> Calls { get; } = [];
        public string? Claim { get; set; }
        public int OpenCount { get; set; }
        public bool FailOnHead { get; set; }

        public Task<string> GetBranchHeadAsync(CancellationToken ct) =>
            FailOnHead ? throw new GitHubApiException("GitHub answered 500") : Task.FromResult("sha");
        public Task<string?> ReadClaimAsync(string txId, CancellationToken ct) => Task.FromResult(Claim);
        public Task<string?> ReadFileTextAsync(string path, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<int> CountOpenSubmissionsAsync(CancellationToken ct) => Task.FromResult(OpenCount);
        public Task<bool> CreateBranchAsync(string branch, string baseSha, CancellationToken ct) { Calls.Add($"Branch {branch}"); return Task.FromResult(true); }
        public Task PutFileAsync(string branch, string path, byte[] content, string message, CancellationToken ct) { Calls.Add($"Put {path}"); return Task.CompletedTask; }
        public Task DeleteFileAsync(string branch, string path, string message, CancellationToken ct) => Task.CompletedTask;
        public Task<int> CreatePullRequestAsync(string head, string title, string body, string[] labels, CancellationToken ct) { Calls.Add($"Pr {head}"); return Task.FromResult(1); }
        public Task DeleteBranchAsync(string branch, CancellationToken ct) => Task.CompletedTask;
    }

    public sealed class CapturingLogs : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }

        private sealed class Capture(CapturingLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Messages) owner.Messages.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
            }
        }
    }
}
