using System.Security.Cryptography;
using System.Text;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using DigiByte.Pay.Api.Services.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// DigiPay operator controls for DD payouts (specs/034 contracts/operator-api.md, FR-021). Authenticated by
/// <c>X-DigiPay-Operator-Key</c> only, compared in constant time; 503 when no key is configured (fail closed).
/// Merchant tokens are never accepted.
/// </summary>
public static class OperatorEndpoints
{
    public const string HeaderName = "X-DigiPay-Operator-Key";

    public static RouteGroupBuilder MapOperatorEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var options = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<PayoutOptions>>().Value;
            if (string.IsNullOrEmpty(options.OperatorKey))
                return Results.Json(new { error = "operator_key_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            var provided = ctx.HttpContext.Request.Headers[HeaderName].ToString();
            var ok = provided.Length > 0 && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(options.OperatorKey)));
            return ok ? await next(ctx) : Results.Unauthorized();
        });

        group.MapPost("/payouts/pause", async (OperatorPauseBody body, DigiPayDbContext db, AuditLogger audit) =>
        {
            var now = DateTime.UtcNow;
            if (body.StoreId is null)
            {
                await Payout.SetSettingAsync(db, OperatorSetting.PayoutsPausedAt, Payout.Iso(now));
                await Payout.SetSettingAsync(db, OperatorSetting.PayoutsPausedReason, Trim(body.Reason));
                await db.SaveChangesAsync();
                foreach (var merchantId in await db.Stores.Where(s => s.DdPayoutAddress != null).Select(s => s.MerchantId).Distinct().ToListAsync())
                    await audit.LogAsync(merchantId, "payouts.paused", "System", "payouts", summary: "DigiPay paused all DD payouts",
                        metadata: new { reason = Trim(body.Reason), scope = "global" });
                return Results.Ok(new { pausedAt = now });
            }
            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == body.StoreId);
            if (store is null) return Results.NotFound(new { error = "store not found" });
            store.DdPayoutsPausedByOperatorAt = now;
            await db.SaveChangesAsync();
            await audit.LogAsync(store.MerchantId, "payouts.paused", "Store", store.Id, summary: "DigiPay paused DD payouts for this store",
                metadata: new { reason = Trim(body.Reason), scope = "store" });
            return Results.Ok(new { pausedAt = now });
        });

        group.MapPost("/payouts/resume", async (OperatorResumeBody body, DigiPayDbContext db, AuditLogger audit) =>
        {
            if (body.StoreId is null)
            {
                await Payout.SetSettingAsync(db, OperatorSetting.PayoutsPausedAt, null);
                await Payout.SetSettingAsync(db, OperatorSetting.PayoutsPausedReason, null);
                await db.SaveChangesAsync();
                foreach (var merchantId in await db.Stores.Where(s => s.DdPayoutAddress != null).Select(s => s.MerchantId).Distinct().ToListAsync())
                    await audit.LogAsync(merchantId, "payouts.resumed", "System", "payouts", summary: "DigiPay resumed DD payouts",
                        metadata: new { scope = "global" });
                return Results.Ok(new { resumed = true });
            }
            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == body.StoreId);
            if (store is null) return Results.NotFound(new { error = "store not found" });
            store.DdPayoutsPausedByOperatorAt = null;
            await db.SaveChangesAsync();
            await audit.LogAsync(store.MerchantId, "payouts.resumed", "Store", store.Id, summary: "DigiPay resumed DD payouts for this store",
                metadata: new { scope = "store" });
            return Results.Ok(new { resumed = true });
        });

        group.MapGet("/payouts/health", async (DigiPayDbContext db, DigiDollarNodeClient node, DdLedgerService ledger,
            PayoutAlerts alerts, IOptions<PayoutOptions> options) =>
        {
            var health = await node.GetHealthAsync();
            long? available = null;
            if (health.Reachable)
            {
                try { available = await node.GetWalletBalanceAsync(0) - await ledger.GetIncomingUnconfirmedAsync(); }
                catch { /* shown as null */ }
            }
            var liability = await ledger.GetLiabilityAsync();
            return Results.Ok(new
            {
                paused = await Payout.GloballyPausedAsync(db, options.Value),
                node = new { reachable = health.Reachable, ddEnabled = health.DdEnabled, feeReserveDgb = health.FeeReserveDgb, reserveHealthy = health.ReserveHealthy },
                walletAvailableCents = available,
                liabilityCents = liability,
                coverageOk = available is not null && available >= liability,
                checkingCount = await db.DdPayouts.CountAsync(p => p.Status == DdPayout.StatusChecking),
                waitingCount = await db.DdPayouts.CountAsync(p => p.Status == DdPayout.StatusWaiting),
                lastScanAt = Payout.ParseIso(await Payout.GetSettingAsync(db, OperatorSetting.LedgerLastScanAt)),
                openAlerts = alerts.Raised.Select(a => a.Alert).Distinct(),
            });
        });

        return group;
    }

    private static string? Trim(string? reason) => reason is null ? null : reason.Length > 256 ? reason[..256] : reason;
}

public record OperatorPauseBody(string? StoreId, string? Reason);
public record OperatorResumeBody(string? StoreId);
