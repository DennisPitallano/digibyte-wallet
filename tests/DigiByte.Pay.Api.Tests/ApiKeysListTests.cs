using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Api.Tests;

/// <summary>POS tablet keys belong to POS tablets: the API keys list leaves them out.</summary>
public class ApiKeysListTests
{
    [Fact]
    public async Task The_api_keys_list_leaves_out_pos_tablet_keys()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);
        var merchantId = (await factory.LoadStore(storeId))!.MerchantId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
            db.ApiKeys.Add(new PayApiKey { Id = "key_postablet0000001", MerchantId = merchantId, Prefix = "dgp_postab01", Hash = "x", Label = "POS — Front counter" });
            db.ApiKeys.Add(new PayApiKey { Id = "key_postablet0000002", MerchantId = merchantId, Prefix = "dgp_postab02", Hash = "y", Label = "POS device — 2026-11-26" });
            await db.SaveChangesAsync();
        }

        var keys = await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/api-keys", apiKey));
        Assert.Equal(HttpStatusCode.OK, keys.StatusCode);
        using var doc = JsonDocument.Parse(await keys.Content.ReadAsStringAsync());
        var labels = doc.RootElement.EnumerateArray().Select(k => k.GetProperty("label").GetString()).ToList();
        Assert.Contains("SDK initial key", labels);
        Assert.DoesNotContain(labels, l => l != null && l.StartsWith("POS"));

        // They are still listed as tablets.
        var devices = await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/pos/devices", apiKey));
        using var devicesDoc = JsonDocument.Parse(await devices.Content.ReadAsStringAsync());
        Assert.Equal(2, devicesDoc.RootElement.GetArrayLength());
    }
}
