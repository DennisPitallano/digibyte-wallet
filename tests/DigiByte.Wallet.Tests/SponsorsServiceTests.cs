using System.Net;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

public class SponsorsServiceTests
{
    private const string Base = "https://raw.example.test/sponsors/data/";
    private const string TwoSponsors = """{"version":1,"sponsors":[{"name":"Old"},{"name":"New"}]}""";

    private static (SponsorsService svc, FakeHandler http, FakeJs js) Create(FakeHandler? http = null, FakeJs? js = null, TimeProvider? clock = null)
    {
        http ??= new FakeHandler();
        js ??= new FakeJs();
        return (new SponsorsService(new HttpClient(http), js, Base, clock), http, js);
    }

    // ── US1: fresh results ──

    [Fact]
    public async Task Success_ReturnsFresh_NewestFirst()
    {
        var (svc, http, _) = Create(new FakeHandler { Body = TwoSponsors });
        var r = await svc.LoadAsync();
        Assert.Equal(SponsorLoadState.Fresh, r.State);
        Assert.Equal(["New", "Old"], r.List!.Entries.Select(e => e.Name));
        Assert.Equal(1, http.Requests.Count);
    }

    [Fact]
    public async Task EmptyList_ReturnsFreshEmpty()
    {
        var (svc, _, _) = Create(new FakeHandler { Body = """{"version":1,"sponsors":[]}""" });
        var r = await svc.LoadAsync();
        Assert.Equal(SponsorLoadState.FreshEmpty, r.State);
        Assert.Empty(r.List!.Entries);
    }

    [Fact]
    public async Task Request_GoesOnlyToConfiguredBase_AndHasNoQueryString()
    {
        var (svc, http, _) = Create(new FakeHandler { Body = TwoSponsors });
        await svc.LoadAsync();
        var uri = Assert.Single(http.Requests);
        Assert.Equal(Base + "sponsors.json", uri.ToString());
        Assert.Equal("", uri.Query);
    }

