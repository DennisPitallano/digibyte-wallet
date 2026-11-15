using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Services;

/// <summary>What the route serves to wallets (contracts/oracle-api.md). All fields are always present; no caller or upstream detail is passed on.</summary>
public sealed record OracleSnapshot(
    long PriceMicroUsd,
    long LastUpdateHeight,
    long LastUpdateTime,
    bool ReportedStale,
    bool VolatilityFreeze,
    bool MintingRestricted,
    bool AllOperationsRestricted,
    bool ErrActive,
    long FetchedAt,
    string Source = "digibyte.io");

/// <summary>
/// Reads the oracle price and the DigiDollar protection status from digibyte.io for the wallet (specs/014-digibyte-io-oracle). The wallet never
/// contacts digibyte.io itself: this relay does, with the same two fixed requests whoever asks. Each of the two reads is kept as one shared
/// answer: concurrent callers share one request, there is at most one attempt per read every 30 seconds whether the last one worked or not, an
/// answer is served until 5 minutes after it was fetched and never after, and anything unusable (wrong shape, price not above zero, oracle
/// status other than "active", too big, too slow, a redirect) counts as a failed attempt. The relay logs nothing about the caller.
/// </summary>
public sealed class OracleRelay(IHttpClientFactory httpFactory, IOptions<OracleOptions> options, TimeProvider time)
{
    public const string ClientName = "DigiByteIoOracle";

    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UpstreamLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(10);
    private const int MaxBytes = 64 * 1024;

    private readonly Kind<PricePart> _price = new();
    private readonly Kind<StatsPart> _stats = new();

    private sealed record PricePart(long PriceMicroUsd, long Height, long Time, bool ReportedStale, bool VolatilityFreeze, bool MintingRestricted, bool AllOperationsRestricted);

    private sealed record StatsPart(bool ErrActive);

    /// <summary>True when the route is switched on and its base address is acceptable.</summary>
    public bool Enabled => options.Value.IsUsable;

    /// <summary>The shared answer, or null when no usable answer younger than 5 minutes exists. Never throws.</summary>
    public async Task<OracleSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.TryGetBase(out var baseUrl))
            return null;

        await Task.WhenAll(
            _price.EnsureAsync(ct => FetchPriceAsync(baseUrl, ct), time),
            _stats.EnsureAsync(ct => FetchStatsAsync(baseUrl, ct), time)).WaitAsync(cancellationToken);

        var now = time.GetUtcNow();
        if (_price.Good(now) is not { } price || _stats.Good(now) is not { } stats)
            return null;

        // The older of the two reads sets the age the wallet sees, so the 5-minute limit holds for both.
        var fetchedAt = (price.At < stats.At ? price.At : stats.At).ToUnixTimeSeconds();
        var p = price.Value;
        return new OracleSnapshot(p.PriceMicroUsd, p.Height, p.Time, p.ReportedStale, p.VolatilityFreeze, p.MintingRestricted, p.AllOperationsRestricted, stats.Value.ErrActive, fetchedAt);
    }

    // ---------- one shared answer per kind of data ----------

    private sealed class Kind<T> where T : class
    {
        private readonly object _gate = new();
        private Task? _inflight;
        private DateTimeOffset? _lastAttempt;
        private (T Value, DateTimeOffset At)? _good;

        /// <summary>
        /// Starts an attempt unless one is already running (callers then share it) or the previous attempt, successful or not, began less than
        /// 30 seconds ago (callers then use what is held). Never throws.
        /// </summary>
        public Task EnsureAsync(Func<CancellationToken, Task<T?>> fetch, TimeProvider clock)
        {
            lock (_gate)
            {
                if (_inflight is not null)
                    return _inflight;
                var now = clock.GetUtcNow();
                if (_lastAttempt is { } last && now - last < Spacing)
                    return Task.CompletedTask;
                _lastAttempt = now;
                // Task.Run so the attempt always starts away from this lock, even when the upstream answers at once.
                return _inflight = Task.Run(() => RunAsync(fetch, clock));
            }
        }

        private async Task RunAsync(Func<CancellationToken, Task<T?>> fetch, TimeProvider clock)
        {
            T? value = null;
            try
            {
                using var limit = new CancellationTokenSource(UpstreamLimit, clock);
                value = await fetch(limit.Token);
            }
            catch (Exception)
            {
                // Any failure, including a timeout, is a failed attempt: the held answer (if young enough) keeps serving.
            }
            lock (_gate)
            {
                if (value is not null)
                    _good = (value, clock.GetUtcNow());
                _inflight = null;
            }
        }

        public (T Value, DateTimeOffset At)? Good(DateTimeOffset now)
        {
            lock (_gate)
                return _good is { } g && now - g.At <= MaxAge ? g : null;
        }
    }

    // ---------- the two reads ----------

    private async Task<PricePart?> FetchPriceAsync(string baseUrl, CancellationToken ct)
    {
        var text = await GetTextAsync($"{baseUrl}/getoracleprice", ct);
        if (text is null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!WholeNumber(root, "price_micro_usd", out var price) || price <= 0) return null;
            if (!WholeNumber(root, "last_update_height", out var height) || height <= 0) return null;
            if (!WholeNumber(root, "last_update_time", out var updated) || updated <= 0) return null;
            if (updated > time.GetUtcNow().Add(FutureTolerance).ToUnixTimeSeconds()) return null;
            if (!Flag(root, "is_stale", out var stale)) return null;
            if (!root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "active") return null;
            if (!root.TryGetProperty("mint_volatility", out var vol) || vol.ValueKind != JsonValueKind.Object) return null;
            if (!Flag(vol, "protection_active", out var freeze) || !Flag(vol, "minting_restricted", out var minting) || !Flag(vol, "all_operations_restricted", out var all)) return null;

            return new PricePart(price, height, updated, stale, freeze, minting, all);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<StatsPart?> FetchStatsAsync(string baseUrl, CancellationToken ct)
    {
        var text = await GetTextAsync($"{baseUrl}/getdigidollarstats", ct);
        if (text is null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!Flag(root, "is_emergency", out var emergency)) return null;
            if (!root.TryGetProperty("err_tier", out var tier) || tier.ValueKind != JsonValueKind.Object) return null;
            if (!tier.TryGetProperty("burn_multiplier", out var burn) || burn.ValueKind != JsonValueKind.Number || !burn.TryGetDouble(out var multiplier)) return null;

            // The emergency state is active when the emergency flag is set or the burn multiplier is above 1 (research R3).
            return new StatsPart(emergency || multiplier > 1);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A fixed GET with nothing from the caller, no redirects and a size cap. Null for anything but a plain 200 within the cap.</summary>
    private async Task<string?> GetTextAsync(string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            return null;
        if (response.Content.Headers.ContentLength is > MaxBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
                break;
            read += n;
        }
        return read > MaxBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static bool WholeNumber(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value);
    }

    private static bool Flag(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = v.GetBoolean();
        return true;
    }
}
