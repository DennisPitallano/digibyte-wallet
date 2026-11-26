using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>POST /v1/pay/test/sessions/{id}/advance on real (non-demo) testnet/regtest sessions.</summary>
public class SandboxAdvanceTests
{
    private const string Hook = "https://hooks.example.com/digipay";

    private static async Task<(PayFactory Factory, HttpClient Client, string ApiKey, string StoreId, string SessionId)> Setup(string network = "testnet")
    {
        var factory = new PayFactory();
        var client = factory.CreateClient();
        var (apiKey, storeId) = await factory.RegisterMerchant(client);
        await factory.WithDb(async db =>
        {
            (await db.Stores.FirstAsync(s => s.Id == storeId)).Network = network;
            await db.SaveChangesAsync();
        });
        await factory.SetStoreWebhook(storeId, Hook);
        var created = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", apiKey, new { amount = 2.5m }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var sessionId = (await Json(created)).GetProperty("id").GetString()!;
        return (factory, client, apiKey, storeId, sessionId);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Advance(HttpClient client, string id, string? key, object? body = null)
    {
        var request = key is null
            ? new HttpRequestMessage(HttpMethod.Post, $"/v1/pay/test/sessions/{id}/advance") { Content = body is null ? null : JsonContent.Create(body) }
            : PayFactory.Authed(HttpMethod.Post, $"/v1/pay/test/sessions/{id}/advance", key, body);
        return client.SendAsync(request);
    }

    private static async Task<List<string>> Events(PayFactory factory, string storeId) =>
        (await factory.LoadDeliveries(storeId)).OrderBy(d => d.CreatedAt).Select(d => d.EventName).ToList();

    [Fact]
    public async Task Without_auth_a_real_session_is_refused()
    {
        var (factory, client, _, _, sessionId) = await Setup();
        using var _f = factory;
        var response = await Advance(client, sessionId, key: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await Json(response)).GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Another_merchants_key_gets_404()
    {
        var (factory, client, _, _, sessionId) = await Setup();
        using var _f = factory;
        var (otherKey, _) = await factory.RegisterMerchant(client);
        Assert.Equal(HttpStatusCode.NotFound, (await Advance(client, sessionId, otherKey)).StatusCode);
    }

    [Fact]
    public async Task Mainnet_store_is_refused()
    {
        var (factory, client, apiKey, storeId, sessionId) = await Setup("mainnet");
        using var _f = factory;
        var response = await Advance(client, sessionId, apiKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("advance only works on testnet or regtest stores", (await Json(response)).GetProperty("error").GetString());
        Assert.Empty(await factory.LoadDeliveries(storeId));
    }

    [Fact]
    public async Task Steps_pending_seen_paid_confirmed_and_fires_the_same_webhooks_as_a_real_payment()
    {
        var (factory, client, apiKey, storeId, sessionId) = await Setup("regtest");
        using var _f = factory;

        var seen = await Json(await Advance(client, sessionId, apiKey));
        Assert.Equal("seen", seen.GetProperty("status").GetString());
        Assert.Equal(0, seen.GetProperty("confirmations").GetInt32());
        Assert.Equal(250_000_000, seen.GetProperty("receivedSatoshis").GetInt64());
        Assert.StartsWith("simulated", seen.GetProperty("paidTxid").GetString());

        var paid = await Json(await Advance(client, sessionId, apiKey));
        Assert.Equal("paid", paid.GetProperty("status").GetString());
        Assert.Equal(1, paid.GetProperty("confirmations").GetInt32());

        var confirmed = await Json(await Advance(client, sessionId, apiKey));
        Assert.Equal("confirmed", confirmed.GetProperty("status").GetString());
        Assert.Equal(6, confirmed.GetProperty("confirmations").GetInt32());

        // Confirmed is the end: another call changes nothing and sends nothing.
        var again = await Json(await Advance(client, sessionId, apiKey));
        Assert.Equal("confirmed", again.GetProperty("status").GetString());

        Assert.Equal(["session.seen", "session.paid", "session.confirmed"], await Events(factory, storeId));
        Assert.Equal(3, factory.Receiver.Requests.Count);
    }

    [Fact]
    public async Task Body_jumps_to_a_status_and_sets_the_confirmation_count()
    {
        var (factory, client, apiKey, storeId, sessionId) = await Setup();
        using var _f = factory;

        var paid = await Json(await Advance(client, sessionId, apiKey, new { to = "paid", confirmations = 3 }));
        Assert.Equal("paid", paid.GetProperty("status").GetString());
        Assert.Equal(3, paid.GetProperty("confirmations").GetInt32());

        // Count only: stays paid, no second webhook.
        var more = await Json(await Advance(client, sessionId, apiKey, new { confirmations = 4 }));
        Assert.Equal(("paid", 4), (more.GetProperty("status").GetString(), more.GetProperty("confirmations").GetInt32()));
        Assert.Equal(["session.paid"], await Events(factory, storeId));

        var confirmed = await Json(await Advance(client, sessionId, apiKey, new { confirmations = 12 }));
        Assert.Equal(("confirmed", 12), (confirmed.GetProperty("status").GetString(), confirmed.GetProperty("confirmations").GetInt32()));
        Assert.Equal(["session.paid", "session.confirmed"], await Events(factory, storeId));
    }

    [Theory]
    [InlineData("{\"to\":\"refunded\"}")]
    [InlineData("{\"to\":\"paid\",\"confirmations\":7}")]
    [InlineData("{\"to\":\"paid\",\"confirmations\":0}")]
    [InlineData("{\"to\":\"confirmed\",\"confirmations\":2}")]
    [InlineData("{\"to\":\"seen\",\"confirmations\":1}")]
    [InlineData("{\"confirmations\":-1}")]
    [InlineData("not json")]
    public async Task Invalid_bodies_are_400(string body)
    {
        var (factory, client, apiKey, storeId, sessionId) = await Setup();
        using var _f = factory;
        var request = PayFactory.Authed(HttpMethod.Post, $"/v1/pay/test/sessions/{sessionId}/advance", apiKey);
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await Json(response)).GetProperty("error").GetString()));
        Assert.Empty(await factory.LoadDeliveries(storeId));
    }

