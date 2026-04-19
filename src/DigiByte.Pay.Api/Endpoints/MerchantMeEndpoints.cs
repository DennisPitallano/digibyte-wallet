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
