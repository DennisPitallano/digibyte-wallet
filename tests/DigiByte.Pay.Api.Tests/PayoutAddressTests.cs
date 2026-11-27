using System.Net;
using System.Net.Http.Json;
using DigiByte.Crypto.DigiId;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/034 US1 and US3: setting and changing the DD payout address (tasks T024, T025, T049).</summary>
public class PayoutAddressTests
{
    [Fact]
    public async Task Invalid_addresses_are_refused_with_the_reason()
    {
        using var h = await PayoutHarness.CreateAsync();
        // DD can't be turned on yet (no address), so make a DigiPay address the direct way.
        var own = PayFactory.NewDdAddress();
        await h.Factory.WithDb(async db =>
        {
            db.DdAddresses.Add(new DdAddress { Address = own, MerchantId = "mer_x", Kind = DdAddress.KindPayment, Network = "mainnet" });
            await db.SaveChangesAsync();
        });
        var good = PayFactory.NewDdAddress();
        var badChecksum = good[..^1] + (good[^1] == 'a' ? 'b' : 'a');

        var cases = new (string Address, string Reason)[]
        {
            ("not an address!", "format"),
            (badChecksum, "checksum"),
            (PayFactory.NewDdAddress("testnet"), "wrong_network"),
            (PayFactory.NewAddress(), "dgb_address"),
            (own, "digipay_address"),
        };
        foreach (var (address, reason) in cases)
        {
            var response = await h.PutAddress(address);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("payout_address_invalid", body);
            Assert.Contains($"\"reason\":\"{reason}\"", body);
        }
        Assert.Null((await h.Store()).DdPayoutAddress);
    }

