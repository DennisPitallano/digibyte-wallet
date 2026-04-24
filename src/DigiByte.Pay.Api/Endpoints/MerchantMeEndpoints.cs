using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
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
        group.MapGet("", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();
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
                Stores = stores.Select(StoreDto),
            });
        });

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

    internal static object StoreDto(PayStore s) => new
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
        s.CreatedAt,
    };

    internal static Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db) =>
        MerchantAuthenticator.AuthenticateAsync(http, db);
}

public record UpdateMeRequest(string? DisplayName);
