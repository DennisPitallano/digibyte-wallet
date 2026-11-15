using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The wallet's oracle feed (specs/014-digibyte-io-oracle): reads the data service's oracle route in the background and keeps one shared answer.
/// It never makes a caller wait: <see cref="Current"/> and <see cref="IsLoading"/> only read what is held, and <see cref="RequestRefresh"/> starts
/// a fetch on its own and returns at once. A fetch is limited to 5 seconds. There is at most one attempt every 30 seconds, counted from the
/// previous attempt whether it worked or not, and none while one is running. Anything unusable (an error status, a wrong shape, a price not above
/// zero) is no reading. The request is a plain GET with no query string, body or custom headers, so it carries nothing about the user and a
/// browser sends no preflight request for it. The configured address must be https (or http on a loopback address, for development), otherwise
/// the feed does nothing.
/// </summary>
public sealed class OracleDataServiceClient : IOracleFeed
{
    private static readonly TimeSpan FetchLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly string? _url;
    private readonly TimeProvider _time;

    private OracleReading? _reading;
    private DateTimeOffset _receivedAt;
    private bool _fetching;
    private bool _firstFetchDone;
    private DateTimeOffset? _lastAttempt;
    private int _retries;
    private ITimer? _retryTimer;

    public OracleDataServiceClient(HttpClient http, string? url, TimeProvider? time = null)
    {
        _http = http;
        _url = IsAcceptableUrl(url) ? url!.Trim() : null;
        _time = time ?? TimeProvider.System;
    }

    public event Action? Changed;

    /// <summary>
    /// Whether the wallet should talk to the oracle route at all: the <c>OracleEnabled</c> switch is on, DigiDollar reads from the explorer (the node
    /// backend has its own oracle data), and the address is usable. Anything else means no request, ever (spec 014 SC-007).
    /// </summary>
    public static bool IsEnabled(bool oracleEnabled, bool explorerBackend, string? url) =>
        oracleEnabled && explorerBackend && IsAcceptableUrl(url);

    /// <summary>True for an https address, or an http address on a loopback host (development only).</summary>
    public static bool IsAcceptableUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    public OracleReading? Current
    {
        get
        {
            lock (_gate)
                return _reading is { } r && _time.GetUtcNow() - r.FetchedAtUtc <= MaxAge ? r : null;
        }
    }

    public bool IsLoading
    {
        get
        {
            lock (_gate)
                return _fetching && !_firstFetchDone && !(_reading is { } r && _time.GetUtcNow() - r.FetchedAtUtc <= MaxAge);
        }
    }

    /// <summary>After a failed fetch with no reading held, the feed asks again by itself this many times (one per 30 seconds), so a service that was
    /// briefly down does not leave a screen without a price until the next page load. Any caller's request starts the count again.</summary>
    private const int MaxAutoRetries = 3;

    public void RequestRefresh() => Refresh(fromRetry: false);

    private void Refresh(bool fromRetry)
    {
        if (_url is null)
            return;
        lock (_gate)
        {
            if (!fromRetry)
                _retries = 0;
            if (_fetching)
                return;
            var now = _time.GetUtcNow();
            if (_lastAttempt is { } last && now - last < Spacing)
                return;
            if (_reading is not null && now - _receivedAt < Spacing)
                return;
            _fetching = true;
            _lastAttempt = now;
        }
        // Started away from the caller's thread, so a request that answers at once cannot run inside a caller's lock.
        _ = Task.Run(FetchAsync);
    }

    private async Task FetchAsync()
    {
        OracleReading? reading = null;
        try
        {
            using var limit = new CancellationTokenSource(FetchLimit, _time);
            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            using var response = await _http.SendAsync(request, limit.Token);
            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(limit.Token);
                if (OracleReading.TryParse(text, _time.GetUtcNow(), out var parsed))
                    reading = parsed;
            }
        }
        catch (Exception)
        {
            // Any failure, including the 5-second limit, simply means no new reading.
        }

        lock (_gate)
        {
            if (reading is not null)
            {
                _reading = reading;
                _receivedAt = _time.GetUtcNow();
            }
            _firstFetchDone = true;
            _fetching = false;
            if (reading is not null)
                _retries = 0;
            else if (_reading is null && _retries < MaxAutoRetries)
            {
                _retries++;
                _retryTimer?.Dispose();
                _retryTimer = _time.CreateTimer(_ => Refresh(fromRetry: true), null, Spacing, Timeout.InfiniteTimeSpan);
            }
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // A listener's failure never affects the feed.
        }
    }
}
