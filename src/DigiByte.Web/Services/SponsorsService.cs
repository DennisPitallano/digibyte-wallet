using System.Text.Json;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Loads the sponsors list from GitHub when the Sponsors page opens. One request per load, no polling, no cache busting
/// on open (the browser cache's short lifetime bounds how fresh the list is); the Refresh button does bust it. A fully valid list is kept in local storage
/// as the last good copy; if a later fetch fails, that copy is shown and labelled out of date. Nothing else is stored and
/// nothing here touches wallet data. See specs/006-sponsors-page/contracts/hosting-and-ui.md.
/// </summary>
public sealed class SponsorsService
{
    public const string StorageKey = "dgb-sponsors-lastgood";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly string _baseUrl;
    private readonly TimeProvider _clock;

    public SponsorsService(HttpClient http, IJSRuntime js, string baseUrl, TimeProvider? clock = null)
    {
        _http = http;
        _js = js;
        _baseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        _clock = clock ?? TimeProvider.System;
    }

    /// <param name="maxAge">
    /// When given and the saved copy is younger than this, it is returned as a fresh result and nothing is requested. The Home
    /// strip uses this so opening Home does not contact GitHub more than once per period.
    /// </param>
    /// <param name="bypassCache">
    /// Set by the Refresh button: asks for the list with a changing query so a copy held by the browser or GitHub's edge is not reused.
    /// The path (what the content security policy checks) is unchanged.
    /// </param>
    public async Task<SponsorLoadResult> LoadAsync(CancellationToken ct = default, TimeSpan? maxAge = null, bool bypassCache = false)
    {
        if (maxAge is { } limit)
        {
            var saved = await TryLoadStoredAsync();
            if (saved is not null && _clock.GetUtcNow() - saved.FetchedAt < limit)
                return new SponsorLoadResult
                {
                    State = saved.Entries.Count == 0 ? SponsorLoadState.FreshEmpty : SponsorLoadState.Fresh,
                    List = saved,
                };
        }

        var text = await TryFetchAsync(bypassCache, ct);
        if (text is not null)
        {
            var parsed = SponsorListParser.Parse(text, _baseUrl);
            if (parsed.Readable)
            {
                var now = _clock.GetUtcNow();
                await TryStoreAsync(text, now);
                var list = new SponsorList { Entries = parsed.Entries, FetchedAt = now, SkippedCount = parsed.SkippedCount };
                return new SponsorLoadResult
                {
                    State = parsed.Entries.Count == 0 ? SponsorLoadState.FreshEmpty : SponsorLoadState.Fresh,
                    List = list,
                };
            }
        }

        var stored = await TryLoadStoredAsync();
        return stored is null
            ? new SponsorLoadResult { State = SponsorLoadState.Unavailable }
            : new SponsorLoadResult { State = SponsorLoadState.Stale, List = stored };
    }

    private async Task<string?> TryFetchAsync(bool bypassCache, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _http.GetAsync(_baseUrl + "sponsors.json" + (bypassCache ? "?t=" + _clock.GetUtcNow().ToUnixTimeSeconds() : ""), timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Offline, timeout, rate limit, blocked: all the same to the visitor.
            return null;
        }
    }

    private async Task TryStoreAsync(string text, DateTimeOffset fetchedAt)
    {
        try
        {
            var record = JsonSerializer.Serialize(new StoredCopy(fetchedAt, text));
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, record);
        }
        catch (Exception)
        {
            // Storage can be blocked (private window, cleared data); the page still works without a copy.
        }
    }

    private async Task<SponsorList?> TryLoadStoredAsync()
    {
        try
        {
            var raw = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(raw)) return null;
            var record = JsonSerializer.Deserialize<StoredCopy>(raw);
            if (record is null || string.IsNullOrEmpty(record.Text)) return null;
            var parsed = SponsorListParser.Parse(record.Text, _baseUrl);
            if (!parsed.Readable) return null;
            return new SponsorList { Entries = parsed.Entries, FetchedAt = record.FetchedAt, SkippedCount = parsed.SkippedCount };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record StoredCopy(DateTimeOffset FetchedAt, string Text);
}
