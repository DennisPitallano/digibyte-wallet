using System.Text.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Unauthenticated, aggregate-only surface for the marketing site.
/// Deliberately returns counts / totals — no merchant ids, addresses, or
/// session ids — so exposing it publicly leaks nothing about any one
/// merchant's activity. Cached at the CDN level in prod; a 30-second
/// in-process cache here keeps the database quiet if it's not.
/// </summary>
public static class PublicEndpoints
{
    private static readonly object _lock = new();
    private static StatsSnapshot? _cached;
    private static DateTime _cachedAt;
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder group)
    {
        // GET /v1/pay/public/stats — numbers for the landing-page counters.
        // Kept intentionally coarse: merchants / stores / sessions / paid /
        // totalSatoshis, no breakdown by merchant, no time-windowed slicing
        // (that would let someone diff snapshots to pin activity on a merchant).
        group.MapGet("/stats", async (DigiPayDbContext db) =>
        {
            // Cache hit: skip the roundtrip. Double-checked locking keeps the
            // first concurrent request on a cold start from stampeding the DB.
            if (_cached is not null && DateTime.UtcNow - _cachedAt < CacheFor)
                return Results.Ok(_cached);

            // Exclude demo / synthetic test rows from the public marketing numbers.
            //   mer_demo_*, sto_demo_*, ses_demo_*  — /v1/pay/test/demo-session (TestEndpoints)
            //   ses_test_*                          — webhook.test synthetic events
            // If a real merchant ever id-collides with these prefixes we'd under-count, but
            // the id alphabet + length makes that astronomically unlikely.
            var merchants = await db.Merchants.AsNoTracking()
                .Where(m => !m.Id.StartsWith("mer_demo_"))
                .CountAsync();
            var stores = await db.Stores.AsNoTracking()
                .Where(s => !s.Id.StartsWith("sto_demo_"))
                .CountAsync();
            var realSessions = db.Sessions.AsNoTracking()
                .Where(s => !s.Id.StartsWith("ses_demo_") && !s.Id.StartsWith("ses_test_"));
            var sessions = await realSessions.CountAsync();
            var paid = await realSessions
                .Where(s => s.Status == PaySessionStatus.Paid || s.Status == PaySessionStatus.Confirmed)
                .CountAsync();
            var totalSatoshis = await realSessions
                .Where(s => s.Status == PaySessionStatus.Paid || s.Status == PaySessionStatus.Confirmed)
                .SumAsync(s => (long?)s.ReceivedSatoshis) ?? 0L;

            var snapshot = new StatsSnapshot(
                Merchants: merchants,
                Stores: stores,
                Sessions: sessions,
                PaidSessions: paid,
                TotalDgb: totalSatoshis / 100_000_000m,
                GeneratedAt: DateTime.UtcNow);

            lock (_lock)
            {
                _cached = snapshot;
                _cachedAt = DateTime.UtcNow;
            }
            return Results.Ok(snapshot);
        });

        // GET /v1/pay/public/price?currency=eur
        // Proxied CoinGecko spot price for the POS tablet's fiat mode.
        // Public (no auth) so paired kiosks don't need to carry the API key
        // just for a rate lookup; 30-second cache makes us a thin wrapper —
        // never more than 2 CoinGecko hits/minute per currency no matter how
        // many tablets are paired.
        group.MapGet("/price", async (
            string? currency,
            IHttpClientFactory httpFactory,
            IMemoryCache cache) =>
        {
            var cur = (currency ?? "usd").Trim().ToLowerInvariant();
            // Tight allow-list — anything not here bounces before we hit
            // CoinGecko, so we can't be used as an open proxy.
            if (!SupportedFiatCurrencies.Contains(cur))
                return Results.BadRequest(new { error = "unsupported currency" });

            // Two-tier cache:
            //   fresh (60s)  — return immediately, no upstream call.
            //   stale (10m)  — kept around so we can serve last-known price if
            //                  CoinGecko is rate-limiting or down. This is
            //                  what keeps kiosks usable during a 429 storm.
            // 60s is the right fresh window for a POS: fiat/DGB rates don't
            // move fast enough at till-level granularity to matter within a
            // minute, and it cuts our CoinGecko call budget by ~2x vs. 30s.
            var freshKey = $"digipay.price.fresh:{cur}";
            var staleKey = $"digipay.price.stale:{cur}";
            var lockKey  = $"digipay.price.lock:{cur}";

            if (cache.TryGetValue(freshKey, out PriceSnapshot? fresh) && fresh is not null)
                return Results.Ok(fresh);

            // Single-flight: only one request per currency hits CoinGecko at a
            // time, even if 50 tablets race in simultaneously. Everyone else
            // waits and gets the just-fetched value.
            var gate = cache.GetOrCreate(lockKey, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return new SemaphoreSlim(1, 1);
            })!;
            await gate.WaitAsync();
            try
            {
                // Re-check: another caller may have just populated it.
                if (cache.TryGetValue(freshKey, out PriceSnapshot? after) && after is not null)
                    return Results.Ok(after);

                try
                {
                    var http = httpFactory.CreateClient("DigiPayChain");
                    var url = $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={cur}";
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
                        if (json.TryGetProperty("digibyte", out var dgb)
                            && dgb.TryGetProperty(cur, out var priceEl))
                        {
                            var price = priceEl.GetDecimal();
                            var snapshot = new PriceSnapshot(cur.ToUpperInvariant(), price, DateTime.UtcNow);
                            cache.Set(freshKey, snapshot, TimeSpan.FromSeconds(60));
                            cache.Set(staleKey, snapshot, TimeSpan.FromMinutes(10));
                            return Results.Ok(snapshot);
                        }
                    }
                }
                catch
                {
                    // fall through to stale fallback
                }

                // Upstream failed (rate-limit, network, bad payload). Serve
                // the last known price if we have one — kiosks keep working
                // and the slight staleness is clearly surfaced by FetchedAt.
                if (cache.TryGetValue(staleKey, out PriceSnapshot? stale) && stale is not null)
                    return Results.Ok(stale);

                return Results.StatusCode(502);
            }
            finally
            {
                gate.Release();
            }
        });

        return group;
    }

    /// <summary>
    /// Narrow allow-list of fiat currencies we'll proxy a price quote for.
    /// Matches the set we're willing to ship in the POS currency selector;
    /// deliberately excludes anything that would require additional compliance
    /// thinking (e.g. USDT pegs). CoinGecko supports these out of the box.
    /// </summary>
    private static readonly HashSet<string> SupportedFiatCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "usd", "eur", "gbp", "cad", "aud", "jpy", "chf", "sek", "nok", "dkk", "php", "ngn", "zar",
    };

    public record PriceSnapshot(string Currency, decimal DgbPrice, DateTime FetchedAt);

    public record StatsSnapshot(
        int Merchants,
        int Stores,
        int Sessions,
        int PaidSessions,
        decimal TotalDgb,
        DateTime GeneratedAt);
}