    [Fact]
    public async Task It_never_moves_backwards()
    {
        var (factory, client, apiKey, _, sessionId) = await Setup();
        using var _f = factory;
        await Advance(client, sessionId, apiKey, new { to = "confirmed" });
        Assert.Equal(HttpStatusCode.Conflict, (await Advance(client, sessionId, apiKey, new { to = "seen" })).StatusCode);
    }

    [Fact]
    public async Task Advanced_sessions_are_flagged_simulated_and_the_invoice_monitor_skips_them()
    {
        var (factory, client, apiKey, _, sessionId) = await Setup();
        using var _f = factory;
        var untouched = (await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", apiKey, new { amount = 1m })))).GetProperty("id").GetString();

        await Advance(client, sessionId, apiKey); // → seen, which the monitor would normally poll

        await factory.WithDb(async db =>
        {
            Assert.True((await db.Sessions.SingleAsync(s => s.Id == sessionId)).Simulated);
            var watched = await InvoiceMonitor.SessionsToWatch(db, DateTime.UtcNow).Select(s => s.Id).ToListAsync();
            Assert.DoesNotContain(sessionId, watched);
            Assert.Contains(untouched, watched);
        });
    }

    [Fact]
    public async Task Demo_sessions_still_advance_without_auth_and_without_webhooks()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var demo = await Json(await client.PostAsync("/v1/pay/test/demo-session", null));
        var id = demo.GetProperty("sessionId").GetString()!;

        var statuses = new List<string?>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await Json(await Advance(client, id, key: null))).GetProperty("status").GetString());
        Assert.Equal(["seen", "paid", "confirmed", "confirmed"], statuses);

        await factory.WithDb(async db => Assert.False((await db.Sessions.SingleAsync(s => s.Id == id)).Simulated));
        Assert.Empty(factory.Receiver.Requests);
    }
}
