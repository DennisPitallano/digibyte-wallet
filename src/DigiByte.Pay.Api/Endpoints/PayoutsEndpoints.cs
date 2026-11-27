using System.Text.Json;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using DigiByte.Pay.Api.Services.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// DD payout endpoints for merchants (specs/034 contracts/merchant-api.md C1, C2). Payout-address routes accept a
/// Dashboard session only (<c>dps_</c>) plus, to set or change an address, a fresh Digi-ID step-up grant (FR-004, FR-008).
/// Read routes also accept a merchant API key (FR-033).
/// </summary>
public static class PayoutsEndpoints
{
    public static RouteGroupBuilder MapPayoutsEndpoints(this RouteGroupBuilder group)
    {
        // ---- C1: step-up and payout address (session only) ----

        group.MapGet("/payouts/step-up/challenge", async (HttpRequest http, DigiPayDbContext db, StepUpService stepUp) =>
        {
            var (caller, sessionId, err) = await SessionCallerAsync(http, db);
            if (err is not null) return err;
            var entry = stepUp.IssueChallenge(StepUpGrant.PurposePayoutAddress, caller!.Merchant.Id, sessionId!);
            return Results.Ok(new { nonce = entry.Nonce, uri = entry.Uri, expiresAt = entry.ExpiresAt });
        });

        group.MapPut("/stores/{storeId}/payout-address", (string storeId, PayoutAddressBody body, HttpRequest http, DigiPayDbContext db,
            StepUpService stepUp, PayoutAddressService addresses) => SetAddressAsync(storeId, body, http, db, stepUp, addresses));
        group.MapPut("/me/dd-balance/payout-address", (PayoutAddressBody body, HttpRequest http, DigiPayDbContext db,
            StepUpService stepUp, PayoutAddressService addresses) => SetAddressAsync(null, body, http, db, stepUp, addresses));

        group.MapDelete("/stores/{storeId}/payout-address/pending", (string storeId, HttpRequest http, DigiPayDbContext db, PayoutAddressService addresses) =>
            CancelPendingAsync(storeId, http, db, addresses));
        group.MapDelete("/me/dd-balance/payout-address/pending", (HttpRequest http, DigiPayDbContext db, PayoutAddressService addresses) =>
            CancelPendingAsync(null, http, db, addresses));

        group.MapPost("/stores/{storeId}/payout-address/acknowledge", async (string storeId, AcknowledgeBody body, HttpRequest http, DigiPayDbContext db) =>
        {
            var (caller, _, err) = await SessionCallerAsync(http, db);
            if (err is not null) return err;
            var change = await db.PayoutAddressChanges.FirstOrDefaultAsync(c => c.Id == body.ChangeId && c.MerchantId == caller!.Merchant.Id);
            if (change is null) return Results.NotFound(new { error = "change not found" });
            change.AcknowledgedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/stores/{storeId}/payout-address/confirm", async (string storeId, HttpRequest http, DigiPayDbContext db, AuditLogger audit) =>
        {
            var (caller, _, err) = await SessionCallerAsync(http, db);
            if (err is not null) return err;
            var (owner, ownerErr) = await LoadOwnerAsync(db, caller!.Merchant.Id, storeId);
            if (ownerErr is not null) return ownerErr;
            if (owner!.DdPayoutsAutoPausedAt is not null)
            {
                owner.DdPayoutsAutoPausedAt = null;
                owner.DdPayoutConsecutiveFailures = 0;
                await db.SaveChangesAsync();
                await audit.LogAsync(owner.MerchantId, "payouts.resumed", "Store", storeId,
                    summary: "The merchant confirmed the DD payout address; automatic payouts resumed", metadata: new { reason = "address_confirmed" });
            }
            return Results.Ok(new { paused = false });
        });

        // ---- C2: settings, balance and payouts ----

        group.MapPatch("/stores/{storeId}/payout-settings", async (string storeId, PayoutSettingsBody body, HttpRequest http, DigiPayDbContext db, AuditLogger audit) =>
        {
            var (caller, _, err) = await SessionCallerAsync(http, db);
            if (err is not null) return err;
            var (owner, ownerErr) = await LoadOwnerAsync(db, caller!.Merchant.Id, storeId);
            if (ownerErr is not null) return ownerErr;
            if (ValidateSettings(body) is { } invalid) return invalid;

            owner!.DdPayoutSchedule = body.Schedule!;
            if (body.Schedule == "daily")
            {
                owner.DdPayoutDailyTime = TimeOnly.ParseExact(body.DailyTime!, "HH:mm");
                owner.DdPayoutTimeZone = string.IsNullOrWhiteSpace(body.TimeZone) ? "UTC" : body.TimeZone;
                owner.DdPayoutLastDailyRunAt = DateTime.UtcNow < PayoutSchedule.TodaySlotUtc(owner, DateTime.UtcNow) ? null : DateTime.UtcNow;
            }
            if (body.Schedule == "threshold") owner.DdPayoutThresholdCents = body.ThresholdCents;
            await db.SaveChangesAsync();
            await audit.LogAsync(owner.MerchantId, "payout_settings.update", "Store", storeId,
                summary: $"Changed when DD payouts go out ({body.Schedule})",
                metadata: new { schedule = body.Schedule, dailyTime = body.DailyTime, timeZone = owner.DdPayoutTimeZone, thresholdCents = body.ThresholdCents });
            return Results.Ok(new { schedule = owner.DdPayoutSchedule, dailyTime = owner.DdPayoutDailyTime?.ToString("HH:mm"), timeZone = owner.DdPayoutTimeZone, thresholdCents = owner.DdPayoutThresholdCents });
        });

        group.MapGet("/stores/{storeId}/dd-balance", (string storeId, HttpRequest http, DigiPayDbContext db, DdLedgerService ledger, IOptions<PayoutOptions> options) =>
            BalanceAsync(storeId, http, db, ledger, options.Value));
        group.MapGet("/me/dd-balance", (HttpRequest http, DigiPayDbContext db, DdLedgerService ledger, IOptions<PayoutOptions> options) =>
            BalanceAsync(null, http, db, ledger, options.Value));

        group.MapPost("/stores/{storeId}/payouts", async (string storeId, PayoutRequestBody body, HttpRequest http, DigiPayDbContext db,
            DdLedgerService ledger, AuditLogger audit, IOptions<PayoutOptions> options) =>
        {
            var (caller, _, err) = await SessionCallerAsync(http, db);
            if (err is not null) return err;
            var (owner, ownerErr) = await LoadOwnerAsync(db, caller!.Merchant.Id, storeId);
            if (ownerErr is not null) return ownerErr;
            var reason = body.Reason is "retry" ? "retry" : "manual";

            if (await Payout.GloballyPausedAsync(db, options.Value) || owner!.DdPayoutsPausedByOperatorAt is not null)
                return Conflict("operator_paused");
            if (owner.DdPayoutAddress is null) return Conflict("no_address");
            if (await db.PayoutAddressChanges.AnyAsync(c => c.StoreId == storeId && c.Status == PayoutAddressChange.StatusPending))
                return Conflict("change_pending");
            if (await db.DdPayouts.AnyAsync(p => p.StoreId == storeId && (p.Status == DdPayout.StatusSending || p.Status == DdPayout.StatusChecking)))
                return Conflict("payout_in_progress");
            var balance = await ledger.GetBalanceAsync(owner.MerchantId, storeId);
            if (balance.PayableCents < Payout.MinPayoutCents && !await db.DdPayouts.AnyAsync(p => p.StoreId == storeId && p.Status == DdPayout.StatusWaiting))
                return Conflict("below_minimum");

            owner.DdPayoutRequestedAt = DateTime.UtcNow;
            owner.DdPayoutRequestReason = reason;
            await db.SaveChangesAsync();
            await audit.LogAsync(owner.MerchantId, $"payout.{reason}", "Store", storeId,
                summary: reason == "retry" ? "Asked to retry the DD payout" : "Asked to pay out held DD now");
            return Results.Accepted(value: new { requestedAt = owner.DdPayoutRequestedAt });
        });

        group.MapGet("/stores/{storeId}/payouts", (string storeId, string? status, string? cursor, HttpRequest http, DigiPayDbContext db) =>
            ListAsync(storeId, status, cursor, http, db));
        group.MapGet("/me/dd-balance/payouts", (string? status, string? cursor, HttpRequest http, DigiPayDbContext db) =>
            ListAsync(null, status, cursor, http, db));

        group.MapGet("/payouts/{payoutId}", async (string payoutId, HttpRequest http, DigiPayDbContext db) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            var payout = await db.DdPayouts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payoutId && p.MerchantId == caller.Merchant.Id);
            if (payout is null) return Results.NotFound(new { error = "payout not found" });
            return Results.Ok(await PayoutPayloads.BuildAsync(db, payout));
        });

