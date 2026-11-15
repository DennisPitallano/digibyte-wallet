namespace DigiByte.SpvGateway.Endpoints;

/// <summary>One request the gateway answered: when, which kind of call, how it ended and how long it took. Nothing that identifies a wallet.</summary>
/// <param name="Test">True for a request the operator sent from the status page (it carries <c>X-Test-Request</c>), so it is not mistaken for a wallet.</param>
public sealed record RequestEntry(DateTimeOffset At, string Method, string Route, int Status, int Ms, bool Test = false);

/// <summary>
/// The last few requests the gateway answered, kept in memory only, so an operator can see that wallets are reaching it. It records the
/// route pattern (such as <c>/v1/tx/{txid}</c>, never the value in it), the status and the time taken: no query string, no body, no
/// addresses, no transaction ids and nothing about who asked. It is lost on restart.
/// </summary>
public sealed class RequestLog(int capacity = 40)
{
    private readonly object _gate = new();
    private readonly Queue<RequestEntry> _recent = new();
    private readonly Dictionary<string, long> _byRoute = [];
    private long _total;
    private long _errors;

    public void Record(RequestEntry entry)
    {
        lock (_gate)
        {
            _total++;
            if (entry.Status >= 400)
                _errors++;
            var key = $"{entry.Method} {entry.Route}";
            _byRoute[key] = _byRoute.GetValueOrDefault(key) + 1;
            _recent.Enqueue(entry);
            while (_recent.Count > capacity)
                _recent.Dequeue();
        }
    }

    /// <summary>Forgets every request and starts the totals again from zero.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _recent.Clear();
            _byRoute.Clear();
            _total = 0;
            _errors = 0;
        }
    }

    /// <summary>The totals and the recent requests, newest first.</summary>
    public (long Total, long Errors, IReadOnlyDictionary<string, long> ByRoute, IReadOnlyList<RequestEntry> Recent) Snapshot()
    {
        lock (_gate)
            return (_total, _errors, new Dictionary<string, long>(_byRoute), _recent.Reverse().ToList());
    }
}

/// <summary>
/// Records each answered request in the <see cref="RequestLog"/>. The status page's own polling (it sends <c>X-Status-Page</c>), the page
/// and its files, the operator reset, and cross-origin preflight checks are left out, so what is listed is what wallets and monitors asked for.
/// </summary>
public sealed class RequestLogMiddleware(RequestDelegate next, RequestLog log, SpvGatewayOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.ShowRequestLog)
        {
            await next(context);
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            Record(context, started);
        }
    }

    private void Record(HttpContext context, long started)
    {
        var request = context.Request;
        if (HttpMethods.IsOptions(request.Method) || request.Headers.ContainsKey("X-Status-Page"))
            return;
        // Only requests that matched an API route are listed; the route pattern is used, so identifiers in the path never appear.
        if (context.GetEndpoint() is not RouteEndpoint endpoint || endpoint.RoutePattern.RawText is not { } route)
            return;
        if (route is "/" or "/status.json" or "/status/reset")
            return;
        log.Record(new RequestEntry(DateTimeOffset.UtcNow, request.Method, route, context.Response.StatusCode, (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, request.Headers.ContainsKey("X-Test-Request")));
    }
}
