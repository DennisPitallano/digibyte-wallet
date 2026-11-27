using System.Net;
using System.Text.Json;

namespace DigiByte.Pay.Api.Tests;

/// <summary>/v1/pay/dev/* exists only in Development with DigiPay:DevSignIn=true.</summary>
public class DevSignInTests
{
    private static PayFactory Factory(string environment, bool? flag) =>
        PayFactory.Create(environment: environment, settings: flag is null
            ? null
            : new Dictionary<string, string?> { ["DigiPay:DevSignIn"] = flag.Value ? "true" : "false" });

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Production", null)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public async Task Is_absent_outside_development_or_when_the_flag_is_off(string environment, bool? flag)
    {
        using var factory = Factory(environment, flag);
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/v1/pay/dev/signin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/pay/dev/status")).StatusCode);
    }

    [Fact]
    public async Task Signs_in_as_the_dev_merchant_with_a_testnet_store_ready_for_sessions()
    {
        using var factory = Factory("Development", true);
        var client = factory.CreateClient();

        var status = await client.GetAsync("/v1/pay/dev/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.True((await Json(status)).GetProperty("enabled").GetBoolean());

        // Same body as a finished Digi-ID poll: { status, merchantId, displayName, token }.
        var signin = await Json(await client.PostAsync("/v1/pay/dev/signin", null));
        Assert.Equal("signed", signin.GetProperty("status").GetString());
        Assert.Equal("Dev merchant", signin.GetProperty("displayName").GetString());
        var token = signin.GetProperty("token").GetString()!;
        Assert.StartsWith("dps_", token);
        var merchantId = signin.GetProperty("merchantId").GetString();

        var me = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/me", token)));
        var store = Assert.Single(me.GetProperty("stores").EnumerateArray());
        Assert.Equal("testnet", store.GetProperty("network").GetString());
        Assert.True(store.GetProperty("hasReceive").GetBoolean());
        // specs/035 T026: DigiDollar on, with the test mnemonic's testnet DD key and no payout address.
        Assert.True(store.GetProperty("digiDollarEnabled").GetBoolean());
        Assert.Equal(TestDdKeys.Address(0, "testnet"), store.GetProperty("ddKey").GetProperty("firstAddress").GetString());

        var session = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", token, new { amount = 1m }));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var sessionId = (await Json(session)).GetProperty("id").GetString()!;

        // The sandbox can move it along, since the store is on testnet.
        var advanced = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, $"/v1/pay/test/sessions/{sessionId}/advance", token));
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        // Signing in again reuses the same merchant.
        var again = await Json(await client.PostAsync("/v1/pay/dev/signin", null));
        Assert.Equal(merchantId, again.GetProperty("merchantId").GetString());
    }

    [Fact]
    public async Task Is_left_out_of_the_openapi_document()
    {
        using var factory = Factory("Development", true);
        var doc = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        Assert.DoesNotContain("/v1/pay/dev", doc);
    }
}
