using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/033 US4 (T060): the store's DD donation address and the tip page's public donation options.</summary>
public class DonationOptionsTests
{
    private static Task<HttpResponseMessage> DdDonationAddress(HttpClient client, string key, string storeId) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}/donation-address?currency=DD", key));

    [Fact]
    public async Task The_DD_donation_address_is_made_once_and_registered_as_a_donation()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);

        var first = await Json(await DdDonationAddress(client, key, storeId));
        var again = await Json(await DdDonationAddress(client, key, storeId));

        Assert.Equal(TestDdKeys.Address(0), first.GetProperty("address").GetString());
        Assert.Equal("dd", first.GetProperty("mode").GetString());
        Assert.Equal(TestDdKeys.Address(0), again.GetProperty("address").GetString());
        Assert.Equal(1, await DdAddressCount(factory));
        await factory.WithDb(async db =>
        {
            var row = await db.DdAddresses.SingleAsync(a => a.Address == TestDdKeys.Address(0));
            Assert.Equal(DdAddress.KindDonation, row.Kind);
            Assert.Equal(storeId, row.StoreId);
            Assert.Null(row.SessionId);
        });
    }

    [Theory]
    [InlineData(false, true)]  // DigiPay doesn't offer DD beyond the API yet
    [InlineData(true, false)]  // the store hasn't turned DigiDollar on
    public async Task The_DD_donation_address_needs_DD_offered_for_the_store(bool surfaces, bool ddOn)
    {
        using var factory = Factory(surfaces);
        var (client, key, storeId) = await DdStore(factory, ddOn);

        var response = await DdDonationAddress(client, key, storeId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("digidollar_not_enabled_for_store", (await Json(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_DD_donation_address_is_made_even_while_the_explorer_is_down()
    {
        // specs/035 FR-010: the address comes from the store's key, with no outside call.
        using var factory = Factory();
        factory.DdExplorer.Down = true;
        var (client, key, storeId) = await DdStore(factory);

        var response = await DdDonationAddress(client, key, storeId);

        Assert.Equal(TestDdKeys.Address(0), (await Json(response)).GetProperty("address").GetString());
    }

    [Fact]
    public async Task The_DGB_donation_address_is_unchanged()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);

        var dgb = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}/donation-address", key)));

        Assert.Equal("address", dgb.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Donation_options_offer_DD_only_while_the_store_can_take_it()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        await DdDonationAddress(client, key, storeId);

        var on = await Json(await client.GetAsync($"/v1/pay/public/stores/{storeId}/donation-options"));
        Assert.Equal(TestDdKeys.Address(0), on.GetProperty("ddAddress").GetString());
        Assert.True(on.GetProperty("ddAvailable").GetBoolean());
        Assert.Equal("mainnet", on.GetProperty("network").GetString());
        Assert.False(string.IsNullOrEmpty(on.GetProperty("dgbAddress").GetString()));

        (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = false })))
            .EnsureSuccessStatusCode();
        var off = await Json(await client.GetAsync($"/v1/pay/public/stores/{storeId}/donation-options"));
        Assert.Equal(JsonValueKind.Null, off.GetProperty("ddAddress").ValueKind);
        Assert.False(off.GetProperty("ddAvailable").GetBoolean());
        Assert.Equal(TestDdKeys.Address(0), (await factory.LoadStore(storeId))!.DdDonationAddress); // kept while the key stays
    }

    [Fact]
    public async Task Donation_options_report_a_down_explorer_without_hiding_the_address()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        await DdDonationAddress(client, key, storeId);
        factory.DdExplorer.Down = true;

        var options = await Json(await client.GetAsync($"/v1/pay/public/stores/{storeId}/donation-options"));

        Assert.False(options.GetProperty("ddAvailable").GetBoolean());
        Assert.Equal("dd_service_unavailable", options.GetProperty("ddReason").GetString());
    }

    [Fact]
    public async Task Donation_options_never_create_an_address_and_404_an_unknown_store()
    {
        using var factory = Factory();
        var (client, _, storeId) = await DdStore(factory);

        var options = await Json(await client.GetAsync($"/v1/pay/public/stores/{storeId}/donation-options"));

        Assert.Equal(JsonValueKind.Null, options.GetProperty("ddAddress").ValueKind);
        Assert.Equal(0, await DdAddressCount(factory));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/pay/public/stores/sto_nope/donation-options")).StatusCode);
    }
}
