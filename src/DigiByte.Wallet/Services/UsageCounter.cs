namespace DigiByte.Wallet.Services;

/// <summary>
/// Anonymous aggregate usage counts for SC-005 (research R17, FR-023): an empty POST to
/// <c>/api/metrics/event/{name}</c> naming one of three events. The request carries no body, query string, cookie, custom
/// header, address, amount or identifier: the server learns only "this event happened once". It is fire-and-forget, never
/// throws into the UI, and after any failure it stays quiet for a few minutes instead of retrying.
///
/// This deliberately does NOT use the Google Analytics bootstrap (<c>wwwroot/js/analytics.js</c>) for DigiDollar events:
/// GA assigns a per-device client ID, which would break SC-005's "no per-user or per-device identifier".
/// </summary>
public sealed class UsageCounter
{
    public static readonly string[] Events = ["home_view", "dd_section_view", "dd_send_attempted"];

    /// <summary>How long to stay quiet after a failed or rejected call.</summary>
    public static readonly TimeSpan Backoff = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Func<bool> _enabled;
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _quietUntil = DateTimeOffset.MinValue;

    public UsageCounter(HttpClient http, string baseUrl, Func<bool> enabled, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _enabled = enabled;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task RecordAsync(string eventName, CancellationToken ct = default)
    {
        if (!Events.Contains(eventName, StringComparer.Ordinal)) return; // only the allowlisted names ever leave the device
        if (!_enabled()) return;
        if (_now() < _quietUntil) return;

        try
        {
            using var response = await _http.PostAsync($"{_baseUrl}/api/metrics/event/{eventName}", content: null, ct);
            if (response.IsSuccessStatusCode)
                _quietUntil = DateTimeOffset.MinValue;
            else
                _quietUntil = _now() + Backoff;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // the page went away: nothing to record
        }
        catch (Exception)
        {
            // Counting must never affect the wallet: stay quiet for a while and move on.
            _quietUntil = _now() + Backoff;
        }
    }
}
