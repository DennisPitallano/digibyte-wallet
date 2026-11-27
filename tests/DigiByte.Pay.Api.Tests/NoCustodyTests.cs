using System.Net;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/035 US5 (T047): DigiPay holds no DigiDollar, so nothing of spec 034's payouts is left.</summary>
public class NoCustodyTests
{
    [Fact]
    public async Task A_store_with_DigiDollar_on_deletes_like_any_other()
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f);
        var other = await Json(await c.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/stores", key, new { name = "Second", network = "mainnet" })));
        Assert.NotNull(other.GetProperty("id").GetString());
        await Create(c, key, new { storeId, currency = "DD", amountCentsDd = 500 });

        var response = await c.SendAsync(PayFactory.Authed(HttpMethod.Delete, $"/v1/pay/stores/{storeId}", key));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("dd_held", await response.Content.ReadAsStringAsync());
        Assert.Null(await f.LoadStore(storeId));
    }

    [Theory]
    [InlineData("GET", "/v1/pay/payouts")]
    [InlineData("GET", "/v1/pay/stores/{store}/payout-address")]
    [InlineData("PUT", "/v1/pay/stores/{store}/payout-address")]
    [InlineData("POST", "/v1/pay/operator/payouts/pause")]
    [InlineData("GET", "/v1/pay/operator/payouts/health")]
    public async Task Payout_and_operator_routes_are_gone(string method, string path)
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f);

        var response = await c.SendAsync(PayFactory.Authed(new HttpMethod(method), path.Replace("{store}", storeId), key, method == "GET" ? null : new { }));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task The_API_description_has_no_payout_routes()
    {
        using var f = Factory();
        var doc = await f.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.DoesNotContain("payout", doc, StringComparison.OrdinalIgnoreCase);
    }
}
