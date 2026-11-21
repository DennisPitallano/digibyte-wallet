namespace DigiByte.Web.Services;

/// <summary>Explorer request counts for one benchmark run (spec 023, FR-010b). In memory only: never saved, logged or sent.</summary>
public sealed record RequestCounts(int Summary, int HistoryPage, int TipHeight, int Other)
{
    public int Total => Summary + HistoryPage + TipHeight + Other;
}

/// <summary>
/// Holds the counts while a benchmark run is open. Outside a run nothing is counted. One at a time: the page runs them in order.
/// </summary>
public sealed class RequestCounter
{
    private int _summary, _page, _tip, _other;
    private volatile bool _open;

    public bool IsOpen => _open;

    public void Start()
    {
        Interlocked.Exchange(ref _summary, 0);
        Interlocked.Exchange(ref _page, 0);
        Interlocked.Exchange(ref _tip, 0);
        Interlocked.Exchange(ref _other, 0);
        _open = true;
    }

    public RequestCounts Stop()
    {
        _open = false;
        return new RequestCounts(_summary, _page, _tip, _other);
    }

    /// <summary>Sorts one explorer request by its path: <c>/address/{a}</c>, <c>/address/{a}/txs…</c>, <c>/blocks/tip/height</c>, or other.</summary>
    internal void Count(Uri? uri)
    {
        if (!_open || uri is null) return;
        var path = uri.AbsolutePath;
        var at = path.IndexOf("/api/", StringComparison.Ordinal);
        var parts = (at >= 0 ? path[(at + 5)..] : path.TrimStart('/')).Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["address", _]: Interlocked.Increment(ref _summary); break;
            case ["address", _, "txs", ..]: Interlocked.Increment(ref _page); break;
            case ["blocks", "tip", "height"]: Interlocked.Increment(ref _tip); break;
            default: Interlocked.Increment(ref _other); break;
        }
    }
}

/// <summary>
/// Counts the requests the explorer client really sends (inside the retry handler, so a retry counts as another request), only while
/// a <see cref="RequestCounter"/> run is open. It looks at the path only and keeps nothing else.
/// </summary>
public sealed class RequestCountingHandler(RequestCounter counter) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        counter.Count(request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }
}
