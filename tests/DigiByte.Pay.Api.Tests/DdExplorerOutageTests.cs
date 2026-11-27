using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/035 US4 (T043): while the public explorer can't be reached, DD waits and DGB is unaffected.</summary>
public class DdExplorerOutageTests
{
    private static void ForgetAvailability(PayFactory f) =>
        f.Services.GetRequiredService<IMemoryCache>().Remove(DdAvailability.CacheKeyFor("mainnet"));

    [Fact]
    public async Task Checkout_shows_DD_unavailable_and_the_switch_is_refused()
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f);
        var id = (await CreateChoice(c, key, storeId)).GetProperty("id").GetString()!;
        f.DdExplorer.Down = true;

        var session = await Json(await c.GetAsync($"/v1/pay/sessions/{id}"));
        Assert.False(session.GetProperty("ddAvailable").GetBoolean());
        Assert.Equal("dd_service_unavailable", session.GetProperty("ddReason").GetString());
        AssertConflict(await PickDd(c, id), "dd_option_unavailable", "dd_service_unavailable");
    }

    [Fact]
    public async Task An_explicit_DD_payment_is_still_created()
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f);
        f.DdExplorer.Down = true;

        var response = await Create(c, key, new { storeId, currency = "DD", amountCentsDd = 500 });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(TestDdKeys.Address(0), (await Json(response)).GetProperty("address").GetString());
    }

    [Fact]
    public async Task DGB_keeps_working_and_DD_paid_during_the_outage_catches_up_even_past_expiry()
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f);
        var dd = await Json(await Create(c, key, new { storeId, currency = "DD", amountCentsDd = 500 }));
        var dgb = await Json(await Create(c, key, new { storeId, amount = 2m }));
        var ddId = dd.GetProperty("id").GetString()!;
        var dgbId = dgb.GetProperty("id").GetString()!;
        f.DdExplorer.Down = true;

        // During the outage: DGB is paid, DD is paid too but can't be seen, and the DD payment's time runs out.
        f.Chain.Pay(dgb.GetProperty("address").GetString()!, 200_000_000, confirmations: 1);
        f.DdExplorer.Pay(dd.GetProperty("address").GetString()!, 500, confirmations: 1);
        await f.WithDb(async db =>
        {
            (await db.Sessions.FirstAsync(s => s.Id == ddId)).ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });
        await Tick(f);

        Assert.NotEqual(PaySessionStatus.Pending, (await Load(f, dgbId)).Status);
        var waiting = await Load(f, ddId);
        Assert.Equal(PaySessionStatus.Pending, waiting.Status); // not expired, not failed: waiting for the explorer

        // The explorer is back.
        f.DdExplorer.Down = false;
        ForgetAvailability(f);
        await Tick(f);

        Assert.Equal(PaySessionStatus.Paid, (await Load(f, ddId)).Status);
    }
}