    [Fact]
    public async Task An_api_key_can_never_set_the_address()
    {
        using var h = await PayoutHarness.CreateAsync();
        var response = await h.AsKey(HttpMethod.Put, $"/v1/pay/stores/{h.StoreId}/payout-address", new { address = PayFactory.NewDdAddress(), stepUpGrant = "sug_x" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("session_required", await PayoutHarness.Error(response));

        var cancel = await h.AsKey(HttpMethod.Delete, $"/v1/pay/stores/{h.StoreId}/payout-address/pending");
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
    }

    [Fact]
    public async Task A_step_up_grant_is_required_and_works_once_for_its_own_session()
    {
        using var h = await PayoutHarness.CreateAsync();
        var address = PayFactory.NewDdAddress();
        var url = $"/v1/pay/stores/{h.StoreId}/payout-address";

        var none = await h.AsSession(HttpMethod.Put, url, new { address });
        Assert.Equal("stepup_required", await PayoutHarness.Error(none));

        var (_, otherSession) = await h.Factory.CreateSessionToken(h.ApiKey);
        var othersGrant = await h.Factory.GrantStepUp(otherSession);
        var foreign = await h.AsSession(HttpMethod.Put, url, new { address, stepUpGrant = othersGrant });
        Assert.Equal("stepup_required", await PayoutHarness.Error(foreign));

        var grant = await h.Factory.GrantStepUp(h.SessionId);
        await h.Factory.WithDb(async db =>
        {
            // An expired grant is refused too.
            var expired = new StepUpGrant { Id = "sug_expired000001", MerchantId = (await db.Stores.FirstAsync(s => s.Id == h.StoreId)).MerchantId,
                SessionId = h.SessionId, Purpose = StepUpGrant.PurposePayoutAddress, DigiIdAddress = "x", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
            db.StepUpGrants.Add(expired);
            await db.SaveChangesAsync();
        });
        var stale = await h.AsSession(HttpMethod.Put, url, new { address, stepUpGrant = "sug_expired000001" });
        Assert.Equal("stepup_required", await PayoutHarness.Error(stale));

        Assert.Equal(HttpStatusCode.Accepted, (await h.AsSession(HttpMethod.Put, url, new { address, stepUpGrant = grant })).StatusCode);
        var reused = await h.AsSession(HttpMethod.Put, url, new { address = PayFactory.NewDdAddress(), stepUpGrant = grant });
        Assert.Equal("stepup_required", await PayoutHarness.Error(reused));
    }

    [Fact]
    public async Task A_first_address_on_a_store_holding_nothing_applies_at_once_and_is_announced()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.Factory.SetStoreWebhook(h.StoreId, "https://merchant.example/hook");

        var address = await h.SetAddress();

        Assert.Equal(address, (await h.Store()).DdPayoutAddress);
        Assert.Single(await h.Audit("payout_address.requested"));
        Assert.Single(await h.Audit("payout_address.applied"));
        var events = (await h.Factory.LoadDeliveries(h.StoreId)).OrderBy(d => d.CreatedAt).Select(d => d.EventName).ToList();
        Assert.Equal(["payout_address.change_requested", "payout_address.changed"], events);
        var alerts = await PayoutHarness.Json(await h.AsSession(HttpMethod.Get, "/v1/pay/me/payout-alerts"));
        Assert.Equal("changed", alerts.GetProperty("items")[0].GetProperty("event").GetString());
    }

    [Fact]
    public async Task A_change_waits_48_hours_with_payouts_held_and_can_be_cancelled_from_another_session()
    {
        using var h = await PayoutHarness.CreateAsync();
        var first = await h.SetAddress();
        var second = PayFactory.NewDdAddress();
        var response = await h.PutAddress(second);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        PayoutAddressChange? pending = null;
        await h.Factory.WithDb(async db => pending = await db.PayoutAddressChanges.SingleAsync(c => c.Status == PayoutAddressChange.StatusPending));
        Assert.Equal(48, Math.Round((pending!.EffectiveAt - pending.RequestedAt).TotalHours));
        Assert.Equal(first, (await h.Store()).DdPayoutAddress);
        Assert.Equal("payout_change_pending", await PayoutHarness.Error(await h.PutAddress(PayFactory.NewDdAddress())));

        // Payouts are held while it's pending.
        await h.Paid(500);
        await h.Cycle();
        Assert.Empty(await h.Payouts());
        Assert.Equal("change_pending", (await h.Balance()).GetProperty("holdReason").GetString());

        // Any signed-in session can cancel it, with no step-up.
        var (otherToken, _) = await h.Factory.CreateSessionToken(h.ApiKey);
        var cancel = await h.Client.SendAsync(PayFactory.Authed(HttpMethod.Delete, $"/v1/pay/stores/{h.StoreId}/payout-address/pending", otherToken));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Single(await h.Audit("payout_address.cancelled"));

        await h.Cycle();
        var payout = Assert.Single(await h.Payouts());
        Assert.Equal(first, payout.Destination);
    }

    [Fact]
    public async Task A_change_applies_after_the_wait_and_the_next_payout_goes_to_the_new_address()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var second = PayFactory.NewDdAddress();
        await h.SetAddress(second);
        await h.Factory.WithDb(async db =>
        {
            // EffectiveAt is init-only: replace the row with one whose wait is over.
            var c = await db.PayoutAddressChanges.SingleAsync(x => x.Status == PayoutAddressChange.StatusPending);
            await db.Database.ExecuteSqlRawAsync("UPDATE \"PayoutAddressChanges\" SET \"EffectiveAt\" = {0} WHERE \"Id\" = {1}", DateTime.UtcNow.AddMinutes(-1), c.Id);
        });

        await h.Paid(700);
        await h.Cycle();

        Assert.Equal(second, (await h.Store()).DdPayoutAddress);
        Assert.Single(await h.Audit("payout_address.applied").ContinueWith(t => t.Result.Where(a => a.Metadata!.Contains(second)).ToList()));
        var send = Assert.Single(h.Node.Sends);
        Assert.Equal(second, send.Address);
    }

    [Fact]
    public async Task A_payout_waiting_when_a_change_applies_goes_to_the_new_address_never_the_old()
    {
        using var h = await PayoutHarness.CreateAsync();
        var first = await h.SetAddress();
        await h.Paid(400);
        h.Node.ReserveHealthy = false;
        await h.Cycle(); // planned, waiting on the fee reserve
        Assert.Equal("fee_reserve", Assert.Single(await h.Payouts()).WaitReason);

        var second = PayFactory.NewDdAddress();
        await h.SetAddress(second);
        await h.Factory.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE \"PayoutAddressChanges\" SET \"EffectiveAt\" = {0} WHERE \"Status\" = 'pending'", DateTime.UtcNow.AddMinutes(-1)));
        h.Node.ReserveHealthy = true;
        await h.Cycle(); // applies the change; the old waiting payout is superseded
        await h.Cycle(); // re-planned to the new address and sent

