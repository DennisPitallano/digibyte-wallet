using System.Net;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/035 US2 (T027): every DD address comes from the store's key, from one shared sequence per key.</summary>
public class DdAddressSequenceTests
{
    private static async Task<string> NewDdSession(HttpClient c, string key, string storeId, long cents = 500)
    {
        var response = await Create(c, key, new { storeId, currency = "DD", amountCentsDd = cents });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Address(await Json(response));
    }

    private static string Address(System.Text.Json.JsonElement session) => session.GetProperty("address").GetString()!;

    [Fact]
    public async Task Payments_get_the_keys_addresses_in_order()
    {
        using var factory = Factory();
        var (c, key, storeId) = await DdStore(factory);

        var addresses = new List<string>();
        for (var i = 0; i < 3; i++) addresses.Add(await NewDdSession(c, key, storeId));

        Assert.Equal([TestDdKeys.Address(0), TestDdKeys.Address(1), TestDdKeys.Address(2)], addresses);
        await factory.WithDb(async db =>
        {
            var rows = await db.DdAddresses.OrderBy(a => a.Index).ToListAsync();
            Assert.Equal([0, 1, 2], rows.Select(r => r.Index));
            Assert.All(rows, r => Assert.Equal(DdAddress.KindPayment, r.Kind));
            Assert.All(rows, r => Assert.Equal(TestDdKeys.KeyId(), r.DdKeyId));
            Assert.Equal(3, (await db.DdKeySequences.SingleAsync()).NextIndex);
        });
    }

    [Fact]
    public async Task Two_stores_with_the_same_key_share_one_sequence()
    {
        using var factory = Factory();
        var (c1, k1, s1) = await DdStore(factory);
        var (c2, k2, s2) = await DdStore(factory);

        var a = await NewDdSession(c1, k1, s1);
        var b = await NewDdSession(c2, k2, s2);
        var c = await NewDdSession(c1, k1, s1);

        Assert.Equal(3, new HashSet<string> { a, b, c }.Count);
        Assert.Equal([TestDdKeys.Address(0), TestDdKeys.Address(1), TestDdKeys.Address(2)], new[] { a, b, c });
    }

    [Fact]
    public async Task Putting_an_old_key_back_continues_its_sequence()
    {
        using var factory = Factory();
        var (c, key, storeId) = await DdStore(factory);
        Assert.Equal(TestDdKeys.Address(0), await NewDdSession(c, key, storeId));

        await TestDdKeys.GiveKey(c, key, storeId, TestDdKeys.OtherWords);
        Assert.Equal(TestDdKeys.Address(0, words: TestDdKeys.OtherWords), await NewDdSession(c, key, storeId));

        await TestDdKeys.GiveKey(c, key, storeId);
        Assert.Equal(TestDdKeys.Address(1), await NewDdSession(c, key, storeId));
    }

    [Fact]
    public async Task Concurrent_payments_never_share_an_index()
    {
        using var factory = Factory();
        var (c, key, storeId) = await DdStore(factory);

        var addresses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => NewDdSession(c, key, storeId)));

        Assert.Equal(8, addresses.Distinct().Count());
        Assert.All(addresses, a => Assert.Contains(a, Enumerable.Range(0, 8).Select(i => TestDdKeys.Address(i))));
    }

    [Fact]
    public async Task The_donation_address_takes_the_next_index_once_and_keeps_it()
    {
        using var factory = Factory();
        var (c, key, storeId) = await DdStore(factory);
        await NewDdSession(c, key, storeId); // index 0

        async Task<string> Donation() => (await Json(await c.SendAsync(
            PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}/donation-address?currency=DD", key)))).GetProperty("address").GetString()!;

        var first = await Donation();
        var again = await Donation();
        var next = await NewDdSession(c, key, storeId);

        Assert.Equal(TestDdKeys.Address(1), first);
        Assert.Equal(first, again);
        Assert.Equal(TestDdKeys.Address(2), next);
        await factory.WithDb(async db =>
            Assert.Equal(DdAddress.KindDonation, (await db.DdAddresses.SingleAsync(a => a.Address == first)).Kind));
    }

    [Fact]
    public async Task Making_DD_addresses_never_calls_the_explorer_even_when_it_is_down()
    {
        using var factory = Factory();
        var (c, key, storeId) = await DdStore(factory);
        factory.DdExplorer.Down = true;
        var before = factory.DdExplorer.RequestCount(_ => true);

        var address = await NewDdSession(c, key, storeId);

        Assert.Equal(TestDdKeys.Address(0), address);
        Assert.Equal(before, factory.DdExplorer.RequestCount(_ => true));
    }

    [Fact]
    public async Task A_store_without_DD_gets_no_DD_payment()
    {
        using var factory = Factory();
        var (c, key, storeId) = await DdStore(factory, ddOn: false);

        var response = await Create(c, key, new { storeId, currency = "DD", amountCentsDd = 500 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDb(async db => Assert.False(await db.DdAddresses.AnyAsync()));
    }
}
