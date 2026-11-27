using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// What happens to a store's DD when the store is deleted (specs/034 FR-015, FR-039, edge case "Store deleted while
/// holding DD"). Deletion waits while 1.00 DD or more is held or a payout is in flight; a remainder below 1.00 DD
/// (which the network can't send) and the store's whole DD history move to the merchant-level owner.
/// </summary>
public static class StoreDdRetirement
{
    /// <summary>Null when the store can be deleted now; otherwise the response to return.</summary>
    public static async Task<IResult?> CheckAsync(DigiPayDbContext db, DdLedgerService ledger, PayStore store, bool finalPayout, CancellationToken ct = default)
    {
        var balance = await ledger.GetBalanceAsync(store.MerchantId, store.Id, ct);
        var inFlight = await db.DdPayouts.AnyAsync(p => p.StoreId == store.Id && Payout.InFlightStatuses.Contains(p.Status), ct);
        var held = balance.ConfirmedCents + balance.ConfirmingCents;
        if (held < Payout.MinPayoutCents && !inFlight) return null;

        if (finalPayout && !inFlight && balance.ConfirmedCents >= Payout.MinPayoutCents && store.DdPayoutAddress is not null)
        {
            // Queue the final payout; deleting again once it has gone out completes the deletion.
            store.DdPayoutRequestedAt = DateTime.UtcNow;
            store.DdPayoutRequestReason = "manual";
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { status = "payout_queued", confirmedCents = balance.ConfirmedCents });
        }

        return Results.Conflict(new
        {
            error = "dd_held",
            confirmedCents = balance.ConfirmedCents,
            confirmingCents = balance.ConfirmingCents,
            payoutInProgress = inFlight,
            hasPayoutAddress = store.DdPayoutAddress is not null,
        });
    }

    /// <summary>
    /// Re-owns the store's DD addresses, ledger, payouts and pending changes to the merchant-level owner, creating its
    /// settings row (with the store's payout address and schedule) when needed. Not saved.
    /// </summary>
    public static async Task MoveToMerchantAsync(DigiPayDbContext db, PayStore store, CancellationToken ct = default)
    {
        var addresses = await db.DdAddresses.Where(a => a.StoreId == store.Id).ToListAsync(ct);
        var entries = await db.DdLedgerEntries.Where(e => e.StoreId == store.Id).ToListAsync(ct);
        if (addresses.Count == 0 && entries.Count == 0) return;

        foreach (var a in addresses) a.StoreId = null;
        foreach (var e in entries) e.StoreId = null;
        foreach (var p in await db.DdPayouts.Where(p => p.StoreId == store.Id).ToListAsync(ct)) p.StoreId = null;
        foreach (var c in await db.PayoutAddressChanges.Where(c => c.StoreId == store.Id && c.Status == PayoutAddressChange.StatusPending).ToListAsync(ct))
        {
            c.Status = PayoutAddressChange.StatusCancelled;
            c.CancelledAt = DateTime.UtcNow;
        }

        var settings = await db.MerchantDdSettings.FirstOrDefaultAsync(m => m.MerchantId == store.MerchantId, ct);
        if (settings is null)
        {
            db.MerchantDdSettings.Add(new MerchantDdSettings
            {
                MerchantId = store.MerchantId,
                Network = store.Network,
                DdPayoutAddress = store.DdPayoutAddress,
                DdPayoutAddressSetAt = store.DdPayoutAddress is null ? null : DateTime.UtcNow,
            });
        }
    }
}
