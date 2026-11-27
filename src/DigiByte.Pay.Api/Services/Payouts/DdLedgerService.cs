using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>A credit not yet covered by a live payout, with the part still to pay.</summary>
public sealed record UncoveredCredit(DdLedgerEntry Credit, long AmountCents);

/// <summary>What DigiPay holds for one owner (specs/034 data-model "Balance views").</summary>
public sealed record OwnerBalance(
    long ConfirmedCents,
    long ConfirmingCents,
    IReadOnlyDictionary<string, long> BySource,
    IReadOnlyList<UncoveredCredit> Uncovered)
{
    /// <summary>What a new payout can take now: confirmed held DD not already in a live payout.</summary>
    public long PayableCents => Math.Min(ConfirmedCents - InLivePayoutsCents, Uncovered.Sum(u => u.AmountCents));

    /// <summary>Confirmed held DD already assigned to a waiting payout (still held, no debit yet).</summary>
    public long InLivePayoutsCents { get; init; }
}

/// <summary>Balances, liability and hold reasons computed from the DD ledger (specs/034 research R4).</summary>
public sealed class DdLedgerService
{
    private readonly DigiPayDbContext _db;

    public DdLedgerService(DigiPayDbContext db) => _db = db;

    /// <summary>Signed effect of an entry on the confirmed held balance.</summary>
    public static long Signed(DdLedgerEntry e) => e.Type switch
    {
        DdLedgerEntry.TypeCredit => e.ConfirmedAt is not null && e.ReversedAt is null ? e.AmountCents : 0,
        DdLedgerEntry.TypeDebitReversal or DdLedgerEntry.TypeTransferIn or DdLedgerEntry.TypeReversalAbsorbed => e.AmountCents,
        DdLedgerEntry.TypeDebit or DdLedgerEntry.TypeTransferOut => -e.AmountCents,
        _ => 0,
    };

    public async Task<OwnerBalance> GetBalanceAsync(string merchantId, string? storeId, CancellationToken ct = default)
    {
        var entries = await _db.DdLedgerEntries.AsNoTracking()
            .Where(e => e.MerchantId == merchantId && e.StoreId == storeId)
            .ToListAsync(ct);
        var confirmed = entries.Sum(Signed);
        var confirming = entries.Where(e => e.Type == DdLedgerEntry.TypeCredit && e.ConfirmedAt is null && e.ReversedAt is null).Sum(e => e.AmountCents);

        var creditIds = entries.Where(e => e.Type == DdLedgerEntry.TypeCredit).Select(e => e.Id).ToList();
        var covering = await (from i in _db.DdPayoutItems.AsNoTracking()
                              join p in _db.DdPayouts.AsNoTracking() on i.PayoutId equals p.Id
                              where creditIds.Contains(i.LedgerEntryId) && Payout.LiveStatuses.Contains(p.Status)
                              select new { i.LedgerEntryId, i.AmountCents, p.Status }).ToListAsync(ct);
        var coveredBy = covering.GroupBy(c => c.LedgerEntryId).ToDictionary(g => g.Key, g => g.Sum(c => c.AmountCents));
        var debitedBy = covering.Where(c => Payout.DebitedStatuses.Contains(c.Status))
            .GroupBy(c => c.LedgerEntryId).ToDictionary(g => g.Key, g => g.Sum(c => c.AmountCents));

        var uncovered = entries
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.ConfirmedAt is not null && e.ReversedAt is null)
            .Select(e => new UncoveredCredit(e, e.AmountCents - coveredBy.GetValueOrDefault(e.Id)))
            .Where(u => u.AmountCents > 0)
            .OrderBy(u => u.Credit.ConfirmedAt).ThenBy(u => u.Credit.CreatedAt)
            .ToList();

