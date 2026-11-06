using System.Net;
using System.Text.Json;

namespace DigiByte.Pay.Api.Tests;

/// <summary>T024 (DGB-03): the dispatcher re-validates the stored URL; a hostile one is never called (SC-002, SC-003).</summary>
public class WebhookDispatchTests
{
    private static async Task<(PayFactory Factory, HttpClient Client, string ApiKey, string StoreId)> Setup()
    {
        var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);
        return (factory, client, apiKey, storeId);
    }

    private static async Task<JsonElement> TestWebhook(HttpClient client, string apiKey, string storeId)
    {
        using var request = PayFactory.Authed(HttpMethod.Post, $"/v1/pay/stores/{storeId}/webhook/test", apiKey);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://example.com:22/hook")]
    [InlineData("https://internal.example/hook")]
    public async Task StoredHostileUrl_IsNotCalled_AndShowsAFailedDeliveryWithAReason(string url)
    {
        var (factory, client, apiKey, storeId) = await Setup();
        using var _ = factory;
        await factory.SetStoreWebhook(storeId, url, "my-secret");

        var result = await TestWebhook(client, apiKey, storeId);

        Assert.Empty(factory.Receiver.Requests);
        Assert.Equal("destination not allowed", result.GetProperty("error").GetString());
        var delivery = Assert.Single(await factory.LoadDeliveries(storeId));
        Assert.Equal("destination not allowed", delivery.ErrorMessage);
        Assert.Null(delivery.StatusCode);
        Assert.False(delivery.Success);
        Assert.Null(delivery.NextRetryAt); // a blocked destination is not retried
        // The stored URL and secret are left alone so the merchant can see and fix them.
        var store = (await factory.LoadStore(storeId))!;
        Assert.Equal(url, store.WebhookUrl);
        Assert.Equal("my-secret", store.WebhookSecret);
    }

    [Fact]
    public async Task StoredHostileUrl_ReasonIsGeneric_NoAddressesLeak()
    {
        var (factory, client, apiKey, storeId) = await Setup();
        using var _ = factory;
        await factory.SetStoreWebhook(storeId, "https://internal.example/hook");

        var result = await TestWebhook(client, apiKey, storeId);

        Assert.DoesNotContain("10.0.0.5", result.GetRawText());
        Assert.DoesNotContain("10.0.0.5", (await factory.LoadDeliveries(storeId)).Single().ErrorMessage);
    }

    [Fact]
    public async Task ValidStoredUrl_IsCalled()
    {
        var (factory, client, apiKey, storeId) = await Setup();
        using var _ = factory;
        await factory.SetStoreWebhook(storeId, "https://hooks.example.com/digipay");

        var result = await TestWebhook(client, apiKey, storeId);

        Assert.Equal("https://hooks.example.com/digipay", Assert.Single(factory.Receiver.Requests).ToString());
        Assert.Equal(200, result.GetProperty("statusCode").GetInt32());
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task ResponseSnippet_KeepsTheFirst512Characters_AsPlainText(int status)
    {
        var (factory, client, apiKey, storeId) = await Setup();
        using var _ = factory;
        await factory.SetStoreWebhook(storeId, "https://hooks.example.com/digipay");
        var html = "<script>alert(1)</script><b>" + new string('x', 2000);
        factory.Receiver.Respond = _ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(html) };

        await TestWebhook(client, apiKey, storeId);

        var snippet = Assert.Single(await factory.LoadDeliveries(storeId)).ResponseSnippet!;
        Assert.True(snippet.Length <= 512 + 64, $"snippet length {snippet.Length}"); // escaping may grow it slightly, but never past 512 source chars
        Assert.DoesNotContain("<script>", snippet);
        Assert.DoesNotContain("<b>", snippet);
        Assert.Contains("alert(1)", snippet);
    }

    [Fact]
    public async Task RegisterWithAHostileUrl_ThenTestTheWebhook_SendsNothing()
    {
        // SC-003 regression: the original attack registered a hostile URL and then pressed "test webhook".
        var (factory, client, apiKey, storeId) = await Setup();
        using var _ = factory;

        var register = await factory.Register(client, "https://169.254.169.254/latest/meta-data/");
        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);

        using var patch = PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", apiKey, new { webhookUrl = "https://169.254.169.254/latest/meta-data/" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(patch)).StatusCode);

        using var test = PayFactory.Authed(HttpMethod.Post, $"/v1/pay/stores/{storeId}/webhook/test", apiKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(test)).StatusCode); // no webhook configured

        Assert.Empty(factory.Receiver.Requests);
    }
}
