using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Account-level self-service endpoints. Anything store-scoped (receive, webhook,
/// session defaults) lives in <see cref="StoresEndpoints"/>.
/// </summary>
public static class MerchantMeEndpoints
{
    public static RouteGroupBuilder MapMerchantMeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (HttpRequest http, DigiPayDbContext db, DdSurfacesGate ddGate) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            var merchant = caller.Merchant;
            var stores = await db.Stores.AsNoTracking()
                .Where(s => s.MerchantId == merchant.Id)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();
            return Results.Ok(new
            {
                merchant.Id,
                merchant.DisplayName,
                merchant.DigiIdAddress,
                merchant.CreatedAt,
                // specs/033: whether the DD surfaces are switched on server-wide (FR-032), and the one-time
                // buyer-choice notice (FR-030), which only shows once they are.
                DdSurfacesEnabled = ddGate.Enabled,
                merchant.DdChoiceNoticePending,
                // A store-bound POS tablet only sees its own store.
                Stores = stores.Where(s => caller.CanUseStore(s.Id)).Select(s => StoreDto(s, http)),
            });
        }).AllowPosKey();

        group.MapPatch("", async (UpdateMeRequest body, HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            if (body.DisplayName is not null)
            {
                var trimmed = body.DisplayName.Trim();
                if (trimmed.Length == 0 || trimmed.Length > 120)
                    return Results.BadRequest(new { error = "displayName must be 1-120 chars" });
                merchant.DisplayName = trimmed;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { merchant.Id, merchant.DisplayName, merchant.DigiIdAddress });
        });

        // POST /v1/pay/me/notices/dd-choice/dismiss — clears the one-time "buyers can now pick DGB or DD" notice for
        // good (specs/033 FR-030). Dashboard sessions only (dps_), never API keys; idempotent.
        group.MapPost("/notices/dd-choice/dismiss", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            if (caller.KeyKind is not null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var merchant = await db.Merchants.FirstAsync(m => m.Id == caller.Merchant.Id);
            if (merchant.DdChoiceNoticePending)
            {
                merchant.DdChoiceNoticePending = false;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        // GET /v1/pay/me/audit?limit=50&cursor={iso-utc}
        //
        // Merchant-scoped audit trail. Reverse-chronological, cursor-paginated
        // on CreatedAt so deep history stays stable across inserts.
        group.MapGet("/audit", async (HttpRequest http, DigiPayDbContext db, int? limit, string? cursor) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            var take = Math.Clamp(limit ?? 50, 1, 200);
            DateTime? before = null;
            if (!string.IsNullOrWhiteSpace(cursor) && DateTime.TryParse(cursor,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                before = parsed;
            }

            var q = db.AuditEvents.AsNoTracking()
                .Where(a => a.MerchantId == merchant.Id);
            if (before is not null) q = q.Where(a => a.CreatedAt < before);

            var rows = await q.OrderByDescending(a => a.CreatedAt).Take(take + 1).ToListAsync();
            var hasMore = rows.Count > take;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            return Results.Ok(new
            {
                items = rows.Select(a => new
                {
                    a.Id,
                    a.Action,
                    a.ActorType,
                    a.ActorId,
                    a.ActorIp,
                    a.TargetType,
                    a.TargetId,
                    a.Summary,
                    a.Metadata,
                    a.CreatedAt,
                }),
                nextCursor = hasMore ? rows[^1].CreatedAt.ToString("O") : null,
            });
        });

        return group;
    }

    /// <summary>The store as the API returns it. <paramref name="http"/> supplies the DD surfaces switch.</summary>
    internal static object StoreDto(PayStore s, HttpRequest http)
    {
        var ddSurfaces = http.HttpContext.RequestServices.GetRequiredService<DdSurfacesGate>().Enabled;
        return StoreDto(s, ddSurfaces);
    }

    internal static object StoreDto(PayStore s, bool ddSurfacesEnabled) => new
    {
        s.Id,
        s.MerchantId,
        s.Name,
        s.Network,
        ReceiveMode = !string.IsNullOrEmpty(s.Xpub) ? "xpub"
            : !string.IsNullOrEmpty(s.ReceiveAddress) ? "address"
            : "unconfigured",
        HasReceive = !string.IsNullOrEmpty(s.Xpub) || !string.IsNullOrEmpty(s.ReceiveAddress),
        ReceivePreview = !string.IsNullOrEmpty(s.Xpub) ? $"{s.Xpub[..12]}…"
            : !string.IsNullOrEmpty(s.ReceiveAddress) ? s.ReceiveAddress
            : null,
        s.WebhookUrl,
        HasWebhookSecret = !string.IsNullOrEmpty(s.WebhookSecret),
        s.DefaultSessionExpiryMinutes,
        s.DigiDollarEnabled,
        s.DefaultCurrency,
        // A saved "DD" default acts as USD while DD can't be offered (specs/033 FR-008). The stored value is kept so
        // turning DD back on restores it.
        DefaultCurrencyEffective = s.DefaultCurrency == "DD" && !(s.DigiDollarEnabled && ddSurfacesEnabled)
            ? "USD" : s.DefaultCurrency,
        s.BuyerChoiceEnabled,
        s.DdDonationAddress,
        s.CreatedAt,
    };

    internal static Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db) =>
        MerchantAuthenticator.AuthenticateAsync(http, db);
}

public record UpdateMeRequest(string? DisplayName);