        // Still held, by source: confirmed credits not yet taken by a payout that has gone out (sending onwards).
        var bySource = entries
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.ConfirmedAt is not null && e.ReversedAt is null)
            .GroupBy(e => e.Source ?? DdLedgerEntry.SourcePayment)
            .ToDictionary(g => g.Key, g => g.Sum(e => Math.Max(0, e.AmountCents - debitedBy.GetValueOrDefault(e.Id))));
        foreach (var source in new[] { DdLedgerEntry.SourcePayment, DdLedgerEntry.SourceOverpayment, DdLedgerEntry.SourceLate, DdLedgerEntry.SourceExtraRefund, DdLedgerEntry.SourceDonation })
            bySource.TryAdd(source, 0);

        var inLive = await _db.DdPayouts.AsNoTracking()
            .Where(p => p.MerchantId == merchantId && p.StoreId == storeId && p.Status == DdPayout.StatusWaiting)
            .SumAsync(p => (long?)p.AmountCents, ct) ?? 0;

        return new OwnerBalance(confirmed, confirming, bySource, uncovered) { InLivePayoutsCents = inLive };
    }

    /// <summary>
    /// Everything DigiPay owes merchants right now: every owner's confirmed held DD, plus payouts being sent whose DD
    /// is debited but may not have left the wallet (contracts/operator-api.md).
    /// </summary>
    public async Task<long> GetLiabilityAsync(CancellationToken ct = default)
    {
        var entries = await _db.DdLedgerEntries.AsNoTracking().ToListAsync(ct);
        var held = entries.Sum(Signed);
        var inFlight = await _db.DdPayouts.AsNoTracking()
            .Where(p => p.Status == DdPayout.StatusSending || p.Status == DdPayout.StatusChecking)
            .SumAsync(p => (long?)p.AmountCents, ct) ?? 0;
        return held + inFlight;
    }

    /// <summary>Incoming DD still short of the payout confirmations (research R4: not yet DigiPay's to cover).</summary>
    public async Task<long> GetIncomingUnconfirmedAsync(CancellationToken ct = default) =>
        await _db.DdLedgerEntries.AsNoTracking()
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.ConfirmedAt == null && e.ReversedAt == null)
            .SumAsync(e => (long?)e.AmountCents, ct) ?? 0;

    /// <summary>
    /// Why DD is held and not being paid out, as one of the contract's <c>holdReason</c> values, or null when nothing
    /// is held back (contracts/merchant-api.md C2).
    /// </summary>
    public async Task<string?> GetHoldReasonAsync(IDdPayoutOwner owner, OwnerBalance balance, bool globallyPaused, CancellationToken ct = default)
    {
        if (balance.ConfirmedCents <= 0 && balance.ConfirmingCents <= 0) return null;
        if (globallyPaused || owner.DdPayoutsPausedByOperatorAt is not null) return "operator_paused";
        if (owner.DdPayoutsAutoPausedAt is not null) return "auto_paused";
        if (owner.DdPayoutAddress is null) return "no_address";
        if (await _db.PayoutAddressChanges.AnyAsync(c => c.MerchantId == owner.MerchantId && c.StoreId == owner.OwnerStoreId && c.Status == PayoutAddressChange.StatusPending, ct))
            return "change_pending";
        var waiting = await _db.DdPayouts.AsNoTracking()
            .Where(p => p.MerchantId == owner.MerchantId && p.StoreId == owner.OwnerStoreId && p.Status == DdPayout.StatusWaiting && p.WaitReason != null)
            .Select(p => p.WaitReason).FirstOrDefaultAsync(ct);
        if (waiting is "node_unavailable" or "fee_reserve" or "coverage") return waiting;
        if (await Payout.GetSettingAsync(_db, OperatorSetting.NodeUnreachableSince, ct) is not null) return "node_unavailable";
        var free = balance.ConfirmedCents - balance.InLivePayoutsCents;
        if (free < Payout.MinPayoutCents)
            return balance.ConfirmingCents > 0 ? "waiting_confirmations" : free > 0 ? "below_minimum" : null;
        return owner.DdPayoutSchedule switch
        {
            "daily" => "schedule",
            "threshold" => "threshold",
            _ => null,
        };
    }

    /// <summary>When the next payout is expected, where the schedule says (contracts/merchant-api.md C2).</summary>
    public static DateTime? NextPayoutAt(IDdPayoutOwner owner, OwnerBalance balance, DateTime nowUtc)
    {
        switch (owner.DdPayoutSchedule)
        {
            case "daily":
                return PayoutSchedule.NextDailySlotUtc(owner, nowUtc);
            case "threshold":
                var oldest = balance.Uncovered.Select(u => u.Credit.ConfirmedAt).Where(c => c is not null).Min();
                return oldest is null ? null : oldest.Value + Payout.ThresholdMaxAge;
            default:
                return null;
        }
    }
}

/// <summary>Time arithmetic for the "once a day" schedule.</summary>
public static class PayoutSchedule
{
    public static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    public static bool IsValidZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true;
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch { return false; }
    }

    /// <summary>Today's slot in UTC (in the owner's zone), whether or not it has passed.</summary>
    public static DateTime TodaySlotUtc(IDdPayoutOwner owner, DateTime nowUtc)
    {
        var zone = Zone(owner.DdPayoutTimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var time = owner.DdPayoutDailyTime ?? new TimeOnly(0, 0);
        var localSlot = DateTime.SpecifyKind(localNow.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localSlot, zone);
    }

    public static DateTime NextDailySlotUtc(IDdPayoutOwner owner, DateTime nowUtc)
    {
        var today = TodaySlotUtc(owner, nowUtc);
        var ranToday = owner.DdPayoutLastDailyRunAt is { } last && last >= today;
        return nowUtc < today || !ranToday ? (nowUtc < today ? today : nowUtc) : today.AddDays(1);
    }

    /// <summary>True when today's slot has passed and hasn't run yet.</summary>
    public static bool DailyDue(IDdPayoutOwner owner, DateTime nowUtc)
    {
        var today = TodaySlotUtc(owner, nowUtc);
        return nowUtc >= today && (owner.DdPayoutLastDailyRunAt is null || owner.DdPayoutLastDailyRunAt < today);
    }
}
