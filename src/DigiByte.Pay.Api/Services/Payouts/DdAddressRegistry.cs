using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Records which store and merchant own each DD address DigiPay creates (specs/034 research R4/R5). Every caller of
/// <c>DigiDollarNodeClient.GenerateAddressAsync</c> should register the address; anything missed is found again from
/// sessions, buyer-choice options and donation addresses when DD arrives.
/// </summary>
public static class DdAddressRegistry
{
    /// <summary>Adds a <see cref="DdAddress"/> row to the context (not saved) unless one already exists.</summary>
    public static async Task RegisterAsync(DigiPayDbContext db, string address, string merchantId, string? storeId,
        string kind, string? sessionId, string network, CancellationToken ct = default)
    {
        if (db.DdAddresses.Local.Any(a => a.Address == address)) return;
        if (await db.DdAddresses.AnyAsync(a => a.Address == address, ct)) return;
        db.DdAddresses.Add(new DdAddress
        {
            Address = address,
            MerchantId = merchantId,
            StoreId = storeId,
            Kind = kind,
            SessionId = sessionId,
            Network = network,
        });
    }

    /// <summary>
    /// The owner of a DD address, registering it on the way when only a session, option or donation address knows it.
    /// Null when DigiPay has no record of the address.
    /// </summary>
    public static async Task<DdAddress?> ResolveAsync(DigiPayDbContext db, string address, CancellationToken ct = default)
    {
        var known = db.DdAddresses.Local.FirstOrDefault(a => a.Address == address)
            ?? await db.DdAddresses.FirstOrDefaultAsync(a => a.Address == address, ct);
        if (known is not null) return known;

        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Address == address && s.Currency == "DD", ct);
        if (session is null)
        {
            var option = await db.SessionOptions.AsNoTracking().FirstOrDefaultAsync(o => o.Address == address && o.Currency == "DD", ct);
            if (option is not null)
                session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == option.SessionId, ct);
        }
        if (session is not null)
        {
            var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.StoreId, ct);
            await RegisterAsync(db, address, session.MerchantId, store?.Id, DdAddress.KindPayment, session.Id, store?.Network ?? "mainnet", ct);
            return db.DdAddresses.Local.First(a => a.Address == address);
        }

        var donationStore = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.DdDonationAddress == address, ct);
        if (donationStore is not null)
        {
            await RegisterAsync(db, address, donationStore.MerchantId, donationStore.Id, DdAddress.KindDonation, null, donationStore.Network, ct);
            return db.DdAddresses.Local.First(a => a.Address == address);
        }
        return null;
    }

    /// <summary>True when <paramref name="address"/> is any DD address DigiPay created (never a valid payout address).</summary>
    public static async Task<bool> IsDigiPayAddressAsync(DigiPayDbContext db, string address, CancellationToken ct = default) =>
        await db.DdAddresses.AnyAsync(a => a.Address == address, ct)
        || await db.Sessions.AnyAsync(s => s.Address == address && s.Currency == "DD", ct)
        || await db.SessionOptions.AnyAsync(o => o.Address == address && o.Currency == "DD", ct)
        || await db.Stores.AnyAsync(s => s.DdDonationAddress == address, ct);

    /// <summary>
    /// One-off backfill for DD held before payouts existed (FR-038): a row for every DD session, DD buyer-choice option
    /// and store donation address that has none. A session whose store is gone becomes merchant-level (FR-039).
    /// </summary>
    public static async Task<int> BackfillAsync(DigiPayDbContext db, CancellationToken ct = default)
    {
        var known = (await db.DdAddresses.Select(a => a.Address).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var added = 0;

        void Add(string address, string merchantId, string storeId, string kind, string? sessionId)
        {
            if (!known.Add(address)) return;
            var store = stores.GetValueOrDefault(storeId);
            db.DdAddresses.Add(new DdAddress
            {
                Address = address,
                MerchantId = merchantId,
                StoreId = store?.Id,
                Kind = kind,
                SessionId = sessionId,
                Network = store?.Network ?? "mainnet",
            });
            added++;
        }

        foreach (var s in await db.Sessions.AsNoTracking().Where(s => s.Currency == "DD").Select(s => new { s.Id, s.Address, s.MerchantId, s.StoreId }).ToListAsync(ct))
            Add(s.Address, s.MerchantId, s.StoreId, DdAddress.KindPayment, s.Id);

        var options = await (from o in db.SessionOptions.AsNoTracking()
                             where o.Currency == "DD"
                             join s in db.Sessions.AsNoTracking() on o.SessionId equals s.Id
                             select new { o.Address, s.Id, s.MerchantId, s.StoreId }).ToListAsync(ct);
        foreach (var o in options) Add(o.Address, o.MerchantId, o.StoreId, DdAddress.KindPayment, o.Id);

        foreach (var store in stores.Values.Where(s => s.DdDonationAddress is not null))
            Add(store.DdDonationAddress!, store.MerchantId, store.Id, DdAddress.KindDonation, null);

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }
}
