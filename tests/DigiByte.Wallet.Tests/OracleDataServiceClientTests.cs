using System.Net;
using System.Text;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The wallet's oracle feed (specs/014-digibyte-io-oracle): reads the data service route in the background, keeps one shared answer, never waits
/// on the network for its callers, and treats anything unusable as no answer.
/// </summary>
public class OracleDataServiceClientTests
{
    internal const string Url = "https://api.example.org/api/oracle";

    internal sealed class Stub : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<HttpRequestMessage> Requests { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        public int Count { get { lock (_gate) return Requests.Count; } }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate) Requests.Add(request);
            return Answer(request, cancellationToken);
        }
    }

    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The route's success answer (contracts/oracle-api.md), with knobs for the cases the tests need.</summary>
    internal static string Good(DateTimeOffset now, long price = 2725, long height = 24568788, long? updated = null, bool stale = false, bool freeze = false,
        bool minting = false, bool allOps = false, bool err = false, long? fetchedAt = null, string? replaceName = null, string? replaceWith = null, string extra = "")
    {
        var body = "{" +
            $"\"priceMicroUsd\":{price},\"lastUpdateHeight\":{height},\"lastUpdateTime\":{updated ?? now.ToUnixTimeSeconds() - 20}," +
            $"\"reportedStale\":{B(stale)},\"volatilityFreeze\":{B(freeze)},\"mintingRestricted\":{B(minting)},\"allOperationsRestricted\":{B(allOps)}," +
            $"\"errActive\":{B(err)},\"fetchedAt\":{fetchedAt ?? now.ToUnixTimeSeconds()},\"source\":\"digibyte.io\"{extra}" + "}";
        if (replaceName is not null)
        {
            // Replace one whole property (or drop it when replaceWith is null).
            var start = body.IndexOf($"\"{replaceName}\":", StringComparison.Ordinal);
            var end = body.IndexOf(',', start);
            if (end < 0) end = body.Length - 1;
            body = body[..start] + (replaceWith is null ? "" : $"\"{replaceName}\":{replaceWith},") + body[(end + 1)..];
            body = body.Replace(",}", "}");
        }
        return body;
    }

    private static string B(bool v) => v ? "true" : "false";

    private sealed class Rig
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        public Stub Upstream { get; } = new();
        public OracleDataServiceClient Feed { get; }

        public Rig(string url = Url)
        {
            Upstream.Answer = (_, _) => Task.FromResult(Json(Good(Clock.GetUtcNow())));
            Feed = new OracleDataServiceClient(new HttpClient(Upstream), url, Clock);
        }

        /// <summary>Asks for a refresh and waits for the fetch to finish (the feed raises Changed when it does), but never forever.</summary>
        public async Task<bool> RefreshAsync()
        {
            var done = new TaskCompletionSource();
            void OnChanged() => done.TrySetResult();
            Feed.Changed += OnChanged;
            try
            {
                var before = Upstream.Count;
                Feed.RequestRefresh();
                await Task.Delay(30); // let a started fetch register
                if (Upstream.Count == before)
                    return false; // nothing was started
                await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return true;
            }
            finally
            {
                Feed.Changed -= OnChanged;
            }
        }
    }

    // ---------- reading a good answer ----------

    [Fact]
    public async Task A_good_answer_becomes_the_current_reading()
    {
        var rig = new Rig();

        Assert.True(await rig.RefreshAsync());

        var r = rig.Feed.Current;
        Assert.NotNull(r);
        Assert.Equal(2725, r.PriceMicroUsd);
        Assert.Equal(24568788, r.Height);
        Assert.False(r.ReportedStale);
        Assert.False(r.ErrActive);
        Assert.Equal(rig.Clock.GetUtcNow().ToUnixTimeSeconds(), r.FetchedAt);
        Assert.False(rig.Feed.IsLoading);
    }

    [Fact]
    public async Task Unknown_extra_fields_are_ignored()
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json(Good(rig.Clock.GetUtcNow(), extra: ",\"somethingNew\":{\"a\":1}")));

        await rig.RefreshAsync();

        Assert.NotNull(rig.Feed.Current);
    }

    // ---------- every unusable answer leaves no reading ----------

    public static TheoryData<string> UnusableShapes => new()
    {
        "missing-price", "price-as-text", "price-zero", "price-negative", "missing-flag", "flag-as-text", "missing-fetched-at", "not-json", "array", "future-update-time",
    };

    [Theory]
    [MemberData(nameof(UnusableShapes))]
    public async Task An_unusable_answer_leaves_no_reading(string kind)
    {
        var rig = new Rig();
        var now = rig.Clock.GetUtcNow();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json(kind switch
        {
            "missing-price" => Good(now, replaceName: "priceMicroUsd", replaceWith: null),
            "price-as-text" => Good(now, replaceName: "priceMicroUsd", replaceWith: "\"2725\""),
            "price-zero" => Good(now, price: 0),
            "price-negative" => Good(now, price: -1),
            "missing-flag" => Good(now, replaceName: "errActive", replaceWith: null),
            "flag-as-text" => Good(now, replaceName: "errActive", replaceWith: "\"false\""),
            "missing-fetched-at" => Good(now, replaceName: "fetchedAt", replaceWith: null),
            "not-json" => "this is not json",
            "array" => "[1,2,3]",
            "future-update-time" => Good(now, updated: now.ToUnixTimeSeconds() + 11 * 60),
            _ => throw new InvalidOperationException(kind),
        }));

        await rig.RefreshAsync();

        Assert.Null(rig.Feed.Current);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_error_status_leaves_no_reading_and_still_reports_the_change(HttpStatusCode status)
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json("{\"reason\":\"unavailable\"}", status));

        Assert.True(await rig.RefreshAsync()); // Changed fired, so waiting screens can stop showing "loading"

        Assert.Null(rig.Feed.Current);
        Assert.False(rig.Feed.IsLoading);
    }

    [Fact]
    public async Task A_network_error_leaves_no_reading()
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => throw new HttpRequestException("no route to host");

        Assert.True(await rig.RefreshAsync());

        Assert.Null(rig.Feed.Current);
    }

    [Fact]
    public async Task A_fetch_that_does_not_answer_in_5_seconds_gives_up()
    {
        var rig = new Rig();
        rig.Upstream.Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); };
        var done = new TaskCompletionSource();
        rig.Feed.Changed += () => done.TrySetResult();

        rig.Feed.RequestRefresh();
        await Task.Delay(30);
        Assert.True(rig.Feed.IsLoading);
        rig.Clock.Advance(TimeSpan.FromSeconds(5.5));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(rig.Feed.Current);
        Assert.False(rig.Feed.IsLoading);
    }

    // ---------- spacing: one attempt per 30 seconds, successful or not ----------

    [Fact]
    public async Task A_refresh_inside_30_seconds_of_a_good_one_starts_nothing_and_one_after_it_does()
    {
        var rig = new Rig();
        await rig.RefreshAsync();

        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(await rig.RefreshAsync());
        Assert.Equal(1, rig.Upstream.Count);

        rig.Clock.Advance(TimeSpan.FromSeconds(21)); // 31 s after the first attempt
        Assert.True(await rig.RefreshAsync());
        Assert.Equal(2, rig.Upstream.Count);
    }

    [Fact]
    public async Task A_failed_fetch_is_not_retried_at_once_even_though_the_screens_keep_asking()
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json("{}", HttpStatusCode.ServiceUnavailable));
        await rig.RefreshAsync(); // fails, raises Changed

        for (var i = 0; i < 20; i++)
        {
            rig.Feed.RequestRefresh();
            rig.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        await Task.Delay(50);
        Assert.Equal(1, rig.Upstream.Count);

        rig.Clock.Advance(TimeSpan.FromSeconds(15)); // 35 s after the failed attempt
        rig.Feed.RequestRefresh();
        await Task.Delay(50);
        Assert.Equal(2, rig.Upstream.Count);
    }

    [Fact]
    public async Task No_second_fetch_starts_while_one_is_in_flight()
    {
        var rig = new Rig();
        rig.Upstream.Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); };

        rig.Feed.RequestRefresh();
        rig.Clock.Advance(TimeSpan.FromSeconds(4)); // inside the 5 s limit, past nothing else
        rig.Feed.RequestRefresh();
        rig.Feed.RequestRefresh();
        await Task.Delay(50);

        Assert.Equal(1, rig.Upstream.Count);
    }

    // ---------- age ----------

    [Fact]
    public async Task The_reading_is_not_current_once_it_is_more_than_5_minutes_old()
    {
        var rig = new Rig();
        await rig.RefreshAsync();

        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.NotNull(rig.Feed.Current);
        rig.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Null(rig.Feed.Current);
    }

    // ---------- loading ----------

    [Fact]
    public async Task Loading_is_true_only_while_the_first_fetch_is_in_flight()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        rig.Upstream.Answer = async (_, _) => { await release.Task; return Json(Good(rig.Clock.GetUtcNow())); };
        var done = new TaskCompletionSource();
        rig.Feed.Changed += () => done.TrySetResult();
        Assert.False(rig.Feed.IsLoading); // nothing asked yet

        rig.Feed.RequestRefresh();
        await Task.Delay(30);
        Assert.True(rig.Feed.IsLoading);

        release.SetResult();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(rig.Feed.IsLoading);

        // A later fetch (after the first has finished) is not "loading": the screens would flicker otherwise.
        rig.Upstream.Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); };
        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        rig.Feed.RequestRefresh();
        await Task.Delay(30);
        Assert.False(rig.Feed.IsLoading);
    }

    // ---------- what the wallet sends ----------

    [Fact]
    public async Task The_request_is_a_plain_get_with_no_query_body_or_custom_headers()
    {
        var rig = new Rig();

        await rig.RefreshAsync();

        var request = Assert.Single(rig.Upstream.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.Equal("", request.RequestUri.Query);
        Assert.Null(request.Content);
        Assert.Empty(request.Headers); // nothing identifying and nothing that would make the browser send a preflight request (SC-005)
    }

    // ---------- the configured address ----------

    [Theory]
    [InlineData("http://api.example.org/api/oracle")]
    [InlineData("ftp://api.example.org/api/oracle")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_address_that_is_not_https_makes_no_request(string? url)
    {
        var rig = new Rig(url!);

        Assert.False(await rig.RefreshAsync());

        Assert.Equal(0, rig.Upstream.Count);
        Assert.Null(rig.Feed.Current);
        Assert.False(rig.Feed.IsLoading);
    }

    [Theory]
    [InlineData("http://localhost:5007/api/oracle")]
    [InlineData("http://127.0.0.1:5007/api/oracle")]
    public async Task A_loopback_http_address_is_allowed_for_development(string url)
    {
        var rig = new Rig(url);

        Assert.True(await rig.RefreshAsync());

        Assert.NotNull(rig.Feed.Current);
    }

    [Fact]
    public void The_address_check_accepts_https_and_loopback_only()
    {
        Assert.True(OracleDataServiceClient.IsAcceptableUrl("https://digibyte-api-production.up.railway.app/api/oracle"));
        Assert.True(OracleDataServiceClient.IsAcceptableUrl("http://localhost:5007/api/oracle"));
        Assert.False(OracleDataServiceClient.IsAcceptableUrl("http://digibyte-api-production.up.railway.app/api/oracle"));
        Assert.False(OracleDataServiceClient.IsAcceptableUrl(null));
    }

    // ---------- request rate and freshness over time (SC-006, scenario US1.3) ----------

    [Fact]
    public async Task Screens_that_ask_constantly_cause_at_most_twenty_requests_in_ten_minutes()
    {
        var rig = new Rig();

        // A screen re-reading every second for ten simulated minutes.
        for (var second = 0; second < 600; second++)
        {
            rig.Feed.RequestRefresh();
            await Task.Delay(1);
            rig.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        await Task.Delay(50);

        Assert.InRange(rig.Upstream.Count, 1, 20);
    }

    [Fact]
    public async Task A_changed_answer_is_picked_up_by_the_next_refresh_without_a_reload()
    {
        var rig = new Rig();
        await rig.RefreshAsync();
        Assert.Equal(2725, rig.Feed.Current!.PriceMicroUsd);

        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json(Good(rig.Clock.GetUtcNow(), price: 3100)));
        Assert.True(await rig.RefreshAsync());

        Assert.Equal(3100, rig.Feed.Current!.PriceMicroUsd);
    }

    [Fact]
    public async Task A_failed_fetch_is_not_retried_before_thirty_seconds_however_often_the_screens_ask()
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json("{}", HttpStatusCode.ServiceUnavailable));
        await rig.RefreshAsync();
        Assert.Equal(1, rig.Upstream.Count);

        for (var i = 0; i < 20; i++)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(1)); // 20 s in total
            rig.Feed.RequestRefresh();
        }
        await Task.Delay(50);
        Assert.Equal(1, rig.Upstream.Count);

        rig.Clock.Advance(TimeSpan.FromSeconds(11)); // 31 s: the feed's own retry (or a screen's ask) may now go out, once
        for (var w = 0; w < 100 && rig.Upstream.Count < 2; w++) { rig.Feed.RequestRefresh(); await Task.Delay(10); }
        await Task.Delay(50);
        Assert.Equal(2, rig.Upstream.Count);
    }

    // ---------- a failed first fetch is retried without a screen asking again ----------

    [Fact]
    public async Task A_failed_first_fetch_is_retried_on_its_own_after_thirty_seconds_and_the_price_then_appears()
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json("{}", HttpStatusCode.ServiceUnavailable));
        await rig.RefreshAsync();
        Assert.Null(rig.Feed.Current);

        rig.Upstream.Answer = (_, _) => Task.FromResult(Json(Good(rig.Clock.GetUtcNow())));
        var done = new TaskCompletionSource();
        rig.Feed.Changed += () => done.TrySetResult();
        rig.Clock.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(50);
        Assert.Equal(1, rig.Upstream.Count); // not before 30 seconds

        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, rig.Upstream.Count);
        Assert.NotNull(rig.Feed.Current);
    }

    [Fact]
    public async Task The_automatic_retries_stop_after_three_and_never_run_after_a_success()
    {
        var rig = new Rig();
        rig.Upstream.Answer = (_, _) => Task.FromResult(Json("{}", HttpStatusCode.ServiceUnavailable));
        await rig.RefreshAsync();

        for (var i = 0; i < 6; i++)
        {
            var before = rig.Upstream.Count;
            rig.Clock.Advance(TimeSpan.FromSeconds(31));
            for (var w = 0; w < 100 && rig.Upstream.Count == before && i < 3; w++) await Task.Delay(10);
        }
        await Task.Delay(50);

        Assert.Equal(4, rig.Upstream.Count); // the first attempt plus three retries

        var ok = new Rig();
        await ok.RefreshAsync();
        ok.Clock.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(50);
        Assert.Equal(1, ok.Upstream.Count);
    }
}
