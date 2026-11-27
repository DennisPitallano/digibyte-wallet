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
    private readonly string _environment;

    public FakeReceiver Receiver { get; } = new();
    public FakeResolver Resolver { get; } = new();
    public FakeDdNode DdNode { get; } = new();
    public FakeChain Chain { get; } = new();
    public FakeRates Rates { get; } = new();

    public PayFactory() : this(false, 1000, "Production", null) { }

    /// <param name="environment">Host environment; "Development" also loads appsettings.Development.json.</param>
    /// <param name="settings">Extra configuration values (win over the defaults below).</param>
    public static PayFactory Create(bool allowLoopback = false, int registerPerHour = 1000,
        string environment = "Production", IDictionary<string, string?>? settings = null) =>
        new(allowLoopback, registerPerHour, environment, settings);

    private PayFactory(bool allowLoopback, int registerPerHour, string environment, IDictionary<string, string?>? settings)
    {
        _environment = environment;
        _settings = new()
        {
            ["DigiPay:DbPath"] = _dbPath,
            // appsettings.Development.json points at a local Postgres; tests always use the SQLite file.
            ["DigiPay:ConnectionString"] = "",
            ["Outbound:AllowLoopback"] = allowLoopback ? "true" : "false",
            ["RateLimits:RegisterPerHour"] = registerPerHour.ToString(),
            // Tests drive the DD payout worker by hand (specs/034).
            ["DigiPay:Payouts:WorkerEnabled"] = "false",
        };
        if (settings is not null)
            foreach (var (key, value) in settings) _settings[key] = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IOutboundResolver>(Resolver);
            services.AddHttpClient("DigiPayWebhook").ConfigurePrimaryHttpMessageHandler(() => Receiver);
            services.AddHttpClient<DigiByte.Pay.Api.Services.DigiDollarNodeClient>().ConfigurePrimaryHttpMessageHandler(() => DdNode);
            services.AddSingleton<DigiByte.Pay.Api.Services.IChainServiceProvider>(Chain);
            services.AddHttpClient("DigiPayChain").ConfigurePrimaryHttpMessageHandler(() => Rates);
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

    /// <summary>Runs <paramref name="work"/> against the app's database in its own scope.</summary>
    public async Task WithDb(Func<DigiPayDbContext, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<DigiPayDbContext>());
    }

    // ---- DD payouts (specs/034) ----

    /// <summary>A valid DigiDollar address for <paramref name="network"/>, different on every call.</summary>
    public static string NewDdAddress(string network = "mainnet") =>
        DigiByte.Crypto.Addresses.DigiDollarAddressValidator.Encode(new Key().PubKey.TaprootInternalKey.ToBytes(), network);

    /// <summary>Gives a store a payout address directly, so it may turn DigiDollar on (FR-031).</summary>
    public async Task<string> GivePayoutAddress(string storeId, string? address = null)
    {
        string? set = null;
        await WithDb(async db =>
        {
            var store = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Stores, s => s.Id == storeId);
            store.DdPayoutAddress = set = address ?? NewDdAddress(store.Network);
            store.DdPayoutAddressSetAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
        return set!;
    }

    /// <summary>A Dashboard session token (<c>dps_</c>) for the merchant behind <paramref name="apiKey"/>, and its session id.</summary>
    public async Task<(string Token, string SessionId)> CreateSessionToken(string apiKey)
    {
        string token = "", sessionId = "";
        await WithDb(async db =>
        {
            var prefix = apiKey[..apiKey.LastIndexOf('_')];
            var key = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.ApiKeys, k => k.Prefix == prefix);
            var (sPrefix, secret, hash) = DigiByte.Pay.Api.Auth.MerchantAuthenticator.GenerateSessionToken();
            var session = new MerchantSession
            {
                Id = $"sess_{Guid.NewGuid():N}"[..21],
                MerchantId = key.MerchantId,
                TokenPrefix = sPrefix,
                TokenHash = hash,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
            };
            db.MerchantSessions.Add(session);
            var merchant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Merchants, m => m.Id == key.MerchantId);
            merchant.DigiIdAddress ??= NewAddress();
            await db.SaveChangesAsync();
            token = $"{sPrefix}_{secret}";
            sessionId = session.Id;
        });
        return (token, sessionId);
    }

    /// <summary>Inserts a valid step-up grant for the merchant and Dashboard session, as a signed Digi-ID re-sign-in would.</summary>
    public async Task<string> GrantStepUp(string sessionId)
    {
        var id = $"sug_{Guid.NewGuid():N}"[..20];
        await WithDb(async db =>
        {
            var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.MerchantSessions, s => s.Id == sessionId);
            var merchant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Merchants, m => m.Id == session.MerchantId);
            db.StepUpGrants.Add(new StepUpGrant
            {
                Id = id,
                MerchantId = merchant.Id,
                SessionId = sessionId,
                Purpose = StepUpGrant.PurposePayoutAddress,
                DigiIdAddress = merchant.DigiIdAddress!,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>Runs one DD payout worker cycle.</summary>
    public Task RunPayoutCycleAsync() =>
        Services.GetRequiredService<DigiByte.Pay.Api.Services.Payouts.DdPayoutWorker>().RunOnceAsync();

    public async Task<List<WebhookDelivery>> LoadDeliveries(string storeId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.WebhookDeliveries.Where(d => d.StoreId == storeId));
    }
}