    [Fact]
    public async Task FetchedAt_UsesTheClock()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var (svc, _, _) = Create(new FakeHandler { Body = TwoSponsors }, clock: new FixedClock(now));
        var r = await svc.LoadAsync();
        Assert.Equal(now, r.List!.FetchedAt);
    }

    // ── US3: failure states ──

    [Fact]
    public async Task Failure_WithStoredCopy_ReturnsStale_WithStoredFetchTime()
    {
        var t1 = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
        var js = new FakeJs();
        var ok = new FakeHandler { Body = TwoSponsors };
        var (svc1, _, _) = Create(ok, js, new FixedClock(t1));
        await svc1.LoadAsync(); // writes the copy

        var down = new FakeHandler { Status = HttpStatusCode.InternalServerError };
        var (svc2, _, _) = Create(down, js, new FixedClock(t1.AddDays(1)));
        var r = await svc2.LoadAsync();
        Assert.Equal(SponsorLoadState.Stale, r.State);
        Assert.Equal(t1, r.List!.FetchedAt);
        Assert.Equal(["New", "Old"], r.List.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task Failure_WithNoCopy_ReturnsUnavailable()
    {
        var (svc, _, _) = Create(new FakeHandler { Status = HttpStatusCode.ServiceUnavailable });
        var r = await svc.LoadAsync();
        Assert.Equal(SponsorLoadState.Unavailable, r.State);
        Assert.Null(r.List);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task HttpErrors_AreTreatedAsFailure(HttpStatusCode status)
    {
        var (svc, _, _) = Create(new FakeHandler { Status = status });
        Assert.Equal(SponsorLoadState.Unavailable, (await svc.LoadAsync()).State);
    }

    [Fact]
    public async Task NetworkError_AndTimeout_AreTreatedAsFailure()
    {
        var (svc1, _, _) = Create(new FakeHandler { Throw = new HttpRequestException("offline") });
        Assert.Equal(SponsorLoadState.Unavailable, (await svc1.LoadAsync()).State);
        var (svc2, _, _) = Create(new FakeHandler { Throw = new TaskCanceledException("timeout") });
        Assert.Equal(SponsorLoadState.Unavailable, (await svc2.LoadAsync()).State);
    }

    [Fact]
    public async Task Copy_IsWrittenOnlyAfterAValidParse()
    {
        var js = new FakeJs();
        var (svc, _, _) = Create(new FakeHandler { Body = "<html>captive portal</html>" }, js);
        var r = await svc.LoadAsync();
        Assert.Equal(SponsorLoadState.Unavailable, r.State);
        Assert.Empty(js.Store);
    }

    [Fact]
    public async Task UnreadableResponse_KeepsThePreviousGoodCopy()
    {
        var js = new FakeJs();
        var (svc1, _, _) = Create(new FakeHandler { Body = TwoSponsors }, js);
        await svc1.LoadAsync();
        var before = js.Store.Single().Value;

        var (svc2, _, _) = Create(new FakeHandler { Body = """{"version":9,"sponsors":[]}""" }, js);
        var r = await svc2.LoadAsync();
        Assert.Equal(SponsorLoadState.Stale, r.State);
        Assert.Equal(before, js.Store.Single().Value);
    }

    [Fact]
    public async Task CorruptStoredCopy_IsIgnored()
    {
        var js = new FakeJs();
        js.Store[SponsorsService.StorageKey] = "{ not json";
        var (svc, _, _) = Create(new FakeHandler { Status = HttpStatusCode.InternalServerError }, js);
        Assert.Equal(SponsorLoadState.Unavailable, (await svc.LoadAsync()).State);

        js.Store[SponsorsService.StorageKey] = """{"fetchedAt":"2026-01-01T00:00:00+00:00","text":"garbage"}""";
        Assert.Equal(SponsorLoadState.Unavailable, (await svc.LoadAsync()).State);
    }

    [Fact]
    public async Task BlockedStorage_DoesNotBreakAFreshLoad()
    {
        var js = new FakeJs { Throw = true };
        var (svc, _, _) = Create(new FakeHandler { Body = TwoSponsors }, js);
        var r = await svc.LoadAsync();
        Assert.Equal(SponsorLoadState.Fresh, r.State);
    }

    // ── Home strip: reuse a recent copy instead of asking GitHub again ──

    [Fact]
    public async Task MaxAge_RecentCopy_IsReturnedWithoutAnyRequest()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var js = new FakeJs();
        var (first, _, _) = Create(new FakeHandler { Body = TwoSponsors }, js, new FixedClock(now));
        await first.LoadAsync();

        var down = new FakeHandler { Status = HttpStatusCode.InternalServerError };
        var (second, http, _) = Create(down, js, new FixedClock(now.AddMinutes(10)));
        var r = await second.LoadAsync(maxAge: TimeSpan.FromMinutes(30));
        Assert.Equal(SponsorLoadState.Fresh, r.State);
        Assert.Equal(["New", "Old"], r.List!.Entries.Select(e => e.Name));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task MaxAge_OldCopy_IsRefetched()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var js = new FakeJs();
        var (first, _, _) = Create(new FakeHandler { Body = """{"version":1,"sponsors":[{"name":"Only"}]}""" }, js, new FixedClock(now));
        await first.LoadAsync();

        var (second, http, _) = Create(new FakeHandler { Body = TwoSponsors }, js, new FixedClock(now.AddMinutes(45)));
        var r = await second.LoadAsync(maxAge: TimeSpan.FromMinutes(30));
        Assert.Single(http.Requests);
        Assert.Equal(["New", "Old"], r.List!.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task MaxAge_NoCopy_Fetches_AndAFailureIsUnavailable()
    {
        var (svc, http, _) = Create(new FakeHandler { Status = HttpStatusCode.ServiceUnavailable });
        var r = await svc.LoadAsync(maxAge: TimeSpan.FromMinutes(30));
        Assert.Single(http.Requests);
        Assert.Equal(SponsorLoadState.Unavailable, r.State);
    }

    // ── test doubles ──

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "";
        public Exception? Throw { get; set; }
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (Throw is not null) throw Throw;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }

    private sealed class FakeJs : IJSRuntime
    {
        public Dictionary<string, string> Store { get; } = new();
        public bool Throw { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (Throw) throw new JSException("storage unavailable");
            switch (identifier)
            {
                case "localStorage.getItem":
                    object? value = Store.TryGetValue((string)args![0]!, out var v) ? v : null;
                    return new ValueTask<TValue>((TValue)value!);
                case "localStorage.setItem":
                    Store[(string)args![0]!] = (string)args[1]!;
                    break;
            }
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
