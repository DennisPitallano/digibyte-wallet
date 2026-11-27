using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigiByte.Pay.Api.Tests;

/// <summary>Shared set-up for the specs/033 US3 (buyer choice) tests.</summary>
internal static class BuyerChoiceTestKit
{
    public static PayFactory Factory(bool surfaces = true) =>
        PayFactory.Create(settings: new Dictionary<string, string?> { [DdSurfacesGate.ConfigKey] = surfaces ? "true" : "false" });

    /// <summary>A merchant whose store has a payout address, DigiDollar on and buyer choice on.</summary>
    public static async Task<(HttpClient Client, string Key, string StoreId)> DdStore(PayFactory factory, bool ddOn = true)
    {
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        await factory.GivePayoutAddress(storeId);
        if (ddOn)
            (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = true })))
                .EnsureSuccessStatusCode();
        return (client, key, storeId);
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    public static Task<HttpResponseMessage> Create(HttpClient client, string key, object body) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key, body));

    /// <summary>A $25.00 (or <paramref name="fiat"/>) payment asking for buyer choice.</summary>
    public static async Task<JsonElement> CreateChoice(HttpClient client, string key, string storeId, decimal fiat = 25m, string currency = "USD")
    {
        var response = await Create(client, key, new { storeId, amount = 2500m, fiatCurrency = currency, fiatAmount = fiat, dgbPriceAtCreation = 0.01m, buyerChoice = true });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    public static Task<HttpResponseMessage> PickDd(HttpClient client, string sessionId) =>
        client.PostAsync($"/v1/pay/sessions/{sessionId}/options/dd", null);

    public static async Task<PaySession> Load(PayFactory factory, string sessionId)
    {
        PaySession? s = null;
        await factory.WithDb(async db => s = await db.Sessions.AsNoTracking().FirstAsync(x => x.Id == sessionId));
        return s!;
    }

    public static Task Tick(PayFactory factory) =>
        factory.Services.GetServices<IHostedService>().OfType<InvoiceMonitor>().Single().RunOnceAsync();

    public static string Address(JsonElement option) => option.GetProperty("address").GetString()!;

    public static JsonElement Option(JsonElement session, string currency) =>
        session.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("currency").GetString() == currency);

    public static void AssertConflict(HttpResponseMessage response, string error, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = Json(response).GetAwaiter().GetResult();
        Assert.Equal(error, body.GetProperty("error").GetString());
        Assert.Equal(reason, body.GetProperty("reason").GetString());
    }
}
