using System.Text.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// specs/033 US3 (T038, T039): the monitor locks a buyer-choice payment to the currency its first funds arrive in, and
/// records funds in the other currency as an extra payment (FR-024, FR-025, contract C3).
/// </summary>
public class InvoiceMonitorBuyerChoiceTests
{
    /// <summary>A buyer-choice payment with both options, and the receiver collecting webhook bodies.</summary>
    private static async Task<(PayFactory Factory, string Id, string DgbAddress, string DdAddress, List<string> Hooks)> TwoOptions()
    {
        var factory = Factory();
        var hooks = new List<string>();
        factory.Receiver.Respond = req =>
        {
            lock (hooks) hooks.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        };
        var (client, key, storeId) = await DdStore(factory);
        await factory.SetStoreWebhook(storeId, "https://hooks.example/digipay");
        var session = await CreateChoice(client, key, storeId);
        var id = session.GetProperty("id").GetString()!;
        var dd = await Json(await PickDd(client, id));
        return (factory, id, Address(Option(session, "DGB")), Address(dd), hooks);
    }

    private static List<JsonElement> Events(List<string> hooks, string name)
    {
        lock (hooks)
            return hooks.Select(h => JsonDocument.Parse(h).RootElement.Clone())
                .Where(e => e.GetProperty("event").GetString() == name).ToList();
    }

    [Fact]
    public async Task DD_funds_lock_the_payment_to_DD()
    {
        var (factory, id, _, dd, hooks) = await TwoOptions();
        using var _f = factory;
        factory.DdExplorer.Pay(dd, 2500, confirmations: 6);

        await Tick(factory);

        var s = await Load(factory, id);
        Assert.Equal("DD", s.Currency);
        Assert.Equal(dd, s.Address);
        Assert.Equal(2500, s.AmountCentsDd);
        Assert.NotNull(s.LockedAt);
        Assert.Equal(PaySessionStatus.Confirmed, s.Status);
        var confirmed = Assert.Single(Events(hooks, "session.confirmed"));
        Assert.Equal("DD", confirmed.GetProperty("session").GetProperty("currency").GetString()); // the currency actually paid
        Assert.True(confirmed.GetProperty("session").GetProperty("buyerChoice").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, confirmed.GetProperty("session").GetProperty("lockedAt").ValueKind);
    }

    [Fact]
    public async Task DGB_funds_lock_the_payment_to_DGB()
    {
        var (factory, id, dgb, _, _) = await TwoOptions();
        using var _f = factory;
        factory.Chain.Pay(dgb, 2500L * 100_000_000);

        await Tick(factory);

        var s = await Load(factory, id);
        Assert.Equal("DGB", s.Currency);
        Assert.Equal(dgb, s.Address);
        Assert.NotNull(s.LockedAt);
        Assert.Equal(PaySessionStatus.Seen, s.Status);
    }

    [Fact]
    public async Task DGB_wins_when_both_arrive_in_the_same_pass_and_the_DD_becomes_an_extra_payment()
    {
        var (factory, id, dgb, dd, hooks) = await TwoOptions();
        using var _f = factory;
        factory.Chain.Pay(dgb, 2500L * 100_000_000);
        factory.DdExplorer.Pay(dd, 2500, confirmations: 6);

        await Tick(factory);
        await Tick(factory);

        var s = await Load(factory, id);
        Assert.Equal("DGB", s.Currency);
        Assert.Equal("DD", s.ExtraCurrency);
        Assert.Equal(2500, s.ExtraAmount);
    }

    [Fact]
    public async Task A_down_DD_explorer_never_stops_DGB_from_locking()
    {
        var (factory, id, dgb, _, _) = await TwoOptions();
        using var _f = factory;
        factory.DdExplorer.Down = true;
        factory.Chain.Pay(dgb, 2500L * 100_000_000, confirmations: 6);

        await Tick(factory);

        var s = await Load(factory, id);
        Assert.Equal("DGB", s.Currency);
        Assert.Equal(PaySessionStatus.Confirmed, s.Status);
    }

    [Fact]
    public async Task Funds_in_the_other_currency_are_an_extra_payment_sent_once_and_kept_up_to_date()
    {
        var (factory, id, dgb, dd, hooks) = await TwoOptions();
        using var _f = factory;
        factory.Chain.Pay(dgb, 2500L * 100_000_000, confirmations: 6);
        await Tick(factory);

        var extraTxid = factory.DdExplorer.Pay(dd, 500, confirmations: 6);
        await Tick(factory);
        factory.DdExplorer.Pay(dd, 300, confirmations: 6);
        await Tick(factory);

        var s = await Load(factory, id);
        Assert.Equal("DD", s.ExtraCurrency);
        Assert.Equal(800, s.ExtraAmount);
        Assert.Equal(extraTxid, s.ExtraTxid);
        Assert.NotNull(s.ExtraSeenAt);
        var extra = Assert.Single(Events(hooks, "session.extra_payment"));
        var payload = extra.GetProperty("session").GetProperty("extra");
        Assert.Equal("DD", payload.GetProperty("currency").GetString());
        Assert.Equal(500, payload.GetProperty("amount").GetInt64());
        await factory.WithDb(async db =>
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Action == "session.extra_payment" && a.TargetId == id)));
    }

    [Fact]
    public async Task Extra_payments_are_still_caught_after_expiry_inside_the_late_window()
    {
        var (factory, id, dgb, dd, _) = await TwoOptions();
        using var _f = factory;
        factory.Chain.Pay(dgb, 2500L * 100_000_000, confirmations: 6);
        await Tick(factory);
        await factory.WithDb(async db =>
        {
            var s = await db.Sessions.FirstAsync(x => x.Id == id);
            s.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        });

        factory.DdExplorer.Pay(dd, 300, confirmations: 6);
        await Tick(factory);

        Assert.Equal(300, (await Load(factory, id)).ExtraAmount);
    }

    [Fact]
    public async Task An_unlocked_payment_with_no_funds_still_expires()
    {
        var (factory, id, _, _, hooks) = await TwoOptions();
        using var _f = factory;
        await factory.WithDb(async db =>
        {
            var s = await db.Sessions.FirstAsync(x => x.Id == id);
            s.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        await Tick(factory);

        var s = await Load(factory, id);
        Assert.Equal(PaySessionStatus.Expired, s.Status);
        Assert.Null(s.LockedAt);
        Assert.Single(Events(hooks, "session.expired"));
    }
}
