using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

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

        return group;
    }

    public record StatsSnapshot(
        int Merchants,
        int Stores,
        int Sessions,
        int PaidSessions,
        decimal TotalDgb,
        DateTime GeneratedAt);
}
