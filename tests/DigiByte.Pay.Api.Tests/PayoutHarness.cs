using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// One merchant with a mainnet store, an API key and a Dashboard session, over a <see cref="FakeDdNode"/>
/// (specs/034 tasks T004). Helpers set a payout address, take DD payments and run payout cycles.
/// </summary>
public sealed class PayoutHarness : IDisposable
{
    public const string OperatorKey = "op-test-key";

    public PayFactory Factory { get; }
    public HttpClient Client { get; }
    public FakeDdNode Node => Factory.DdNode;
    public string ApiKey { get; private set; } = "";
    public string StoreId { get; private set; } = "";
    public string Token { get; private set; } = "";
    public string SessionId { get; private set; } = "";

    private PayoutHarness(IDictionary<string, string?>? settings)
    {
        var all = new Dictionary<string, string?> { ["DigiPay:OperatorKey"] = OperatorKey };
        if (settings is not null) foreach (var (k, v) in settings) all[k] = v;
        Factory = PayFactory.Create(settings: all);
        Client = Factory.CreateClient();
    }

    public static async Task<PayoutHarness> CreateAsync(IDictionary<string, string?>? settings = null)
    {
        var h = new PayoutHarness(settings);
        (h.ApiKey, h.StoreId) = await h.Factory.RegisterMerchant(h.Client);
        (h.Token, h.SessionId) = await h.Factory.CreateSessionToken(h.ApiKey);
        return h;
    }

    public void Dispose() => Factory.Dispose();

    public Task<HttpResponseMessage> AsSession(HttpMethod method, string url, object? body = null) =>
        Client.SendAsync(PayFactory.Authed(method, url, Token, body));

    public Task<HttpResponseMessage> AsKey(HttpMethod method, string url, object? body = null) =>
        Client.SendAsync(PayFactory.Authed(method, url, ApiKey, body));

    public async Task<HttpResponseMessage> PutAddress(string address, string? storeId = null)
    {
        var grant = await Factory.GrantStepUp(SessionId);
        return await AsSession(HttpMethod.Put, $"/v1/pay/stores/{storeId ?? StoreId}/payout-address", new { address, stepUpGrant = grant });
    }

    /// <summary>Sets a payout address through the API (step-up included) and returns it.</summary>
    public async Task<string> SetAddress(string? address = null, string? storeId = null)
    {
        address ??= PayFactory.NewDdAddress();
        var response = await PutAddress(address, storeId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return address;
    }

    /// <summary>Turns DD on (the store must have a payout address) and creates a DD session.</summary>
    public async Task<(string SessionId, string Address)> NewDdSession(long cents, int expiryMinutes = 30)
    {
        (await AsKey(HttpMethod.Patch, $"/v1/pay/stores/{StoreId}", new { digiDollarEnabled = true })).EnsureSuccessStatusCode();
        var response = await AsKey(HttpMethod.Post, "/v1/pay/sessions", new { storeId = StoreId, currency = "DD", amountCentsDd = cents, expiresInSeconds = expiryMinutes * 60 });
        var json = await Json(response);
        return (json.GetProperty("id").GetString()!, json.GetProperty("address").GetString()!);
    }

    /// <summary>Takes a paid DD session: creates it and has the fake wallet receive the DD.</summary>
    public async Task<(string SessionId, string Address, string Txid)> Paid(long cents, int confirmations = 6)
    {
        var (sessionId, address) = await NewDdSession(cents);
        var txid = Node.Receive(address, cents, confirmations);
        return (sessionId, address, txid);
    }

    public Task Cycle() => Factory.RunPayoutCycleAsync();

    public async Task<JsonElement> Balance(string? storeId = null) =>
        await Json(await AsKey(HttpMethod.Get, $"/v1/pay/stores/{storeId ?? StoreId}/dd-balance"));

    public async Task<List<DdPayout>> Payouts()
    {
        List<DdPayout> list = [];
        await Factory.WithDb(async db => list = await db.DdPayouts.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync());
        return list;
    }

    public async Task<PayStore> Store(string? storeId = null) => (await Factory.LoadStore(storeId ?? StoreId))!;

    public async Task<List<AuditEvent>> Audit(string actionPrefix)
    {
        List<AuditEvent> list = [];
        await Factory.WithDb(async db => list = await db.AuditEvents.AsNoTracking().Where(a => a.Action.StartsWith(actionPrefix)).ToListAsync());
        return list;
    }

    public PayoutAlerts Alerts => Factory.Services.GetRequiredService<PayoutAlerts>();

    public static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    public static async Task<string?> Error(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
    }
}
