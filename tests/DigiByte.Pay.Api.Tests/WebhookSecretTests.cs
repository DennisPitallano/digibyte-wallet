using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Api.Tests;

/// <summary>The webhook signing secret is shown once: when it is first created, and on each rotate.</summary>
public class WebhookSecretTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PatchWebhook(HttpClient client, string apiKey, string storeId, string url) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", apiKey, new { webhookUrl = url }));

    private static Task<HttpResponseMessage> Rotate(HttpClient client, string apiKey, string storeId) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Post, $"/v1/pay/stores/{storeId}/webhook-secret/rotate", apiKey));

    [Fact]
    public async Task The_first_webhook_save_returns_the_new_secret_once()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);

        var first = await Json(await PatchWebhook(client, apiKey, storeId, "https://hooks.example.com/a"));
        var secret = first.GetProperty("webhookSecret").GetString();
        Assert.False(string.IsNullOrEmpty(secret));
        Assert.Equal(secret, (await factory.LoadStore(storeId))!.WebhookSecret);
        // The usual store fields are still there.
        Assert.Equal(storeId, first.GetProperty("id").GetString());
        Assert.True(first.GetProperty("hasWebhookSecret").GetBoolean());

        // Changing the address keeps the secret and doesn't show it again.
        var second = await Json(await PatchWebhook(client, apiKey, storeId, "https://hooks.example.com/b"));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("webhookSecret").ValueKind);
        Assert.Equal(secret, (await factory.LoadStore(storeId))!.WebhookSecret);
    }

    [Fact]
    public async Task Other_changes_return_no_secret()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);

        var response = await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", apiKey, new { name = "Renamed" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(response)).GetProperty("webhookSecret").ValueKind);
    }

    [Fact]
    public async Task Rotate_replaces_the_secret_returns_it_and_is_audited()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);
        await factory.SetStoreWebhook(storeId, "https://hooks.example.com/a", secret: "old-secret");

        var response = await Rotate(client, apiKey, storeId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        var secret = body.GetProperty("webhookSecret").GetString();
        Assert.False(string.IsNullOrEmpty(secret));
        Assert.NotEqual("old-secret", secret);
        Assert.Equal(48, secret!.Length);
        Assert.Single(body.EnumerateObject()); // only the secret

        Assert.Equal(secret, (await factory.LoadStore(storeId))!.WebhookSecret);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        var events = await db.AuditEvents.AsNoTracking().Where(e => e.TargetId == storeId && e.Action == "store.update").ToListAsync();
        Assert.Contains(events, e => e.Metadata != null && e.Metadata.Contains("webhookSecret"));
        Assert.DoesNotContain(events, e => (e.Metadata ?? "").Contains(secret) || (e.Summary ?? "").Contains(secret));
    }

    [Fact]
    public async Task Rotate_needs_sign_in_and_ownership()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (_, storeId) = await factory.RegisterMerchant(client);
        var (otherKey, _) = await factory.RegisterMerchant(client);

        var anonymous = await client.PostAsync($"/v1/pay/stores/{storeId}/webhook-secret/rotate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Another merchant's store is "not found", the same as PATCH.
        Assert.Equal(HttpStatusCode.NotFound, (await Rotate(client, otherKey, storeId)).StatusCode);
        Assert.Null((await factory.LoadStore(storeId))!.WebhookSecret);
    }
}
