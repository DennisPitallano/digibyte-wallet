using System.Net;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services.Payouts;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/034 US1: sending payouts and never paying twice (tasks T026, T027).</summary>
public class PayoutSendTests
{
    [Fact]
    public async Task A_confirmed_DD_payment_is_paid_out_in_full_and_confirmed()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.Factory.SetStoreWebhook(h.StoreId, "https://merchant.example/hook");
        var destination = await h.SetAddress();
        var (sessionId, _, _) = await h.Paid(500);

        await h.Cycle();

        var payout = Assert.Single(await h.Payouts());
        Assert.Equal(DdPayout.StatusSent, payout.Status);
        var send = Assert.Single(h.Node.Sends);
        Assert.Equal(destination, send.Address);
        Assert.Equal(500, send.AmountCents); // no fee taken from the merchant (FR-017)
        Assert.Equal(payout.Id, send.Comment);
        Assert.Equal(0, (await h.Balance()).GetProperty("confirmedCents").GetInt64());

        h.Node.TxStatuses[send.Txid] = ("confirmed", 6);
        await h.Cycle();
        Assert.Equal(DdPayout.StatusConfirmed, Assert.Single(await h.Payouts()).Status);

        var events = (await h.Factory.LoadDeliveries(h.StoreId)).Select(d => d.EventName).ToList();
        Assert.Contains("payout.sent", events);
        Assert.Contains("payout.confirmed", events);
        var detail = await PayoutHarness.Json(await h.AsKey(HttpMethod.Get, $"/v1/pay/payouts/{payout.Id}"));
        Assert.Equal(sessionId, detail.GetProperty("items")[0].GetProperty("sessionId").GetString());
        var list = await PayoutHarness.Json(await h.AsKey(HttpMethod.Get, "/v1/pay/sessions"));
        var row = list.GetProperty("sessions").EnumerateArray().Single(r => r.GetProperty("id").GetString() == sessionId);
        Assert.Equal(payout.Id, row.GetProperty("ddPayout").GetProperty("payoutId").GetString());
    }

    [Fact]
    public async Task Nothing_is_paid_out_before_six_confirmations()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var (_, _, txid) = await h.Paid(500, confirmations: 2);

        await h.Cycle();
        Assert.Empty(await h.Payouts());
        var balance = await h.Balance();
        Assert.Equal(500, balance.GetProperty("confirmingCents").GetInt64());
        Assert.Equal("waiting_confirmations", balance.GetProperty("holdReason").GetString());

        h.Node.SetConfirmations(txid, 6);
        await h.Cycle();
        Assert.Single(h.Node.Sends);
    }

    [Fact]
    public async Task A_rejected_send_fails_restores_the_balance_and_three_in_a_row_pause_the_store()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(300);
        h.Node.OnSend = (_, _, _) => FakeDdNode.Reply(HttpStatusCode.BadRequest, new { error = "bad-txns-dd-something" });

        for (var i = 0; i < 3; i++) await h.Cycle();

        var payouts = await h.Payouts();
        Assert.Equal(3, payouts.Count);
        Assert.All(payouts, p => Assert.Equal(DdPayout.StatusFailed, p.Status));
        Assert.Equal(300, (await h.Balance()).GetProperty("confirmedCents").GetInt64());
        Assert.NotNull((await h.Store()).DdPayoutsAutoPausedAt);
        Assert.Contains(await h.Audit("payouts.paused"), a => a.Metadata!.Contains("auto_failures"));
        Assert.Contains(h.Alerts.Raised, a => a.Alert == PayoutAlerts.StoreAutoPaused);

        await h.Cycle();
        Assert.Equal(3, (await h.Payouts()).Count); // paused: nothing new
    }

    [Fact]
    public async Task Retry_now_gets_one_attempt_while_paused_and_clears_the_pause_only_when_it_goes_out()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(300);
        h.Node.OnSend = (_, _, _) => FakeDdNode.Reply(HttpStatusCode.BadRequest, new { error = "refused" });
        for (var i = 0; i < 3; i++) await h.Cycle();

        Assert.Equal(HttpStatusCode.Accepted, (await h.AsSession(HttpMethod.Post, $"/v1/pay/stores/{h.StoreId}/payouts", new { reason = "retry" })).StatusCode);
        Assert.NotNull((await h.Store()).DdPayoutsAutoPausedAt); // not cleared by asking
        await h.Cycle();
        Assert.Equal(4, (await h.Payouts()).Count);
        Assert.NotNull((await h.Store()).DdPayoutsAutoPausedAt); // that retry failed too

        h.Node.OnSend = null;
        await h.AsSession(HttpMethod.Post, $"/v1/pay/stores/{h.StoreId}/payouts", new { reason = "retry" });
        await h.Cycle();
        Assert.Single(h.Node.Sends);
        Assert.Null((await h.Store()).DdPayoutsAutoPausedAt);
        Assert.Contains(await h.Audit("payouts.resumed"), a => a.Metadata!.Contains("payout_sent"));
    }

    [Fact]
    public async Task An_unclear_send_is_checked_and_never_sent_twice()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(250);
        var calls = 0;
        h.Node.OnSend = (to, cents, comment) =>
        {
            calls++;
            // The node took the send but the answer was lost.
            h.Node.Sends.Add(new FakeSend(new string('c', 64), to, cents, comment, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0));
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        };

        await h.Cycle();
        Assert.Equal(1, calls);
        // Reconciled on the next cycle from /sends by its comment.
        await h.Cycle();
        await h.Cycle();
        Assert.Equal(1, calls);
        var payout = Assert.Single(await h.Payouts());
        Assert.Equal(DdPayout.StatusSent, payout.Status);
        Assert.Equal(new string('c', 64), payout.Txid);
        Assert.Single(await h.Audit("payout.checking"));
    }

    [Fact]
    public async Task An_unclear_send_that_never_reached_the_network_fails_after_ten_minutes()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(250);
        h.Node.OnSend = (_, _, _) => new HttpResponseMessage(HttpStatusCode.GatewayTimeout);

        await h.Cycle();
        await h.Cycle();
        Assert.Equal(DdPayout.StatusChecking, Assert.Single(await h.Payouts()).Status);
        Assert.Equal(1, h.Node.SendCalls);

        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"DdPayouts\" SET \"LastAttemptAt\" = {0}", DateTime.UtcNow.AddMinutes(-11)));
        h.Node.OnSend = null;
        await h.Cycle(); // gives up on the first, plans and sends a new one

        var payouts = await h.Payouts();
        Assert.Equal(DdPayout.FailureNotFound, payouts[0].FailureCode);
        Assert.Equal(DdPayout.StatusSent, payouts[1].Status);
        Assert.Equal(250, Assert.Single(h.Node.Sends).AmountCents);
    }

    [Fact]
    public async Task Insufficient_funds_waits_instead_of_failing()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(250);
        h.Node.OnSend = (_, _, _) => FakeDdNode.Reply(HttpStatusCode.BadRequest, new { error = "Insufficient confirmed DigiDollar funds" });

        for (var i = 0; i < 4; i++) await h.Cycle();

        var payout = Assert.Single(await h.Payouts());
        Assert.Equal(DdPayout.StatusWaiting, payout.Status);
        Assert.Equal("coverage", payout.WaitReason);
        Assert.Null((await h.Store()).DdPayoutsAutoPausedAt);
        Assert.Equal(250, (await h.Balance()).GetProperty("confirmedCents").GetInt64());
    }

    [Fact]
    public async Task Payouts_wait_when_the_wallet_does_not_cover_what_DigiPay_owes()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(900);
        h.Node.WalletBalanceCents = 100;

        await h.Cycle();

        Assert.Equal(0, h.Node.SendCalls);
        Assert.Equal("coverage", Assert.Single(await h.Payouts()).WaitReason);
        Assert.Contains(h.Alerts.Raised, a => a.Alert == PayoutAlerts.CoverageShort);
    }

    [Fact]
    public async Task Unconfirmed_change_from_a_payout_does_not_block_the_next_one()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(300);
        await h.Cycle();
        Assert.Single(h.Node.Sends);

        // A second payment arrives while the first payout's change is still unconfirmed.
        await h.Paid(400);
        await h.Cycle();

        Assert.Equal(2, h.Node.Sends.Count);
        Assert.DoesNotContain(h.Alerts.Raised, a => a.Alert == PayoutAlerts.CoverageShort);
    }

    [Fact]
    public async Task A_down_node_means_waiting_never_failed()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(500);
        await h.Factory.WithDb(async db =>
        {
            // Credit known already (seen before the outage).
            await Task.CompletedTask;
        });
        await h.Cycle(); // sent
        await h.Paid(600);
        h.Node.Healthy = false;

        // While the node is down, a payout address can still be changed (FR-035).
        var change = await h.PutAddress(PayFactory.NewDdAddress());
        Assert.Equal(HttpStatusCode.Accepted, change.StatusCode);
        await h.AsSession(HttpMethod.Delete, $"/v1/pay/stores/{h.StoreId}/payout-address/pending");

        for (var i = 0; i < 3; i++) await h.Cycle();
        Assert.DoesNotContain(await h.Payouts(), p => p.Status == DdPayout.StatusFailed);

        h.Node.Healthy = true;
        await h.Cycle();
        Assert.Equal(2, h.Node.Sends.Count);
    }

    [Fact]
    public async Task A_low_fee_reserve_holds_payouts_and_alerts()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        await h.Paid(500);
        h.Node.ReserveHealthy = false;

        await h.Cycle();

        Assert.Equal("fee_reserve", Assert.Single(await h.Payouts()).WaitReason);
        Assert.Equal(0, h.Node.SendCalls);
        Assert.Contains(h.Alerts.Raised, a => a.Alert == PayoutAlerts.FeeReserveLow);
        Assert.Equal("fee_reserve", (await h.Balance()).GetProperty("holdReason").GetString());
    }

    [Fact]
    public async Task A_destination_that_has_become_invalid_is_never_sent_to()
    {
        using var h = await PayoutHarness.CreateAsync();
        var destination = await h.SetAddress();
        await h.Paid(500);
        h.Node.ReserveHealthy = false;
        await h.Cycle(); // waiting
        await h.Factory.WithDb(async db =>
        {
            db.DdAddresses.Add(new DdAddress { Address = destination, MerchantId = "mer_other", Kind = DdAddress.KindPayment, Network = "mainnet" });
            await db.SaveChangesAsync();
        });
        h.Node.ReserveHealthy = true;

        await h.Cycle();

        Assert.Equal(0, h.Node.SendCalls);
        Assert.Equal(DdPayout.FailureAddressInvalid, Assert.Single(await h.Payouts()).FailureCode);
        Assert.NotNull((await h.Store()).DdPayoutsAutoPausedAt);
    }

    [Fact]
    public async Task Payouts_under_one_DD_wait_for_more()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var (_, address) = await h.NewDdSession(100);
        h.Node.Receive(address, 60);

        await h.Cycle();

        Assert.Empty(await h.Payouts());
        Assert.Equal("below_minimum", (await h.Balance()).GetProperty("holdReason").GetString());
    }
}
