using System.Net.Http.Json;
using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>
/// USD exchange rates for display currencies CoinGecko does not price DigiByte in (most African currencies, among others).
/// The wallet shows <c>DGB price in USD x rate</c>. Rates come from the same price proxy as the DGB price
/// (<c>{PriceApiUrl}/fx</c>), so the browser still talks to no extra third party. They are cached for six hours;
/// a failure returns null and the caller shows "no price" rather than a wrong number.
/// </summary>
public sealed class FiatRateProvider
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, decimal>? _rates;
    private DateTimeOffset _expires = DateTimeOffset.MinValue;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;

    public FiatRateProvider(HttpClient http, string? priceApiBaseUrl, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _baseUrl = priceApiBaseUrl?.TrimEnd('/');
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>How many units of <paramref name="code"/> one US dollar buys, or null when unknown or unreachable.</summary>
    public async Task<decimal?> GetPerUsdAsync(string code, CancellationToken ct = default)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return 1m;
        if (_baseUrl is null) return null; // no proxy configured: there is no rate source

        var rates = await GetRatesAsync(ct);
        return rates is not null && rates.TryGetValue(code.ToUpperInvariant(), out var rate) && rate > 0 ? rate : null;
    }

    private async Task<Dictionary<string, decimal>?> GetRatesAsync(CancellationToken ct)
    {
        if (_rates is not null && _now() < _expires) return _rates;
        if (_now() < _retryAfter) return _rates; // a recent failure: do not hammer the proxy

        await _gate.WaitAsync(ct);
        try
        {
            if (_rates is not null && _now() < _expires) return _rates;
            try
            {
                var data = await _http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/fx", ct);
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("rates", out var rates) && rates.ValueKind == JsonValueKind.Object)
                {
                    var parsed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in rates.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var v) && v > 0)
                            parsed[p.Name.ToUpperInvariant()] = v;
                    if (parsed.Count > 0)
                    {
                        _rates = parsed;
                        _expires = _now() + CacheDuration;
                        return _rates;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* unreachable proxy or bad payload: fall through to stale rates, if any */ }

            _retryAfter = _now() + TimeSpan.FromMinutes(1);
            return _rates; // stale rates beat no rates for a display-only value
        }
        finally { _gate.Release(); }
    }
}
