using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
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
        // GET /v1/pay/public/stores/{id}/donation-options — what a store's tip page can offer (specs/033 US4,
        // contract C2). Read-only: it never derives or creates an address. The DD address appears only while the
        // store has DigiDollar on and DigiPay offers it, so turning DD off removes it from every embedded button.
        group.MapGet("/stores/{id}/donation-options", async (string id, DigiPayDbContext db, DdSurfacesGate ddGate, DdAvailability availability) =>
        {
            var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (store is null) return Results.NotFound(new { error = "not_found" });
            var ddAddress = store.DigiDollarEnabled && ddGate.Enabled ? store.DdDonationAddress : null;
            var ddAvailable = ddAddress is not null && await availability.IsAvailableAsync();
            return Results.Ok(new
            {
                storeName = store.Name,
                network = store.Network,
                // Address-mode stores donate to their address; an xpub store's DGB donation address stays in the
                // button's own `to` parameter, as before.
                dgbAddress = string.IsNullOrEmpty(store.ReceiveAddress) ? null : store.ReceiveAddress,
                ddAddress,
                ddAvailable,
                ddReason = ddAddress is null ? null : ddAvailable ? null : "unavailable",
            });
        }).RequireRateLimiting("checkout-option");

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
        // DGB price for the POS, payment links and the checkout's price-drift notice. Public (no auth) so paired
        // tablets don't need their key just for a rate. Every currency in the wallet's list is accepted, priced the
        // way the wallet does it (DgbPriceService); anything else is refused before any upstream call, so this
        // can't be used as an open proxy.
        group.MapGet("/price", async (string? currency, DgbPriceService prices, CancellationToken ct) =>
        {
            var code = (currency ?? "usd").Trim();
            if (!DgbPriceService.IsSupported(code))
                return Results.BadRequest(new { error = "unsupported currency" });
            var quote = await prices.GetAsync(code, ct);
            return quote is null ? Results.StatusCode(502) : Results.Ok(quote);
        });

        // GET /v1/pay/public/currencies
        // The fiat currencies DigiPay can price in (the wallet's list): code, name, symbol, region, decimals.
        group.MapGet("/currencies", () => Results.Ok(new
        {
            regions = DigiByte.Wallet.Models.FiatCurrencies.Regions,
            currencies = DigiByte.Wallet.Models.FiatCurrencies.All.Select(c => new
            {
                c.Code, c.Name, Symbol = c.Symbol.Trim(), c.Region, c.Decimals,
                Flag = DigiByte.Wallet.Models.FiatCurrencies.FlagFor(c.Code),
            }),
        }));

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
