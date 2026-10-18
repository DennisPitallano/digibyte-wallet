using System.Net;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T044: the wallet's anonymous usage counter (SC-005, FR-023, research R17): an empty POST naming one of three events.
/// It carries nothing about the user, never blocks or breaks the UI, and backs off when the counter endpoint is unavailable.
/// </summary>
public class UsageCounterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed record Seen(HttpMethod Method, Uri Uri, bool HasContent, int HeaderCount, string? Cookie, string? Auth);

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); // like a real handler
            Requests.Add(new Seen(request.Method, request.RequestUri!, request.Content is not null, request.Headers.Count(),
                request.Headers.TryGetValues("Cookie", out var c) ? string.Join(";", c) : null,
                request.Headers.Authorization?.ToString()));
            return Task.FromResult(respond());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException("down");
        }
    }

    private static UsageCounter Counter(HttpMessageHandler h, Func<bool>? enabled = null, Func<DateTimeOffset>? clock = null) =>
        new(new HttpClient(h), "https://node.example/", enabled ?? (() => true), clock ?? (() => T0));

    [Theory]
    [InlineData("home_view")]
    [InlineData("dd_section_view")]
    [InlineData("dd_send_attempted")]
    public async Task Sends_AnEmptyPost_ToTheEventPath(string name)
    {
        var h = new Handler(() => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Counter(h).RecordAsync(name);

        var r = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal($"https://node.example/api/metrics/event/{name}", r.Uri.ToString());
    }

    [Fact]
    public async Task Request_CarriesNothingAboutTheUser()
    {
        var h = new Handler(() => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Counter(h).RecordAsync("dd_section_view");

        var r = Assert.Single(h.Requests);
        Assert.False(r.HasContent);          // no body
        Assert.Equal("", r.Uri.Query);       // no query string
        Assert.Equal("", r.Uri.Fragment);
        Assert.Equal(0, r.HeaderCount);      // no custom headers
        Assert.Null(r.Cookie);
        Assert.Null(r.Auth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("address")]
    [InlineData("home_view?x=1")]
    [InlineData("../admin")]
    [InlineData("HOME_VIEW")]
    public async Task UnknownNames_AreNeverSent(string name)
    {
        var h = new Handler(() => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Counter(h).RecordAsync(name);

        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task WhenDisabled_NothingIsSent()
    {
        var h = new Handler(() => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Counter(h, enabled: () => false).RecordAsync("home_view");

        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ServerErrors_NeverThrow_AndTriggerABackoff(HttpStatusCode status)
    {
        var now = T0;
        var h = new Handler(() => new HttpResponseMessage(status));
        var counter = Counter(h, clock: () => now);

        await counter.RecordAsync("home_view");        // fails
        await counter.RecordAsync("home_view");        // skipped: backing off
        now = T0.AddMinutes(4);
        await counter.RecordAsync("home_view");        // still backing off
        now = T0.AddMinutes(6);
        await counter.RecordAsync("home_view");        // backoff over: tries again

        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailure_NeverThrows_AndBacksOff()
    {
        var h = new ThrowingHandler();
        var counter = Counter(h);

        await counter.RecordAsync("home_view");
        await counter.RecordAsync("dd_section_view");

        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Success_ClearsAnyBackoff()
    {
        var now = T0;
        var failing = true;
        var h = new Handler(() => new HttpResponseMessage(failing ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent));
        var counter = Counter(h, clock: () => now);

        await counter.RecordAsync("home_view");        // fails: backoff starts
        failing = false;
        now = T0.AddMinutes(6);
        await counter.RecordAsync("home_view");        // succeeds
        await counter.RecordAsync("dd_section_view");  // goes straight through: no backoff left

        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task Cancellation_IsRespected()
    {
        var h = new Handler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Counter(h).RecordAsync("home_view", cts.Token); // does not throw

        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task BaseUrl_WithOrWithoutTrailingSlash_Works()
    {
        var h = new Handler(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        var a = new UsageCounter(new HttpClient(h), "https://node.example", () => true, () => T0);
        var b = new UsageCounter(new HttpClient(h), "https://node.example/", () => true, () => T0);

        await a.RecordAsync("home_view");
        await b.RecordAsync("home_view");

        Assert.All(h.Requests, r => Assert.Equal("https://node.example/api/metrics/event/home_view", r.Uri.ToString()));
    }
}
