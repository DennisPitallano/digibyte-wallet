using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Crypto.Networks;
using DigiByte.Pay.Api.Data;
using DigiByte.Server.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace DigiByte.Pay.Api.Tests;

/// <summary>Fake webhook receiver: records every request and answers with <see cref="Respond"/>.</summary>
public sealed class FakeReceiver : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request.RequestUri!);
        return Task.FromResult(Respond(request));
    }
}

/// <summary>Resolver that sends names to public addresses unless listed in <see cref="Private"/>.</summary>
public sealed class FakeResolver : IOutboundResolver
{
    public Dictionary<string, string> Private { get; } = new(StringComparer.OrdinalIgnoreCase) { ["internal.example"] = "10.0.0.5" };

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        Task.FromResult(new[] { IPAddress.Parse(Private.TryGetValue(host, out var ip) ? ip : "93.184.216.34") });
}

/// <summary>In-process DigiPay API on a throwaway SQLite file, with a fake receiver and resolver.</summary>
public sealed class PayFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"digipay-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;

    public FakeReceiver Receiver { get; } = new();
    public FakeResolver Resolver { get; } = new();

    public PayFactory() : this(false, 1000) { }

    public static PayFactory Create(bool allowLoopback = false, int registerPerHour = 1000) => new(allowLoopback, registerPerHour);

    private PayFactory(bool allowLoopback, int registerPerHour)
    {
        _settings = new()
        {
            ["DigiPay:DbPath"] = _dbPath,
            ["Outbound:AllowLoopback"] = allowLoopback ? "true" : "false",
            ["RateLimits:RegisterPerHour"] = registerPerHour.ToString(),
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IOutboundResolver>(Resolver);
            services.AddHttpClient("DigiPayWebhook").ConfigurePrimaryHttpMessageHandler(() => Receiver);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { /* sqlite may still hold the file briefly */ }
    }

    public static string NewAddress() =>
        new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Mainnet).ToString();

    public async Task<HttpResponseMessage> Register(HttpClient client, string? webhookUrl, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/pay/merchants")
        {
            Content = JsonContent.Create(new { displayName = "Test Shop", addressOrXpub = NewAddress(), network = "mainnet", webhookUrl }),
        };
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    /// <summary>Registers a merchant with no webhook and returns its API key and store id.</summary>
    public async Task<(string ApiKey, string StoreId)> RegisterMerchant(HttpClient client)
    {
        var response = await Register(client, webhookUrl: null);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("apiKey").GetString()!, doc.RootElement.GetProperty("storeId").GetString()!);
    }

    public static HttpRequestMessage Authed(HttpMethod method, string url, string apiKey, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    public async Task<PayStore?> LoadStore(string storeId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Stores, s => s.Id == storeId);
    }

    public async Task SetStoreWebhook(string storeId, string url, string secret = "secret-keep-me")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        var store = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Stores, s => s.Id == storeId);
        store.WebhookUrl = url;
        store.WebhookSecret = secret;
        await db.SaveChangesAsync();
    }

    public async Task<List<WebhookDelivery>> LoadDeliveries(string storeId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.WebhookDeliveries.Where(d => d.StoreId == storeId));
    }
}
