using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.Api.Tests;

/// <summary>
/// GET /api/oracle (specs/014-digibyte-io-oracle): the relay that fetches the oracle price and DigiDollar protection status from
/// digibyte.io, checks them, keeps one shared answer and serves it to wallets. digibyte.io is replaced by a stub in every test.
/// </summary>
public class OracleEndpointsTests
{
    private const string PricePath = "/api/getoracleprice";
    private const string StatsPath = "/api/getdigidollarstats";

    // ---------- the stub that stands in for digibyte.io ----------

    private sealed class Stub : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<HttpRequestMessage> Requests { get; } = [];
        public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

        /// <summary>Per path: how to answer. Defaults to the normal state.</summary>
        public Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Answers { get; } = [];

        public int CountFor(string path) { lock (_gate) return Requests.Count(r => r.RequestUri!.AbsolutePath == path); }
        public int Total { get { lock (_gate) return Requests.Count; } }

        public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static string Price(DateTimeOffset now, long price = 2725, string status = "active", bool stale = false, bool freeze = false,
            bool mintRestricted = false, bool allRestricted = false, long? updateTime = null, bool dropPrice = false, bool priceAsText = false) =>
            "{" +
            (dropPrice ? "" : priceAsText ? $"\"price_micro_usd\":\"{price}\"," : $"\"price_micro_usd\":{price},") +
            $"\"price_usd\":{price / 1_000_000.0:0.000000},\"last_update_height\":24568788,\"last_update_time\":{updateTime ?? now.ToUnixTimeSeconds() - 20}," +
            $"\"validity_blocks\":20,\"is_stale\":{stale.ToString().ToLowerInvariant()},\"oracle_count\":12,\"status\":\"{status}\"," +
            $"\"mint_volatility\":{{\"protection_active\":{freeze.ToString().ToLowerInvariant()},\"minting_restricted\":{mintRestricted.ToString().ToLowerInvariant()}," +
            $"\"all_operations_restricted\":{allRestricted.ToString().ToLowerInvariant()},\"deviation_bps\":54}}}}";

        public static string Stats(bool emergency = false, double burn = 1, bool dropEmergency = false, bool emergencyAsText = false) =>
            "{" + (dropEmergency ? "" : emergencyAsText ? $"\"is_emergency\":\"{emergency}\"," : $"\"is_emergency\":{emergency.ToString().ToLowerInvariant()},") +
            $"\"err_tier\":{{\"ratio\":1,\"burn_multiplier\":{burn.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"description\":\"Normal\"}},\"health_percentage\":215}}";

        public void Normal()
        {
            Answers[PricePath] = (_, _) => Task.FromResult(Json(Price(Now())));
            Answers[StatsPath] = (_, _) => Task.FromResult(Json(Stats()));
        }

