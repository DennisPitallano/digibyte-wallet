using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/035 US2 (T030): DD payments are read from the public explorer's DigiDollar data (research R5).</summary>
public class InvoiceMonitorDdExplorerTests
{
    private static async Task<(PayFactory F, HttpClient C, string Key, string StoreId, string Id, string Address)> DdPayment(long cents = 500)
    {
        var f = Factory();
        var (c, key, storeId) = await DdStore(f);
        var session = await Json(await Create(c, key, new { storeId, currency = "DD", amountCentsDd = cents }));
        return (f, c, key, storeId, session.GetProperty("id").GetString()!, session.GetProperty("address").GetString()!);
    }

    [Fact]
    public async Task A_payment_goes_from_seen_to_paid_to_confirmed()
    {
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;

        var txid = f.DdExplorer.Pay(address, 500);
        await Tick(f);
        var seen = await Load(f, id);
        Assert.Equal(PaySessionStatus.Seen, seen.Status);
        Assert.Equal(500, seen.ReceivedSatoshis); // cents for DD
        Assert.Equal(txid, seen.PaidTxid);

        f.DdExplorer.Confirm(txid, 1);
        await Tick(f);
        Assert.Equal(PaySessionStatus.Paid, (await Load(f, id)).Status);

        f.DdExplorer.Confirm(txid, 6);
        await Tick(f);
        var done = await Load(f, id);
        Assert.Equal(PaySessionStatus.Confirmed, done.Status);
        Assert.Equal(6, done.Confirmations);
        await f.WithDb(async db => Assert.NotNull((await db.DdAddresses.SingleAsync(a => a.Address == address)).FirstPaidAt));
    }

    [Fact]
    public async Task A_short_payment_is_underpaid_and_a_second_part_makes_it_paid()
    {
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;

        var first = f.DdExplorer.Pay(address, 300, confirmations: 2);
        await Tick(f);
        Assert.Equal(PaySessionStatus.Underpaid, (await Load(f, id)).Status);

        f.DdExplorer.Pay(address, 200, confirmations: 1);
        await Tick(f);
        var s = await Load(f, id);
        Assert.Equal(PaySessionStatus.Paid, s.Status);
        Assert.Equal(500, s.ReceivedSatoshis);
        Assert.Equal(first, s.PaidTxid); // the first transaction paying the address
        Assert.Equal(1, s.Confirmations); // of the transaction that completed the amount due
    }

    [Fact]
    public async Task An_underpaid_payment_past_the_late_window_is_no_longer_watched()
    {
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;
        f.DdExplorer.Pay(address, 300, confirmations: 2);
        await Tick(f);
        await f.WithDb(async db =>
        {
            (await db.Sessions.FirstAsync(s => s.Id == id)).ExpiresAt = DateTime.UtcNow.AddDays(-4);
            await db.SaveChangesAsync();
        });

        f.DdExplorer.Pay(address, 200, confirmations: 1);
        await Tick(f);

        Assert.Equal(PaySessionStatus.Underpaid, (await Load(f, id)).Status);
    }

    [Fact]
    public async Task An_overpaid_payment_is_paid_with_the_full_amount_recorded()
    {
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;

        f.DdExplorer.Pay(address, 750, confirmations: 1);
        await Tick(f);

        var s = await Load(f, id);
        Assert.Equal(PaySessionStatus.Paid, s.Status);
        Assert.Equal(750, s.ReceivedSatoshis);
    }

    [Fact]
    public async Task A_late_payment_within_three_days_of_expiry_is_reconciled()
    {
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;
        await f.WithDb(async db =>
        {
            var s = await db.Sessions.FirstAsync(x => x.Id == id);
            s.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        await Tick(f);
        Assert.Equal(PaySessionStatus.Expired, (await Load(f, id)).Status);

        f.DdExplorer.Pay(address, 500, confirmations: 1);
        await Tick(f);

        Assert.Equal(PaySessionStatus.Paid, (await Load(f, id)).Status);
        await f.WithDb(async db => Assert.True(await db.AuditEvents.AnyAsync(a => a.Action == "session.late_reconciled" && a.TargetId == id)));
    }

    [Fact]
    public async Task Confirmations_are_recalculated_and_a_vanished_transaction_leaves_the_status()
    {
        // Same as DGB: while watched, confirmations follow the explorer; a transaction that disappears changes nothing.
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;
        var txid = f.DdExplorer.Pay(address, 500, confirmations: 2);
        await Tick(f);
        Assert.Equal(PaySessionStatus.Paid, (await Load(f, id)).Status);

        f.DdExplorer.Confirm(txid, 0);
        await Tick(f);
        Assert.Equal(PaySessionStatus.Seen, (await Load(f, id)).Status);

        f.DdExplorer.Confirm(txid, 1);
        await Tick(f);
        f.DdExplorer.Vanish(txid);
        await Tick(f);
        Assert.Equal(PaySessionStatus.Paid, (await Load(f, id)).Status);
    }

    [Fact]
    public async Task An_open_payment_completes_after_the_store_key_is_replaced()
    {
        var (f, c, key, storeId, id, address) = await DdPayment();
        using var _f = f;

        await TestDdKeys.GiveKey(c, key, storeId, TestDdKeys.OtherWords);
        f.DdExplorer.Pay(address, 500, confirmations: 1);
        await Tick(f);

        var s = await Load(f, id);
        Assert.Equal(address, s.Address);
        Assert.Equal(PaySessionStatus.Paid, s.Status);
    }

    [Fact]
    public async Task An_open_payment_completes_after_the_store_key_is_removed()
    {
        var (f, c, key, storeId, id, address) = await DdPayment();
        using var _f = f;

        (await c.SendAsync(PayFactory.Authed(HttpMethod.Delete, $"/v1/pay/stores/{storeId}/dd-key", key))).EnsureSuccessStatusCode();
        f.DdExplorer.Pay(address, 500, confirmations: 6);
        await Tick(f);

        Assert.Equal(PaySessionStatus.Confirmed, (await Load(f, id)).Status);
        Assert.False((await f.LoadStore(storeId))!.DigiDollarEnabled);
    }

    [Fact]
    public async Task While_the_explorer_is_down_the_payment_waits_and_catches_up()
    {
        var (f, _, _, _, id, address) = await DdPayment();
        using var _f = f;
        f.DdExplorer.Pay(address, 500, confirmations: 1);
        f.DdExplorer.Down = true;

        await Tick(f);
        Assert.Equal(PaySessionStatus.Pending, (await Load(f, id)).Status);

        f.DdExplorer.Down = false;
        // The outage answer is cached for 30 seconds; let that time pass.
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(f.Services)
            .Remove(DigiByte.Pay.Api.Services.DdAvailability.CacheKeyFor("mainnet"));
        await Tick(f);
        Assert.Equal(PaySessionStatus.Paid, (await Load(f, id)).Status);
    }
}
