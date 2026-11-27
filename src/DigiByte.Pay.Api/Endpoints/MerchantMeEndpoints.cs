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
        group.MapGet("", async (HttpRequest http, DigiPayDbContext db, DdSurfacesGate ddGate, DdAvailability ddAvailability) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            var merchant = caller.Merchant;
            var stores = await db.Stores.AsNoTracking()
                .Where(s => s.MerchantId == merchant.Id)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();
            // specs/035 US4: networks where the explorer can't confirm DD right now, for stores that take DD (cached).
            var unpaidRuns = await DdUnpaidRunsAsync(db, stores.Select(s => s.DdKeyId));
            var ddDown = new List<string>();
            foreach (var network in stores.Where(s => s.DigiDollarEnabled).Select(s => s.Network == "mainnet" ? "mainnet" : "testnet").Distinct())
                if (!await ddAvailability.IsAvailableAsync(network)) ddDown.Add(network);
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
                DdUnavailableNetworks = ddDown,
                // A store-bound POS tablet only sees its own store.
                Stores = stores.Where(s => caller.CanUseStore(s.Id))
                    .Select(s => StoreDto(s, http, s.DdKeyId is { } k && unpaidRuns.TryGetValue(k, out var run) ? run : 0)),
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
    internal static object StoreDto(PayStore s, HttpRequest http, int ddUnpaidRun = 0)
    {
        var ddSurfaces = http.HttpContext.RequestServices.GetRequiredService<DdSurfacesGate>().Enabled;
        return StoreDto(s, ddSurfaces, ddUnpaidRun);
    }

    /// <summary>
    /// For each DD key: how many addresses its sequence has given out since the highest-index one that ever received DD
    /// (research R11). Counted per key across stores, since the merchant's wallet scans by key.
    /// </summary>
    internal static async Task<Dictionary<string, int>> DdUnpaidRunsAsync(DigiPayDbContext db, IEnumerable<string?> keyIds)
    {
        var ids = keyIds.OfType<string>().Distinct().ToList();
        if (ids.Count == 0) return [];
        var next = await db.DdKeySequences.AsNoTracking().Where(k => ids.Contains(k.Id))
            .ToDictionaryAsync(k => k.Id, k => k.NextIndex);
        var lastPaid = await db.DdAddresses.AsNoTracking().Where(a => ids.Contains(a.DdKeyId) && a.FirstPaidAt != null)
            .GroupBy(a => a.DdKeyId).Select(g => new { Key = g.Key, Max = g.Max(a => a.Index) })
            .ToDictionaryAsync(x => x.Key, x => x.Max);
        return next.ToDictionary(kv => kv.Key, kv => kv.Value - (lastPaid.TryGetValue(kv.Key, out var max) ? max + 1 : 0));
    }

    internal static object StoreDto(PayStore s, bool ddSurfacesEnabled, int ddUnpaidRun = 0) => new
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
        // specs/035 contract S4: the key itself is never returned, only enough to check it against the wallet.
        DdKey = DdKeyDto(s, ddUnpaidRun),
        s.CreatedAt,
    };

    internal static object? DdKeyDto(PayStore s, int unpaidRun = 0)
    {
        if (s.DdAccountKey is null) return null;
        var parsed = DigiByte.Crypto.KeyGeneration.DigiDollarAccountKey.Parse(s.DdAccountKey, s.Network == "mainnet" ? "mainnet" : "testnet");
        if (!parsed.Ok) return null;
        return new
        {
            Set = true,
            Network = parsed.Key!.Network,
            Fingerprint = parsed.Key.Fingerprint,
            FirstAddress = parsed.Key.Address(0),
            SetAt = s.DdAccountKeySetAt,
            UnpaidRun = unpaidRun,
            UnpaidRunWarning = unpaidRun > DdUnpaidRunWarningAt,
        };
    }

    /// <summary>More DD addresses than this in a row with none paid and the wallet's normal scan may miss later DD (research R11).</summary>
    internal const int DdUnpaidRunWarningAt = 500;

    internal static Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db) =>
        MerchantAuthenticator.AuthenticateAsync(http, db);
}

public record UpdateMeRequest(string? DisplayName);
