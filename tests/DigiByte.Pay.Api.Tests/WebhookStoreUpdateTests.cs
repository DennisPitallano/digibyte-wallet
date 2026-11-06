using System.Net;

namespace DigiByte.Pay.Api.Tests;

/// <summary>T023 (DGB-03): the store update route applies the same validator, and localhost needs the development flag.</summary>
public class WebhookStoreUpdateTests
{
    private static async Task<HttpStatusCode> Patch(PayFactory factory, HttpClient client, string apiKey, string storeId, string url)
    {
        using var request = PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", apiKey, new { webhookUrl = url });
        return (await client.SendAsync(request)).StatusCode;
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://example.com:22/hook")]
    [InlineData("https://internal.example/hook")]
    [InlineData("http://localhost/hook")]
    public async Task HostileWebhookUrl_IsRefused_AndNothingIsStored(string url)
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);

        Assert.Equal(HttpStatusCode.BadRequest, await Patch(factory, client, apiKey, storeId, url));
        Assert.Null((await factory.LoadStore(storeId))!.WebhookUrl);
    }

    [Fact]
    public async Task PublicHttpsUrl_IsAccepted_AndAGeneratedSecretIsStored()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);

        Assert.Equal(HttpStatusCode.OK, await Patch(factory, client, apiKey, storeId, "https://hooks.example.com:8443/digipay"));

        var store = (await factory.LoadStore(storeId))!;
        Assert.Equal("https://hooks.example.com:8443/digipay", store.WebhookUrl);
        Assert.False(string.IsNullOrEmpty(store.WebhookSecret));
    }

    [Fact]
    public async Task EmptyUrl_ClearsTheWebhook()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);
        await Patch(factory, client, apiKey, storeId, "https://hooks.example.com/digipay");

        Assert.Equal(HttpStatusCode.OK, await Patch(factory, client, apiKey, storeId, ""));
        Assert.Null((await factory.LoadStore(storeId))!.WebhookUrl);
    }

    [Fact]
    public async Task HttpsLocalhost_IsRefusedByDefault_AndAllowedOnlyWithTheDevelopmentFlag()
    {
        using (var prod = PayFactory.Create(allowLoopback: false))
        {
            var client = prod.CreateClient();
            var (apiKey, storeId) = await prod.RegisterMerchant(client);
            Assert.Equal(HttpStatusCode.BadRequest, await Patch(prod, client, apiKey, storeId, "https://localhost:8443/hook"));
        }

        using var dev = PayFactory.Create(allowLoopback: true);
        var devClient = dev.CreateClient();
        var (devKey, devStore) = await dev.RegisterMerchant(devClient);
        Assert.Equal(HttpStatusCode.OK, await Patch(dev, devClient, devKey, devStore, "https://localhost:8443/hook"));
        // The flag permits loopback only: private addresses stay refused.
        Assert.Equal(HttpStatusCode.BadRequest, await Patch(dev, devClient, devKey, devStore, "https://10.0.0.5/hook"));
    }

    [Fact]
    public async Task UpdateWithoutAuth_IsStillUnauthorized()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (_, storeId) = await factory.RegisterMerchant(client);
        var response = await client.PatchAsJsonAsyncForTests($"/v1/pay/stores/{storeId}", new { webhookUrl = "https://hooks.example.com/x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

internal static class HttpClientTestExtensions
{
    public static Task<HttpResponseMessage> PatchAsJsonAsyncForTests(this HttpClient client, string url, object body) =>
        client.PatchAsync(url, System.Net.Http.Json.JsonContent.Create(body));
}
