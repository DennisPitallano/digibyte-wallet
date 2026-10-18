using DigiByte.NodeApi.Middleware;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Metrics;

/// <summary>
/// Anonymous aggregate usage counters for SC-005 (research R17, FR-023): <c>POST /api/metrics/event/{name}</c> with an empty
/// body increments a per-day count. It accepts only allowlisted event names, stores and logs nothing else (no IP, user agent
/// or cookie), is rate limited per client in memory, and answers only the wallet's origin (CORS).
/// <c>GET /api/metrics/summary</c> returns the counts and needs the operator key.
/// </summary>
public static class MetricsEndpoints
{
    public const string CorsPolicy = "MetricsWalletOnly";

    public static readonly string[] AllowedEvents = ["dd_section_view", "home_view", "dd_send_attempted"];

    /// <summary>Registers the CORS policy: only the wallet's own origin(s), POST only; none configured means none accepted.</summary>
    public static void AddMetricsCorsPolicy(this Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions options, string[] walletOrigins) =>
        options.AddPolicy(CorsPolicy, policy =>
        {
            if (walletOrigins.Length > 0) policy.WithOrigins(walletOrigins).WithMethods("POST").AllowAnyHeader();
            else policy.SetIsOriginAllowed(_ => false);
        });

    public static RouteGroupBuilder MapMetricsEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/event/{name}", RecordEvent).WithName("RecordMetricEvent").RequireCors(CorsPolicy);
        group.MapGet("/summary", Summary).WithName("GetMetricsSummary");
        return group;
    }

    private static IResult RecordEvent(
        HttpContext http, string name, MetricsStore store, MetricsRateLimiter limiter, TimeProvider clock)
    {
        var now = clock.GetUtcNow();

        // Rate limit first, so a flood of junk is limited too. The address is only an in-memory key.
        if (!limiter.Allow(http.Connection.RemoteIpAddress, now))
        {
            http.Response.Headers.RetryAfter = ((int)limiter.Window.TotalSeconds).ToString();
            return Results.Json(new { error = "rate_limited" }, statusCode: 429);
        }

        if (!AllowedEvents.Contains(name, StringComparer.Ordinal))
            return Results.Json(new { error = "unknown_event" }, statusCode: 400);

        // The request carries no data at all: the name in the path is the whole message.
        var request = http.Request;
        if (request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
            return Results.Json(new { error = "body_not_allowed" }, statusCode: 400);

        store.Increment(name, DateOnly.FromDateTime(now.UtcDateTime));
        return Results.NoContent();
    }

    private static IResult Summary(
        HttpContext http, MetricsStore store, DigiDollarConfig config, IHostEnvironment env, int days = 30)
    {
        var verdict = OperatorKey.Check(config.WriteKey, http.Request.Headers[OperatorKey.HeaderName].ToString(), env.IsDevelopment());
        if (verdict == OperatorKey.Result.NotConfigured)
            return Results.Json(new { error = "write_key_not_configured" }, statusCode: 503);
        if (verdict == OperatorKey.Result.Unauthorized)
            return Results.Json(new { error = "unauthorized" }, statusCode: 401);

        var rows = store.Summary(Math.Clamp(days, 1, 366));
        return Results.Json(new
        {
            days = rows.Select(r => new Dictionary<string, object> { ["date"] = r.Date }
                .Concat(AllowedEvents.Select(e => new KeyValuePair<string, object>(e, r.Events.GetValueOrDefault(e))))
                .ToDictionary(kv => kv.Key, kv => kv.Value)),
        });
    }
}
