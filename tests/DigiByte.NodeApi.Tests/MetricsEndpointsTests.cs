using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using DigiByte.NodeApi.Metrics;
using DigiByte.NodeApi.RpcClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T043: the anonymous usage counter (research R17, SC-005, FR-023): allowlisted names only, empty body, per-client
/// in-memory rate limit, wallet-origin-only CORS, operator-only summary, and nothing identifying is ever stored.
/// </summary>
public class MetricsEndpointsTests
{
    private const string WalletOrigin = "https://wallet.example";

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Host : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Http { get; init; }
        public required MetricsStore Store { get; init; }
        public required TestClock Clock { get; init; }

        public static async Task<Host> StartAsync(
            string? writeKey = "operator-key", string environment = "Production", string[]? origins = null, int limit = 60)
        {
            var clock = new TestClock();
            var store = MetricsStore.InMemory();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new DigiDollarConfig { WriteKey = writeKey });
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton(new MetricsRateLimiter(limit));
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddCors(o => o.AddMetricsCorsPolicy(origins ?? [WalletOrigin]));

            var app = builder.Build();
            app.UseCors();
            app.MapGroup("/api/metrics").MapMetricsEndpoints();
            await app.StartAsync();

            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new Host { App = app, Http = new HttpClient { BaseAddress = new Uri(url) }, Store = store, Clock = clock };
        }

        public Task<HttpResponseMessage> Post(string name, HttpContent? content = null, string? origin = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/metrics/event/{name}") { Content = content };
            if (origin is not null) req.Headers.Add("Origin", origin);
            return Http.SendAsync(req);
        }

        public Task<HttpResponseMessage> Summary(string? key = "operator-key", int? days = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/metrics/summary" + (days is null ? "" : $"?days={days}"));
            if (key is not null) req.Headers.Add("X-NodeApi-Key", key);
            return Http.SendAsync(req);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            Store.Dispose();
        }
    }

    // ---------- recording ----------

    [Theory]
    [InlineData("dd_section_view")]
    [InlineData("home_view")]
    [InlineData("dd_send_attempted")]
    public async Task AllowlistedEvent_IsCounted_AndAnswers204(string name)
    {
        await using var h = await Host.StartAsync();

        var response = await h.Post(name);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var summary = await (await h.Summary()).Content.ReadFromJsonAsync<JsonElement>();
        var today = summary.GetProperty("days")[0];
        Assert.Equal("2026-10-19", today.GetProperty("date").GetString());
        Assert.Equal(1, today.GetProperty(name).GetInt64());
    }

    [Fact]
    public async Task Counts_Accumulate_PerDay_PerEvent()
    {
        await using var h = await Host.StartAsync();
        for (var i = 0; i < 3; i++) await h.Post("home_view");
        await h.Post("dd_section_view");
        h.Clock.Now = h.Clock.Now.AddDays(1);
        await h.Post("home_view");

        var summary = await (await h.Summary()).Content.ReadFromJsonAsync<JsonElement>();
        var days = summary.GetProperty("days").EnumerateArray().ToList();

        Assert.Equal(2, days.Count);
        Assert.Equal("2026-10-20", days[0].GetProperty("date").GetString()); // newest first
        Assert.Equal(1, days[0].GetProperty("home_view").GetInt64());
        Assert.Equal(3, days[1].GetProperty("home_view").GetInt64());
        Assert.Equal(1, days[1].GetProperty("dd_section_view").GetInt64());
        Assert.Equal(0, days[1].GetProperty("dd_send_attempted").GetInt64()); // every allowlisted event is always present
    }

    [Theory]
    [InlineData("address_TD123")]
    [InlineData("DROP TABLE")]
    [InlineData("dd_section_view2")]
    [InlineData("HOME_VIEW")]
    public async Task UnknownEventName_Is400_AndNothingIsCounted(string name)
    {
        await using var h = await Host.StartAsync();

        var response = await h.Post(Uri.EscapeDataString(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var summary = await (await h.Summary()).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, summary.GetProperty("days").GetArrayLength());
    }

    [Fact]
    public async Task ARequestBody_Is400_AndNothingIsCounted()
    {
        await using var h = await Host.StartAsync();

        var response = await h.Post("home_view", new StringContent("{\"address\":\"TDsomething\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var summary = await (await h.Summary()).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, summary.GetProperty("days").GetArrayLength());
    }

    // ---------- rate limit ----------

    [Fact]
    public async Task RateLimit_BlocksTheNthPlusOneRequest_With429_AndRetryAfter()
    {
        await using var h = await Host.StartAsync(limit: 3);
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.NoContent, (await h.Post("home_view")).StatusCode);

        var blocked = await h.Post("home_view");

        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
        Assert.Equal("60", blocked.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ToString());
        var summary = await (await h.Summary()).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, summary.GetProperty("days")[0].GetProperty("home_view").GetInt64()); // the blocked one was not counted
    }

    [Fact]
    public async Task RateLimit_AlsoLimitsJunkRequests()
    {
        await using var h = await Host.StartAsync(limit: 2);
        await h.Post("nonsense");
        await h.Post("nonsense");

        Assert.Equal((HttpStatusCode)429, (await h.Post("home_view")).StatusCode);
    }

    [Fact]
    public async Task RateLimit_WindowRolls_AfterAMinute()
    {
        await using var h = await Host.StartAsync(limit: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Post("home_view")).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await h.Post("home_view")).StatusCode);

        h.Clock.Now = h.Clock.Now.AddSeconds(61);

        Assert.Equal(HttpStatusCode.NoContent, (await h.Post("home_view")).StatusCode);
    }

    // ---------- summary is operator-only ----------

    [Fact]
    public async Task Summary_RequiresTheOperatorKey()
    {
        await using var h = await Host.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Summary(key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Summary(key: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Summary()).StatusCode);
    }

    [Fact]
    public async Task Summary_FailsClosed_WhenNoKeyIsConfigured_OutsideDevelopment()
    {
        await using var h = await Host.StartAsync(writeKey: null, environment: "Production");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await h.Summary(key: null)).StatusCode);
    }

    [Fact]
    public async Task Summary_IsOpenInDevelopment_WithNoKeyConfigured()
    {
        await using var h = await Host.StartAsync(writeKey: null, environment: "Development");
        Assert.Equal(HttpStatusCode.OK, (await h.Summary(key: null)).StatusCode);
    }

    [Fact]
    public async Task Summary_DaysParameter_IsClamped()
    {
        await using var h = await Host.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await h.Summary(days: 0)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Summary(days: 100_000)).StatusCode);
    }

    // ---------- CORS: only the wallet's origin ----------

    [Fact]
    public async Task Cors_AnswersTheWalletOrigin_AndNoOtherOrigin()
    {
        await using var h = await Host.StartAsync();

        var wallet = await h.Post("home_view", origin: WalletOrigin);
        var foreign = await h.Post("home_view", origin: "https://evil.example");

        Assert.Equal(WalletOrigin, wallet.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(foreign.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_Preflight_AllowsPostFromTheWalletOnly()
    {
        await using var h = await Host.StartAsync();

        HttpRequestMessage Preflight(string origin)
        {
            var r = new HttpRequestMessage(HttpMethod.Options, "/api/metrics/event/home_view");
            r.Headers.Add("Origin", origin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            return r;
        }

        var ok = await h.Http.SendAsync(Preflight(WalletOrigin));
        var no = await h.Http.SendAsync(Preflight("https://evil.example"));

        Assert.Equal(WalletOrigin, ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("POST", ok.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.False(no.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_WithNoConfiguredOrigins_AcceptsNoBrowserOrigin()
    {
        await using var h = await Host.StartAsync(origins: []);

        var response = await h.Post("home_view", origin: WalletOrigin);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ---------- nothing identifying is stored or logged ----------

    [Fact]
    public void TheDatabase_HasNoColumnThatCouldHoldAnIdentifier()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metrics-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new MetricsStore($"Data Source={path};Pooling=False"))
                store.Increment("home_view", new DateOnly(2026, 10, 19));

            using var db = new SqliteConnection($"Data Source={path};Pooling=False");
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT name FROM pragma_table_info('metric_count') ORDER BY cid";
            var columns = new List<string>();
            using (var r = cmd.ExecuteReader())
                while (r.Read()) columns.Add(r.GetString(0));

            Assert.Equal(["day", "event", "n"], columns);

            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table'";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheMetricsTypes_TakeNoLogger_SoNothingCanBeLoggedAboutAClient()
    {
        var types = new[] { typeof(MetricsStore), typeof(MetricsRateLimiter), typeof(MetricsEndpoints) };
        foreach (var t in types)
        {
            var ctorParams = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).SelectMany(c => c.GetParameters());
            Assert.DoesNotContain(ctorParams, p => p.ParameterType.Name.StartsWith("ILogger", StringComparison.Ordinal));

            var handlerParams = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters());
            Assert.DoesNotContain(handlerParams, p => p.ParameterType.Name.StartsWith("ILogger", StringComparison.Ordinal));
        }
    }
}

public class MetricsRateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static IPAddress Ip(int n) => IPAddress.Parse($"10.0.0.{n}");

    [Fact]
    public void AllowsUpToTheLimit_ThenBlocks()
    {
        var l = new MetricsRateLimiter(limit: 3);
        Assert.True(l.Allow(Ip(1), T0));
        Assert.True(l.Allow(Ip(1), T0));
        Assert.True(l.Allow(Ip(1), T0));
        Assert.False(l.Allow(Ip(1), T0));
    }

    [Fact]
    public void ClientsAreIndependent()
    {
        var l = new MetricsRateLimiter(limit: 1);
        Assert.True(l.Allow(Ip(1), T0));
        Assert.False(l.Allow(Ip(1), T0));
        Assert.True(l.Allow(Ip(2), T0));
    }

    [Fact]
    public void WindowRestarts_AfterItEnds()
    {
        var l = new MetricsRateLimiter(limit: 1, window: TimeSpan.FromSeconds(10));
        Assert.True(l.Allow(Ip(1), T0));
        Assert.False(l.Allow(Ip(1), T0.AddSeconds(9)));
        Assert.True(l.Allow(Ip(1), T0.AddSeconds(10)));
    }

    [Fact]
    public void UnknownAddress_SharesOneBucket_InsteadOfBypassingTheLimit()
    {
        var l = new MetricsRateLimiter(limit: 1);
        Assert.True(l.Allow(null, T0));
        Assert.False(l.Allow(null, T0));
    }

    [Fact]
    public void Constructor_RejectsNonsense() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetricsRateLimiter(limit: 0));
}