        public void Fail(HttpStatusCode status = HttpStatusCode.InternalServerError)
        {
            Answers[PricePath] = (_, _) => Task.FromResult(Json("{}", status));
            Answers[StatsPath] = (_, _) => Task.FromResult(Json("{}", status));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate) Requests.Add(request);
            return Answers.TryGetValue(request.RequestUri!.AbsolutePath, out var answer)
                ? answer(request, cancellationToken)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Logs : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }

        private sealed class Capture(Logs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Messages) owner.Messages.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
            }
        }
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _settings;

        public Factory(Dictionary<string, string?>? settings = null)
        {
            _settings = settings ?? [];
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
            Upstream = new Stub { Now = () => Clock.GetUtcNow() };
            Upstream.Normal();
        }

        public FakeTimeProvider Clock { get; }
        public Stub Upstream { get; }
        public Logs Log { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var (key, value) in _settings)
                builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddHttpClient("DigiByteIoOracle").ConfigurePrimaryHttpMessageHandler(() => Upstream);
                services.AddLogging(l => l.AddProvider(Log));
            });
        }
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // ---------- success ----------

    [Fact]
    public async Task A_good_upstream_gives_the_contract_shape_and_no_store()
    {
        using var f = new Factory();

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await BodyOf(response);
        Assert.Equal(2725, body.GetProperty("priceMicroUsd").GetInt64());
        Assert.Equal(24568788, body.GetProperty("lastUpdateHeight").GetInt64());
        Assert.Equal(f.Clock.GetUtcNow().ToUnixTimeSeconds() - 20, body.GetProperty("lastUpdateTime").GetInt64());
        Assert.False(body.GetProperty("reportedStale").GetBoolean());
        Assert.False(body.GetProperty("volatilityFreeze").GetBoolean());
        Assert.False(body.GetProperty("mintingRestricted").GetBoolean());
        Assert.False(body.GetProperty("allOperationsRestricted").GetBoolean());
        Assert.False(body.GetProperty("errActive").GetBoolean());
        Assert.Equal(f.Clock.GetUtcNow().ToUnixTimeSeconds(), body.GetProperty("fetchedAt").GetInt64());
        Assert.Equal("digibyte.io", body.GetProperty("source").GetString());
    }

    // ---------- each protection state sets only its own flag (US3) ----------

    [Theory]
    [InlineData("volatility", "volatilityFreeze")]
    [InlineData("minting", "mintingRestricted")]
    [InlineData("allOps", "allOperationsRestricted")]
    [InlineData("emergency", "errActive")]
    [InlineData("burn", "errActive")]
    public async Task A_non_normal_state_sets_only_its_own_flag(string state, string flag)
    {
        using var f = new Factory();
        var now = f.Clock.GetUtcNow();
        f.Upstream.Answers[PricePath] = (_, _) => Task.FromResult(Stub.Json(Stub.Price(now,
            freeze: state == "volatility", mintRestricted: state == "minting", allRestricted: state == "allOps")));
        f.Upstream.Answers[StatsPath] = (_, _) => Task.FromResult(Stub.Json(Stub.Stats(emergency: state == "emergency", burn: state == "burn" ? 1.5 : 1)));

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyOf(response);
        foreach (var name in new[] { "volatilityFreeze", "mintingRestricted", "allOperationsRestricted", "errActive" })
            Assert.Equal(name == flag, body.GetProperty(name).GetBoolean());
        Assert.False(body.GetProperty("reportedStale").GetBoolean());
    }

    [Fact]
    public async Task The_upstream_stale_flag_is_passed_on_as_reportedStale()
    {
        using var f = new Factory();
        f.Upstream.Answers[PricePath] = (_, _) => Task.FromResult(Stub.Json(Stub.Price(f.Clock.GetUtcNow(), stale: true)));

        var body = await BodyOf(await f.CreateClient().GetAsync("/api/oracle"));

        Assert.True(body.GetProperty("reportedStale").GetBoolean());
    }

    // ---------- one shared answer ----------

    [Fact]
    public async Task Two_calls_within_30_seconds_cause_one_upstream_request_per_kind()
    {
        using var f = new Factory();
        var client = f.CreateClient();

        await client.GetAsync("/api/oracle");
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await client.GetAsync("/api/oracle");

        Assert.Equal(1, f.Upstream.CountFor(PricePath));
        Assert.Equal(1, f.Upstream.CountFor(StatsPath));
    }

    [Fact]
    public async Task Concurrent_calls_cause_one_upstream_request_per_kind()
    {
        using var f = new Factory();
        f.Upstream.Answers[PricePath] = async (_, ct) => { await Task.Delay(250, ct); return Stub.Json(Stub.Price(f.Clock.GetUtcNow())); };
        var client = f.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.GetAsync("/api/oracle")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, f.Upstream.CountFor(PricePath));
        Assert.Equal(1, f.Upstream.CountFor(StatsPath));
    }

    // ---------- serve old, then drop ----------

    [Fact]
    public async Task The_last_good_answer_is_served_until_5_minutes_after_it_was_fetched_and_then_the_route_answers_503()
    {
        using var f = new Factory();
        var client = f.CreateClient();
        var first = await BodyOf(await client.GetAsync("/api/oracle"));
        f.Upstream.Fail();

        f.Clock.Advance(TimeSpan.FromSeconds(31));
        var stillServed = await client.GetAsync("/api/oracle");
        f.Clock.Advance(TimeSpan.FromSeconds(270)); // 301 s after the good fetch
        var expired = await client.GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.OK, stillServed.StatusCode);
        Assert.Equal(first.GetProperty("fetchedAt").GetInt64(), (await BodyOf(stillServed)).GetProperty("fetchedAt").GetInt64());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, expired.StatusCode);
        Assert.Equal("unavailable", (await BodyOf(expired)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task During_an_outage_there_is_no_new_attempt_within_30_seconds_of_the_previous_one_however_many_wallets_call()
    {
        using var f = new Factory();
        var client = f.CreateClient();
        await client.GetAsync("/api/oracle"); // t = 0: two requests
        f.Upstream.Fail();

        f.Clock.Advance(TimeSpan.FromSeconds(31)); // t = 31: one failed attempt per kind
        for (var i = 0; i < 10; i++)
            await client.GetAsync("/api/oracle");
        Assert.Equal(4, f.Upstream.Total);

        f.Clock.Advance(TimeSpan.FromSeconds(10)); // t = 41: still inside the spacing
        for (var i = 0; i < 10; i++)
            await client.GetAsync("/api/oracle");
        Assert.Equal(4, f.Upstream.Total);

        f.Clock.Advance(TimeSpan.FromSeconds(22)); // t = 63: next attempt allowed
        await client.GetAsync("/api/oracle");
        Assert.Equal(6, f.Upstream.Total);
    }

    // ---------- every kind of unusable upstream answer ----------

    public static TheoryData<string> BadPriceAnswers => new()
    {
        "status-500", "not-json", "missing-price", "price-as-text", "price-zero", "price-negative", "status-inactive", "too-big", "redirect",
        "future-update-time", "missing-volatility-block",
    };

    [Theory]
    [MemberData(nameof(BadPriceAnswers))]
    public async Task An_unusable_price_answer_gives_503(string kind)
    {
        using var f = new Factory();
        var now = f.Clock.GetUtcNow();
        f.Upstream.Answers[PricePath] = (_, _) => Task.FromResult(kind switch
        {
            "status-500" => Stub.Json("{}", HttpStatusCode.InternalServerError),
            "not-json" => Stub.Json("this is not json"),
            "missing-price" => Stub.Json(Stub.Price(now, dropPrice: true)),
            "price-as-text" => Stub.Json(Stub.Price(now, priceAsText: true)),
            "price-zero" => Stub.Json(Stub.Price(now, price: 0)),
            "price-negative" => Stub.Json(Stub.Price(now, price: -5)),
            "status-inactive" => Stub.Json(Stub.Price(now, status: "inactive")),
            "too-big" => Stub.Json("{\"padding\":\"" + new string('x', 70 * 1024) + "\"}"),
            "redirect" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/") } },
            "future-update-time" => Stub.Json(Stub.Price(now, updateTime: now.ToUnixTimeSeconds() + 11 * 60)),
            "missing-volatility-block" => Stub.Json("{\"price_micro_usd\":2725,\"last_update_height\":1,\"last_update_time\":" + now.ToUnixTimeSeconds() + ",\"is_stale\":false,\"status\":\"active\"}"),
            _ => throw new InvalidOperationException(kind),
        });

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("status-500")]
    [InlineData("not-json")]
    [InlineData("missing-emergency")]
    [InlineData("emergency-as-text")]
    public async Task An_unusable_status_answer_gives_503(string kind)
    {
        using var f = new Factory();
        f.Upstream.Answers[StatsPath] = (_, _) => Task.FromResult(kind switch
        {
            "status-500" => Stub.Json("{}", HttpStatusCode.InternalServerError),
            "not-json" => Stub.Json("nope"),
            "missing-emergency" => Stub.Json(Stub.Stats(dropEmergency: true)),
            "emergency-as-text" => Stub.Json(Stub.Stats(emergencyAsText: true)),
            _ => throw new InvalidOperationException(kind),
        });

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task An_upstream_that_does_not_answer_in_3_seconds_gives_503()
    {
        using var f = new Factory();
        var started = new TaskCompletionSource();
        f.Upstream.Answers[PricePath] = async (_, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return Stub.Json("{}"); };
        var call = f.CreateClient().GetAsync("/api/oracle");

        // Wait until the stub has the request (or the call has already finished), but never forever.
        await Task.WhenAny(started.Task, call, Task.Delay(TimeSpan.FromSeconds(5)));
        f.Clock.Advance(TimeSpan.FromSeconds(3.5));
        var response = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---------- switches ----------

    [Fact]
    public async Task With_the_service_switch_off_the_route_answers_404()
    {
        using var f = new Factory(new() { ["Oracle:Enabled"] = "false" });

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, f.Upstream.Total);
    }

    [Theory]
    [InlineData("http://digibyte.io/api")]
    [InlineData("ftp://digibyte.io/api")]
    [InlineData("not a url")]
    public async Task A_base_address_that_is_not_https_turns_the_route_off(string baseUrl)
    {
        using var f = new Factory(new() { ["Oracle:BaseUrl"] = baseUrl });

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, f.Upstream.Total);
    }

    [Fact]
    public async Task A_loopback_http_address_is_allowed_for_development()
    {
        using var f = new Factory(new() { ["Oracle:BaseUrl"] = "http://localhost:9999/api" });

        var response = await f.CreateClient().GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- limits and privacy ----------

    [Fact]
    public async Task The_31st_request_in_a_minute_from_one_visitor_gets_429()
    {
        using var f = new Factory();
        var client = f.CreateClient();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 31; i++)
            last = await client.GetAsync("/api/oracle");

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal("60", last.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
    }

    [Fact]
    public async Task Nothing_from_the_caller_reaches_digibyte_io_and_only_the_two_fixed_paths_are_asked()
    {
        using var f = new Factory();
        var client = f.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/oracle?address=DSomeAddress123&txid=abc");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");
        request.Headers.Add("X-Custom", "secret-value");
        request.Headers.Add("Cookie", "session=secret-cookie");
        request.Headers.Add("Authorization", "Bearer secret-token");

        await client.SendAsync(request);

        Assert.Equal(2, f.Upstream.Total);
        Assert.All(f.Upstream.Requests, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.True(r.RequestUri!.AbsolutePath is PricePath or StatsPath);
            Assert.Equal("", r.RequestUri.Query);
            var headers = string.Join("\n", r.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)));
            Assert.DoesNotContain("secret", headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("203.0.113.9", headers);
            Assert.False(r.Headers.Contains("Cookie"));
            Assert.False(r.Headers.Contains("Authorization"));
        });
    }

    [Fact]
    public async Task The_routes_log_output_contains_no_visitor_address()
    {
        using var f = new Factory();
        var client = f.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/oracle");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        await client.SendAsync(request);
        f.Upstream.Fail();
        f.Clock.Advance(TimeSpan.FromMinutes(6));
        using var second = new HttpRequestMessage(HttpMethod.Get, "/api/oracle");
        second.Headers.Add("X-Forwarded-For", "203.0.113.9");
        await client.SendAsync(second);

        lock (f.Log.Messages)
            Assert.DoesNotContain(f.Log.Messages, m => m.Contains("203.0.113.9"));
    }
}
