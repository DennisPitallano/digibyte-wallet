using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Decides which owners are due a payout and creates it as <c>waiting</c>, with items covering the oldest confirmed
/// credits first (specs/034 FR-011 to FR-015, research R8 step 5). Sending is <see cref="DdPayoutSender"/>'s job.
/// </summary>
public sealed class DdPayoutPlanner
{
    private readonly DigiPayDbContext _db;
    private readonly DdLedgerService _ledger;

    public DdPayoutPlanner(DigiPayDbContext db, DdLedgerService ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task<int> PlanAsync(bool globallyPaused, CancellationToken ct = default)
    {
        if (globallyPaused) return 0;
        var owners = new List<IDdPayoutOwner>();
        owners.AddRange(await _db.Stores.Where(s => s.DdPayoutAddress != null).ToListAsync(ct));
        owners.AddRange(await _db.MerchantDdSettings.Where(m => m.DdPayoutAddress != null).ToListAsync(ct));

        var created = 0;
        foreach (var owner in owners)
        {
            if (await PlanOwnerAsync(owner, DateTime.UtcNow, ct)) created++;
        }
        return created;
    }

    private async Task<bool> PlanOwnerAsync(IDdPayoutOwner owner, DateTime now, CancellationToken ct)
    {
        var request = owner.DdPayoutRequestedAt is not null ? owner.DdPayoutRequestReason ?? "manual" : null;

        if (owner.DdPayoutAddress is null || owner.DdPayoutsPausedByOperatorAt is not null) return false;
        // An automatically paused owner gets one attempt when the merchant asks for a retry (FR-022, tasks T032).
        if (owner.DdPayoutsAutoPausedAt is not null && request != "retry") return false;
        if (await _db.DdPayouts.AnyAsync(p => p.MerchantId == owner.MerchantId && p.StoreId == owner.OwnerStoreId && Payout.InFlightStatuses.Contains(p.Status), ct))
            return false;
        if (await _db.PayoutAddressChanges.AnyAsync(c => c.MerchantId == owner.MerchantId && c.StoreId == owner.OwnerStoreId && c.Status == PayoutAddressChange.StatusPending, ct))
            return false;

        var balance = await _ledger.GetBalanceAsync(owner.MerchantId, owner.OwnerStoreId, ct);
        var payable = balance.PayableCents;
        if (payable < Payout.MinPayoutCents)
        {
            ClearRequest(owner);
            await _db.SaveChangesAsync(ct);
            return false;
        }

        string? trigger = request;
        if (trigger is null)
        {
            switch (owner.DdPayoutSchedule)
            {
                case "daily":
                    if (PayoutSchedule.DailyDue(owner, now))
                    {
                        trigger = "daily";
                        owner.DdPayoutLastDailyRunAt = now;
                    }
                    break;
                case "threshold":
                    var oldest = balance.Uncovered.Min(u => u.Credit.ConfirmedAt);
                    if (owner.DdPayoutThresholdCents is { } threshold && payable >= threshold) trigger = "threshold";
                    else if (oldest is not null && now - oldest.Value >= Payout.ThresholdMaxAge) trigger = "threshold_age";
                    break;
                default:
                    trigger = "auto";
                    break;
            }
        }
        if (trigger is null) return false;

        var payout = new DdPayout
        {
            Id = Payout.NewId("dpo_"),
            MerchantId = owner.MerchantId,
            StoreId = owner.OwnerStoreId,
            Destination = owner.DdPayoutAddress,
            AmountCents = payable,
            Trigger = trigger,
        };
        _db.DdPayouts.Add(payout);
        var left = payable;
        foreach (var u in balance.Uncovered)
        {
            if (left <= 0) break;
            var take = Math.Min(left, u.AmountCents);
            _db.DdPayoutItems.Add(new DdPayoutItem { PayoutId = payout.Id, LedgerEntryId = u.Credit.Id, AmountCents = take });
            left -= take;
        }
        ClearRequest(owner);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static void ClearRequest(IDdPayoutOwner owner)
    {
        owner.DdPayoutRequestedAt = null;
        owner.DdPayoutRequestReason = null;
    }
}