        group.MapGet("/me/payout-alerts", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            var changes = await db.PayoutAddressChanges.AsNoTracking()
                .Where(c => c.MerchantId == caller.Merchant.Id && c.AcknowledgedAt == null)
                .OrderByDescending(c => c.RequestedAt).Take(20).ToListAsync();
            var storeIds = changes.Where(c => c.StoreId != null).Select(c => c.StoreId!).Distinct().ToList();
            var names = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
            return Results.Ok(new
            {
                items = changes.Select(c => new
                {
                    changeId = c.Id,
                    storeId = c.StoreId,
                    storeName = c.StoreId is null ? null : names.GetValueOrDefault(c.StoreId),
                    @event = c.Status switch
                    {
                        PayoutAddressChange.StatusApplied => "changed",
                        PayoutAddressChange.StatusCancelled => "change_cancelled",
                        _ => "change_requested",
                    },
                    oldAddress = c.OldAddress,
                    newAddress = c.NewAddress,
                    requestedAt = c.RequestedAt,
                    effectiveAt = c.EffectiveAt,
                    status = c.Status,
                }),
            });
        });

        return group;
    }

    // ==================== Handlers ====================

    private static async Task<IResult> SetAddressAsync(string? storeId, PayoutAddressBody body, HttpRequest http, DigiPayDbContext db,
        StepUpService stepUp, PayoutAddressService addresses)
    {
        var (caller, sessionId, err) = await SessionCallerAsync(http, db);
        if (err is not null) return err;
        var (owner, ownerErr) = await LoadOwnerAsync(db, caller!.Merchant.Id, storeId);
        if (ownerErr is not null) return ownerErr;

        if (await addresses.ValidateAsync(body.Address, owner!.Network) is { } reason)
            return Results.BadRequest(new { error = "payout_address_invalid", reason });
        if (await addresses.GetPendingAsync(owner) is not null)
            return Conflict("payout_change_pending");
        if (!await stepUp.TryConsumeAsync(body.StepUpGrant, StepUpGrant.PurposePayoutAddress, caller.Merchant.Id, sessionId!))
            return Results.Json(new { error = "stepup_required" }, statusCode: StatusCodes.Status403Forbidden);

        var change = await addresses.RequestAsync(owner, body.Address!, sessionId!);
        return Results.Accepted(value: new { change = PayoutAddressService.ToPayload(change) });
    }

    private static async Task<IResult> CancelPendingAsync(string? storeId, HttpRequest http, DigiPayDbContext db, PayoutAddressService addresses)
    {
        var (caller, sessionId, err) = await SessionCallerAsync(http, db);
        if (err is not null) return err;
        var (owner, ownerErr) = await LoadOwnerAsync(db, caller!.Merchant.Id, storeId);
        if (ownerErr is not null) return ownerErr;
        var pending = await addresses.GetPendingAsync(owner!);
        if (pending is null) return Results.NotFound(new { error = "no_pending_change" });
        await addresses.CancelAsync(owner!, pending, sessionId!);
        return Results.Ok(new { change = PayoutAddressService.ToPayload(pending) });
    }

    private static async Task<IResult> BalanceAsync(string? storeId, HttpRequest http, DigiPayDbContext db, DdLedgerService ledger, PayoutOptions options)
    {
        var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
        if (caller is null) return Results.Unauthorized();
        IDdPayoutOwner? owner = storeId is null
            ? await db.MerchantDdSettings.AsNoTracking().FirstOrDefaultAsync(m => m.MerchantId == caller.Merchant.Id)
            : await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId && s.MerchantId == caller.Merchant.Id);
        if (owner is null && storeId is not null) return Results.NotFound(new { error = "store not found" });
        owner ??= new MerchantDdSettings { MerchantId = caller.Merchant.Id, Network = "mainnet" };

        var balance = await ledger.GetBalanceAsync(caller.Merchant.Id, storeId);
        var paused = await Payout.GloballyPausedAsync(db, options);
        var pending = await db.PayoutAddressChanges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.MerchantId == caller.Merchant.Id && c.StoreId == storeId && c.Status == PayoutAddressChange.StatusPending);
        var lastScan = Payout.ParseIso(await Payout.GetSettingAsync(db, OperatorSetting.LedgerLastScanAt));
        return Results.Ok(new
        {
            confirmedCents = balance.ConfirmedCents,
            confirmingCents = balance.ConfirmingCents,
            bySource = new
            {
                payment = balance.BySource.GetValueOrDefault(DdLedgerEntry.SourcePayment),
                overpayment = balance.BySource.GetValueOrDefault(DdLedgerEntry.SourceOverpayment),
                late = balance.BySource.GetValueOrDefault(DdLedgerEntry.SourceLate),
                extraRefund = balance.BySource.GetValueOrDefault(DdLedgerEntry.SourceExtraRefund),
                donation = balance.BySource.GetValueOrDefault(DdLedgerEntry.SourceDonation),
            },
            lastCheckedAt = lastScan,
            stale = lastScan is null || DateTime.UtcNow - lastScan.Value > TimeSpan.FromMinutes(5),
            payoutAddress = owner.DdPayoutAddress,
            pendingChange = pending is null ? null : new { id = pending.Id, newAddress = pending.NewAddress, effectiveAt = pending.EffectiveAt },
            schedule = new
            {
                mode = owner.DdPayoutSchedule,
                dailyTime = owner.DdPayoutDailyTime?.ToString("HH:mm"),
                timeZone = owner.DdPayoutTimeZone,
                thresholdCents = owner.DdPayoutThresholdCents,
            },
            autoPaused = owner.DdPayoutsAutoPausedAt is not null,
            holdReason = await ledger.GetHoldReasonAsync(owner, balance, paused),
            nextPayoutAt = DdLedgerService.NextPayoutAt(owner, balance, DateTime.UtcNow),
        });
    }

    private static async Task<IResult> ListAsync(string? storeId, string? status, string? cursor, HttpRequest http, DigiPayDbContext db)
    {
        var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
        if (caller is null) return Results.Unauthorized();
        if (storeId is not null && !await db.Stores.AnyAsync(s => s.Id == storeId && s.MerchantId == caller.Merchant.Id))
            return Results.NotFound(new { error = "store not found" });

        // Payouts re-planned to a new address were never sent; they're left out (tasks T043).
        var q = db.DdPayouts.AsNoTracking()
            .Where(p => p.MerchantId == caller.Merchant.Id && p.StoreId == storeId
                        && (p.FailureCode == null || p.FailureCode != DdPayout.FailureSuperseded));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
        if (Payout.ParseIso(cursor) is { } before) q = q.Where(p => p.CreatedAt < before);
        var page = await q.OrderByDescending(p => p.CreatedAt).Take(51).ToListAsync();
        var items = new List<object>();
        foreach (var p in page.Take(50)) items.Add(await PayoutPayloads.BuildAsync(db, p));
        return Results.Ok(new { items, nextCursor = page.Count > 50 ? Payout.Iso(page[49].CreatedAt) : null });
    }

    // ==================== Helpers ====================

    /// <summary>The caller when it's a Dashboard session, plus that session's id; API keys get 403 (FR-008).</summary>
    internal static async Task<(ApiCaller? Caller, string? SessionId, IResult? Error)> SessionCallerAsync(HttpRequest http, DigiPayDbContext db)
    {
        var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
        if (caller is null) return (null, null, Results.Unauthorized());
        if (caller.KeyKind is not null)
            return (null, null, Results.Json(new { error = "session_required" }, statusCode: StatusCodes.Status403Forbidden));
        var token = http.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var prefix = token[..token.LastIndexOf('_')];
        var sessionId = await db.MerchantSessions.Where(s => s.TokenPrefix == prefix).Select(s => s.Id).FirstOrDefaultAsync();
        return sessionId is null ? (null, null, Results.Unauthorized()) : (caller, sessionId, null);
    }

    private static async Task<(IDdPayoutOwner? Owner, IResult? Error)> LoadOwnerAsync(DigiPayDbContext db, string merchantId, string? storeId)
    {
        if (storeId is not null)
        {
            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.MerchantId == merchantId);
            return store is null ? (null, Results.NotFound(new { error = "store not found" })) : (store, null);
        }
        var settings = await db.MerchantDdSettings.FirstOrDefaultAsync(m => m.MerchantId == merchantId);
        return settings is null ? (null, Results.NotFound(new { error = "no_merchant_dd_balance" })) : (settings, null);
    }

    private static IResult? ValidateSettings(PayoutSettingsBody body)
    {
        IResult Bad(string field) => Results.BadRequest(new { error = "invalid_schedule", field });
        if (body.Schedule is null || !Payout.Schedules.Contains(body.Schedule)) return Bad("schedule");
        if (body.Schedule == "daily")
        {
            if (body.DailyTime is null || !TimeOnly.TryParseExact(body.DailyTime, "HH:mm", out _)) return Bad("dailyTime");
            if (!PayoutSchedule.IsValidZone(body.TimeZone)) return Bad("timeZone");
        }
        if (body.Schedule == "threshold" && body.ThresholdCents is not (>= Payout.MinPayoutCents and <= Payout.MaxThresholdCents))
            return Bad("thresholdCents");
        return null;
    }

    private static IResult Conflict(string error) => Results.Conflict(new { error });
}

public record PayoutAddressBody(string? Address, string? StepUpGrant);
public record AcknowledgeBody(string ChangeId);
public record PayoutSettingsBody(string? Schedule, string? DailyTime, string? TimeZone, long? ThresholdCents);
public record PayoutRequestBody(string? Reason);
