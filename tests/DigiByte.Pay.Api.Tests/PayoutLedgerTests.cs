using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services.Payouts;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/034 US2, US4, US5, US6 and operator controls (tasks T017, T027, T028, T042, T055, T056, T061, T065).</summary>
public class PayoutLedgerTests
{
    [Fact]
    public async Task Receives_are_credited_once_and_split_by_source()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        h.Node.ReserveHealthy = false; // hold payouts so the balance stays visible
        var (_, over) = await h.NewDdSession(500);
        h.Node.Receive(over, 650);
        var (lateSession, late) = await h.NewDdSession(300);
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"Sessions\" SET \"ExpiresAt\" = {0} WHERE \"Id\" = {1}", DateTime.UtcNow.AddHours(-1), lateSession));
        h.Node.Receive(late, 300);
        var donation = PayFactory.NewDdAddress();
        await h.Factory.WithDb(async db =>
        {
            var store = await db.Stores.FirstAsync(s => s.Id == h.StoreId);
            store.DdDonationAddress = donation; // as 033 would create it
            await db.SaveChangesAsync();
        });
        h.Node.Receive(donation, 125);

        await h.Cycle();
        await h.Cycle(); // a second scan must not credit anything twice

        var balance = await h.Balance();
        Assert.Equal(650 + 300 + 125, balance.GetProperty("confirmedCents").GetInt64());
        var by = balance.GetProperty("bySource");
        Assert.Equal(500, by.GetProperty("payment").GetInt64());
        Assert.Equal(150, by.GetProperty("overpayment").GetInt64());
        Assert.Equal(300, by.GetProperty("late").GetInt64());
        Assert.Equal(125, by.GetProperty("donation").GetInt64());
        await h.Factory.WithDb(async db => Assert.True(await db.DdAddresses.AnyAsync(a => a.Address == donation && a.Kind == DdAddress.KindDonation)));
    }

    [Fact]
    public async Task DD_at_an_unknown_address_is_never_credited_and_alerts()
    {
        using var h = await PayoutHarness.CreateAsync();
        h.Node.Receive(PayFactory.NewDdAddress(), 999);

        await h.Cycle();

        await h.Factory.WithDb(async db => Assert.False(await db.DdLedgerEntries.AnyAsync()));
        Assert.Contains(h.Alerts.Raised, a => a.Alert == PayoutAlerts.UnattributedReceive);
    }

    [Fact]
    public async Task Extra_DD_on_a_buyer_choice_session_locked_to_DGB_is_labelled_for_refund()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var (sessionId, address) = await h.NewDdSession(500);
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Sessions\" SET \"BuyerChoice\" = 1, \"LockedAt\" = {0}, \"Currency\" = 'DGB' WHERE \"Id\" = {1}", DateTime.UtcNow, sessionId));
        h.Node.Receive(address, 500);

        await h.Cycle();

        var payout = Assert.Single(await h.Payouts());
        var detail = await PayoutHarness.Json(await h.AsKey(HttpMethod.Get, $"/v1/pay/payouts/{payout.Id}"));
        Assert.Equal("extra_refund", detail.GetProperty("items")[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_reversal_after_payout_is_absorbed_by_DigiPay()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var (_, _, txid) = await h.Paid(800);
        await h.Cycle();
        Assert.Single(h.Node.Sends);

        h.Node.TxStatuses[txid] = ("mempool", 0); // a reorg undid the payment
        await h.Cycle();

        Assert.Equal(0, (await h.Balance()).GetProperty("confirmedCents").GetInt64()); // never negative
        Assert.Contains(h.Alerts.Raised, a => a.Alert == PayoutAlerts.ReversalAbsorbed);
        await h.Factory.WithDb(async db => Assert.True(await db.DdLedgerEntries.AnyAsync(e => e.Type == DdLedgerEntry.TypeReversalAbsorbed && e.AmountCents == 800)));
        Assert.Single(h.Node.Sends); // nothing clawed back or resent
    }

    [Fact]
    public async Task A_threshold_holds_until_reached_or_seven_days_old_and_pay_out_now_overrides()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var settings = await h.AsSession(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}/payout-settings", new { schedule = "threshold", thresholdCents = 5000 });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Single(await h.Audit("payout_settings.update"));

        await h.Paid(2000);
        await h.Paid(2500);
        await h.Cycle();
        Assert.Empty(h.Node.Sends);
        Assert.Equal("threshold", (await h.Balance()).GetProperty("holdReason").GetString());

        await h.Paid(1000);
        await h.Cycle();
        Assert.Equal(5500, Assert.Single(h.Node.Sends).AmountCents);

        await h.Paid(300);
        await h.Cycle();
        Assert.Single(h.Node.Sends);
        Assert.Equal(HttpStatusCode.Accepted, (await h.AsSession(HttpMethod.Post, $"/v1/pay/stores/{h.StoreId}/payouts", new { reason = "manual" })).StatusCode);
        await h.Cycle();
        Assert.Equal(300, h.Node.Sends[1].AmountCents);

        await h.Paid(400);
        await h.Cycle();
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"DdLedgerEntries\" SET \"ConfirmedAt\" = {0} WHERE \"AmountCents\" = 400", DateTime.UtcNow.AddDays(-8)));
        await h.Cycle();
        Assert.Equal(400, h.Node.Sends[2].AmountCents);
        Assert.Equal("threshold_age", (await h.Payouts()).Last().Trigger);
    }

    [Fact]
    public async Task Daily_pays_once_after_the_chosen_time()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var past = DateTime.UtcNow.AddMinutes(-5);
        if (past.Date != DateTime.UtcNow.Date) past = DateTime.UtcNow.Date; // just after midnight
        var response = await h.AsSession(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}/payout-settings",
            new { schedule = "daily", dailyTime = past.ToString("HH:mm"), timeZone = "UTC" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"Stores\" SET \"DdPayoutLastDailyRunAt\" = NULL"));

        await h.Paid(300);
        await h.Cycle();
        Assert.Single(h.Node.Sends);

        await h.Paid(300);
        await h.Cycle();
        Assert.Single(h.Node.Sends); // today's run is done
        Assert.Equal("schedule", (await h.Balance()).GetProperty("holdReason").GetString());
    }

    [Theory]
    [InlineData("weekly", null, null, null, "schedule")]
    [InlineData("daily", "25:00", "UTC", null, "dailyTime")]
    [InlineData("daily", "08:00", "Mars/Olympus", null, "timeZone")]
    [InlineData("threshold", null, null, 50L, "thresholdCents")]
    [InlineData("threshold", null, null, 20_000_000L, "thresholdCents")]
    public async Task Bad_payout_settings_are_refused(string schedule, string? dailyTime, string? timeZone, long? threshold, string field)
    {
        using var h = await PayoutHarness.CreateAsync();
        var response = await h.AsSession(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}/payout-settings",
            new { schedule, dailyTime, timeZone, thresholdCents = threshold });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"field\":\"{field}\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Read_routes_work_with_an_api_key_and_hide_other_merchants_stores()
    {
        using var h = await PayoutHarness.CreateAsync();
        using var other = await PayoutHarness.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await h.AsKey(HttpMethod.Get, $"/v1/pay/stores/{h.StoreId}/dd-balance")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.AsKey(HttpMethod.Get, $"/v1/pay/stores/{h.StoreId}/payouts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.AsKey(HttpMethod.Get, $"/v1/pay/stores/{other.StoreId}/dd-balance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.AsKey(HttpMethod.Post, $"/v1/pay/stores/{h.StoreId}/payouts", new { reason = "manual" })).StatusCode);
    }

    [Fact]
    public async Task Pay_out_now_explains_why_it_cannot()
    {
        using var h = await PayoutHarness.CreateAsync();
        var none = await h.AsSession(HttpMethod.Post, $"/v1/pay/stores/{h.StoreId}/payouts", new { reason = "manual" });
        Assert.Equal("no_address", await PayoutHarness.Error(none));
        await h.SetAddress();
        var empty = await h.AsSession(HttpMethod.Post, $"/v1/pay/stores/{h.StoreId}/payouts", new { reason = "manual" });
        Assert.Equal("below_minimum", await PayoutHarness.Error(empty));
    }

    [Fact]
    public async Task A_store_holding_DD_cannot_be_deleted_until_paid_out_and_a_small_remainder_moves_to_the_merchant()
    {
        using var h = await PayoutHarness.CreateAsync();
        var second = await PayoutHarness.Json(await h.AsKey(HttpMethod.Post, "/v1/pay/stores", new { name = "Second", network = "mainnet" }));
        var keep = h.StoreId;
        // Work on the second store.
        var storeId = second.GetProperty("id").GetString()!;
        await h.SetAddress(storeId: storeId);
        await h.Factory.WithDb(async db =>
        {
            var s = await db.Stores.FirstAsync(x => x.Id == storeId);
            db.DdAddresses.Add(new DdAddress { Address = "TDsmall", MerchantId = s.MerchantId, StoreId = storeId, Kind = DdAddress.KindDonation, Network = "mainnet" });
            db.DdAddresses.Add(new DdAddress { Address = "TDbig", MerchantId = s.MerchantId, StoreId = storeId, Kind = DdAddress.KindDonation, Network = "mainnet" });
            await db.SaveChangesAsync();
        });
        h.Node.ReserveHealthy = false;
        h.Node.Receive("TDbig", 700);
        await h.Cycle();

        var blocked = await h.AsKey(HttpMethod.Delete, $"/v1/pay/stores/{storeId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("dd_held", await PayoutHarness.Error(blocked));

        h.Node.ReserveHealthy = true;
        await h.Cycle(); // the waiting payout goes out
        h.Node.Receive("TDsmall", 40);
        await h.Cycle();

        var deleted = await h.AsKey(HttpMethod.Delete, $"/v1/pay/stores/{storeId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var merchantLevel = await PayoutHarness.Json(await h.AsKey(HttpMethod.Get, "/v1/pay/me/dd-balance"));
        Assert.Equal(40, merchantLevel.GetProperty("confirmedCents").GetInt64());
        Assert.NotNull(keep);
    }

    [Fact]
    public async Task Operator_routes_need_the_operator_key_and_pause_payouts()
    {
        using var h = await PayoutHarness.CreateAsync();
        using var noKey = PayFactory.Create();
        var unconfigured = await noKey.CreateClient().PostAsJsonAsync("/v1/pay/operator/payouts/pause", new { reason = "x" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfigured.StatusCode);

        var wrong = new HttpRequestMessage(HttpMethod.Post, "/v1/pay/operator/payouts/pause") { Content = JsonContent.Create(new { reason = "x" }) };
        wrong.Headers.Add("X-DigiPay-Operator-Key", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.SendAsync(wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.AsSession(HttpMethod.Post, "/v1/pay/operator/payouts/pause", new { reason = "x" })).StatusCode);

        await h.SetAddress();
        await h.Paid(500);
        Assert.Equal(HttpStatusCode.OK, (await Operator(h, "/v1/pay/operator/payouts/pause", new { reason = "incident" })).StatusCode);
        await h.Cycle();
        Assert.Empty(h.Node.Sends);
        Assert.Equal("operator_paused", (await h.Balance()).GetProperty("holdReason").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Operator(h, "/v1/pay/operator/payouts/resume", new { })).StatusCode);
        await h.Cycle();
        Assert.Single(h.Node.Sends);

        var health = await PayoutHarness.Json(await Operator(h, "/v1/pay/operator/payouts/health", null, HttpMethod.Get));
        Assert.True(health.GetProperty("coverageOk").GetBoolean());
        Assert.True(health.TryGetProperty("liabilityCents", out _));
    }

    [Fact]
    public async Task Payout_webhooks_are_signed_and_retried_byte_for_byte()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.Factory.SetStoreWebhook(h.StoreId, "https://merchant.example/hook");
        await h.SetAddress();
        h.Factory.Receiver.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await h.Paid(500);
        await h.Cycle();

        var first = (await h.Factory.LoadDeliveries(h.StoreId)).Single(d => d.EventName == "payout.sent");
        Assert.NotNull(first.PayloadJson);
        Assert.NotNull(first.NextRetryAt);
        Assert.Contains("\"payout\":", first.PayloadJson);
        Assert.Contains("\"items\":", first.PayloadJson);

        h.Factory.Receiver.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"WebhookDeliveries\" SET \"NextRetryAt\" = {0} WHERE \"Id\" = {1}", DateTime.UtcNow.AddMinutes(-1), first.Id));
        var retrier = h.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<DigiByte.Pay.Api.Services.WebhookRetrier>().Single();
        var sweep = typeof(DigiByte.Pay.Api.Services.WebhookRetrier).GetMethod("SweepAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)sweep.Invoke(retrier, [CancellationToken.None])!;

        var retry = (await h.Factory.LoadDeliveries(h.StoreId)).Single(d => d.EventName == "payout.sent" && d.Attempt == 2);
        Assert.Equal(first.PayloadJson, retry.PayloadJson);
        Assert.True(retry.Success);
    }

    [Fact]
    public async Task No_failure_webhook_for_a_payout_re_planned_to_a_new_address()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.Factory.SetStoreWebhook(h.StoreId, "https://merchant.example/hook");
        await h.SetAddress();
        await h.Paid(400);
        h.Node.ReserveHealthy = false;
        await h.Cycle();
        await h.SetAddress(PayFactory.NewDdAddress());
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"PayoutAddressChanges\" SET \"EffectiveAt\" = {0} WHERE \"Status\" = 'pending'", DateTime.UtcNow.AddMinutes(-1)));
        h.Node.ReserveHealthy = true;
        await h.Cycle();
        await h.Cycle();

        Assert.DoesNotContain(await h.Factory.LoadDeliveries(h.StoreId), d => d.EventName == "payout.failed");
    }

    [Fact]
    public async Task DD_held_before_payouts_existed_is_found_by_the_backfill()
    {
        using var h = await PayoutHarness.CreateAsync();
        var legacy = PayFactory.NewDdAddress();
        await h.Factory.WithDb(async db =>
        {
            var store = await db.Stores.FirstAsync(s => s.Id == h.StoreId);
            db.Sessions.Add(new PaySession
            {
                Id = "ses_legacy000000001", MerchantId = store.MerchantId, StoreId = store.Id, AddressIndex = 0,
                Address = legacy, AmountSatoshis = 0, Currency = "DD", AmountCentsDd = 1000,
                ExpiresAt = DateTime.UtcNow.AddDays(-30), Status = PaySessionStatus.Confirmed,
            });
            await db.SaveChangesAsync();
            Assert.Equal(1, await DdAddressRegistry.BackfillAsync(db));
            Assert.Equal(0, await DdAddressRegistry.BackfillAsync(db)); // safe to run again
        });
        h.Node.Receive(legacy, 1000, at: DateTime.UtcNow.AddDays(-30).AddMinutes(-5));

        await h.Cycle();
        var balance = await h.Balance();
        Assert.Equal(1000, balance.GetProperty("confirmedCents").GetInt64());
        Assert.Equal("no_address", balance.GetProperty("holdReason").GetString());

        // A first address on a store already holding DD waits like a change (US4 #2).
        await h.SetAddress();
        Assert.Null((await h.Store()).DdPayoutAddress);
    }

    private static Task<HttpResponseMessage> Operator(PayoutHarness h, string url, object? body, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-DigiPay-Operator-Key", PayoutHarness.OperatorKey);
        return h.Client.SendAsync(request);
    }
}