        Assert.DoesNotContain(h.Node.Sends, s => s.Address == first);
        Assert.Equal(second, Assert.Single(h.Node.Sends).Address);
        var list = await PayoutHarness.Json(await h.AsKey(HttpMethod.Get, $"/v1/pay/stores/{h.StoreId}/payouts"));
        Assert.Equal(1, list.GetProperty("items").GetArrayLength()); // the superseded one is hidden
    }

    [Fact]
    public async Task DigiDollar_can_only_be_turned_on_with_a_payout_address()
    {
        using var h = await PayoutHarness.CreateAsync();
        var refused = await h.AsKey(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}", new { digiDollarEnabled = true });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("payout_address_required", await PayoutHarness.Error(refused));

        await h.SetAddress();
        Assert.True((await h.AsKey(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}", new { digiDollarEnabled = true })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Stores_that_already_had_DigiDollar_on_are_exempt()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.Factory.WithDb(async db =>
        {
            var s = await db.Stores.FirstAsync(x => x.Id == h.StoreId);
            s.DdPayoutAddressRequiredExempt = true;
            await db.SaveChangesAsync();
        });
        Assert.True((await h.AsKey(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}", new { digiDollarEnabled = true })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task The_network_cannot_change_under_a_payout_address()
    {
        using var h = await PayoutHarness.CreateAsync();
        await h.SetAddress();
        var response = await h.AsKey(HttpMethod.Patch, $"/v1/pay/stores/{h.StoreId}", new { network = "testnet" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("payout_address_network_mismatch", await PayoutHarness.Error(response));
    }

    [Fact]
    public async Task A_step_up_signed_by_another_digi_id_address_is_refused_and_audited()
    {
        using var h = await PayoutHarness.CreateAsync();
        var challenge = await PayoutHarness.Json(await h.AsSession(HttpMethod.Get, "/v1/pay/payouts/step-up/challenge"));
        var uri = challenge.GetProperty("uri").GetString()!;
        var stranger = new Key();
        var signed = DigiIdService.Sign(new DigiIdRequest { OriginalUri = uri, CallbackUrl = "", Domain = "", Nonce = "" }, stranger);

        var response = await h.Client.PostAsJsonAsync("/v1/pay/auth/callback", new { address = signed.Address, uri, signature = signed.Signature });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("stepup_address_mismatch", await PayoutHarness.Error(response));
        Assert.Contains(await h.Audit("payout.stepup_failed"), a => a.Metadata!.Contains("address_mismatch"));
        await h.Factory.WithDb(async db => Assert.False(await db.Merchants.AnyAsync(m => m.DigiIdAddress == signed.Address)));
    }

    [Fact]
    public async Task A_step_up_signed_by_the_merchant_mints_a_grant_not_a_session()
    {
        using var h = await PayoutHarness.CreateAsync();
        var key = new Key();
        await h.Factory.WithDb(async db =>
        {
            var session = await db.MerchantSessions.FirstAsync(s => s.Id == h.SessionId);
            var merchant = await db.Merchants.FirstAsync(m => m.Id == session.MerchantId);
            merchant.DigiIdAddress = DigiIdService.GetAddress(key);
            await db.SaveChangesAsync();
        });
        var challenge = await PayoutHarness.Json(await h.AsSession(HttpMethod.Get, "/v1/pay/payouts/step-up/challenge"));
        var uri = challenge.GetProperty("uri").GetString()!;
        var signed = DigiIdService.Sign(new DigiIdRequest { OriginalUri = uri, CallbackUrl = "", Domain = "", Nonce = "" }, key);
        (await h.Client.PostAsJsonAsync("/v1/pay/auth/callback", new { address = signed.Address, uri, signature = signed.Signature })).EnsureSuccessStatusCode();

        var poll = await PayoutHarness.Json(await h.Client.GetAsync($"/v1/pay/auth/poll/{challenge.GetProperty("nonce").GetString()}"));
        Assert.Equal("signed", poll.GetProperty("status").GetString());
        Assert.False(poll.TryGetProperty("token", out _));
        var grant = poll.GetProperty("stepUpGrant").GetString()!;

        var put = await h.AsSession(HttpMethod.Put, $"/v1/pay/stores/{h.StoreId}/payout-address", new { address = PayFactory.NewDdAddress(), stepUpGrant = grant });
        Assert.Equal(HttpStatusCode.Accepted, put.StatusCode);
    }

    [Fact]
    public async Task The_step_up_challenge_needs_a_dashboard_session()
    {
        using var h = await PayoutHarness.CreateAsync();
        var response = await h.AsKey(HttpMethod.Get, "/v1/pay/payouts/step-up/challenge");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
