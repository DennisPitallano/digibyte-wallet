using System.Threading.RateLimiting;
using DigiByte.Api.Endpoints;
using DigiByte.Api.Hubs;
using DigiByte.Api.Services;
using DigiByte.P2P.Shared.Sponsors;
using DigiByte.Server.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT env var — bind to it for cloud deployment
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
// Key-possession gate for the multisig creation-room hub (constitution 1.2.0, co-signer coordination relay).
builder.Services.AddSingleton<DigiByte.Api.Hubs.CreationRoomGate>();
// Online multisig signing rooms (specs/017-multisig-online-signing): in-memory rooms, per-connection limits, anonymous telemetry.
builder.Services.Configure<DigiByte.Api.Multisig.SigningRoomOptions>(builder.Configuration.GetSection(DigiByte.Api.Multisig.SigningRoomOptions.SectionName));
builder.Services.AddSingleton<DigiByte.Api.Multisig.SigningRoomRegistry>();
builder.Services.AddSingleton<DigiByte.Api.Multisig.SigningRoomRateLimiter>();
builder.Services.AddSingleton<DigiByte.Api.Multisig.SigningRoomTelemetry>();
builder.Services.AddHostedService<DigiByte.Api.Multisig.SigningRoomSweeper>();
builder.Services.AddMemoryCache();
// ── Oracle relay (specs/014-digibyte-io-oracle): the wallet reads the oracle price and DigiDollar protection status from here, never from
// digibyte.io itself. Fixed requests, no cookies, no redirects; Oracle:Enabled switches the route off, Oracle:BaseUrl must be https. ──
builder.Services.Configure<OracleOptions>(builder.Configuration.GetSection(OracleOptions.SectionName));
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(OracleRelay.ClientName, client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet/1.0");
    client.Timeout = TimeSpan.FromSeconds(10); // a backstop only: the relay itself gives up after 3 seconds
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSingleton<OracleRelay>();
builder.Services.AddHttpClient("CoinGeckoProxy", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet/1.0");
});

// Daily price ranges for CSV exports (spec 031): the read-only archive shipped with the service, and its live sources
// (CoinGecko, Frankfurter/ECB, exchange-api), which receive only currencies and dates.
builder.Services.AddSingleton(sp => new DigiByte.Api.Services.PriceArchive(
    builder.Configuration["PriceArchive:Path"] is { Length: > 0 } archivePath
        ? archivePath
        : Path.Combine(AppContext.BaseDirectory, "PriceArchive")));
builder.Services.AddHttpClient(DigiByte.Api.Endpoints.PriceRangeEndpoint.HttpClientName, client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet/1.0");
    client.Timeout = TimeSpan.FromSeconds(20);
});
// Digi-ID callback proxy: the target is derived from a signature-verified URI and the client refuses redirects and private/loopback/metadata
// addresses at connect time (specs/008-security-hardening). Outbound:AllowLoopback is a development-only switch and defaults to false.
builder.Services.Configure<OutboundOptions>(builder.Configuration.GetSection(OutboundOptions.SectionName));
builder.Services.AddGuardedHttpClient("DigiIdProxy", OutboundUrlPolicy.DigiId(builder.Configuration.GetValue<bool>("Outbound:AllowLoopback")));
// ── Sponsor submissions (specs/006-sponsors-page): public intake relay, constitution 1.1.0 ──
// The bot token is read ONLY from the environment variable Sponsors__GitHubToken; it is never in a config file, log or response.
builder.Services.Configure<SponsorOptions>(builder.Configuration.GetSection("Sponsors"));
builder.Services.AddHttpClient<IGitHubSponsorClient, GitHubSponsorClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet-Sponsors/1.0");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("SponsorExplorers", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet-Sponsors/1.0");
    client.Timeout = TimeSpan.FromSeconds(10);
});
// Explorers for donation lookups: digiexplorer.info (Esplora). digibyteblockexplorer.com (Blockbook) was removed on 2026-11-16 because it
// had answered every request with 502; BlockbookTxLookup is still in the code to add back if an instance returns.
builder.Services.AddSingleton<IExplorerTxLookup>(sp => new EsploraTxLookup(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("SponsorExplorers"), "https://digiexplorer.info/api"));
builder.Services.AddScoped<IDonationVerifier, DonationVerifier>();
builder.Services.AddScoped<SponsorSubmissionService>();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(SponsorEndpoints.RateLimitPolicy, context =>
    {
        var perHour = Math.Max(1, context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<SponsorOptions>>().Value.PerVisitorPerHour);
        return RateLimitPartition.GetFixedWindowLimiter(SponsorEndpoints.ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perHour,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
    // Digi-ID callback proxy: 10 requests per minute per client (the visitor key is the rightmost X-Forwarded-For entry, which the proxy appended).
    limiter.AddPolicy(DigiIdEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(SponsorEndpoints.ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
    // Daily price ranges for CSV exports (spec 031): one request per export, so 30 per minute per visitor is generous.
    limiter.AddPolicy(DigiByte.Api.Endpoints.PriceRangeEndpoint.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(SponsorEndpoints.ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
    // Oracle relay: 30 requests per minute per visitor (a healthy wallet asks at most twice a minute).
    limiter.AddPolicy(OracleEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(SponsorEndpoints.ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
    // Signing-room hub connections: 30 per minute per client address, so reconnecting can't reset the hub's own
    // per-connection limits cheaply (specs/017-multisig-online-signing, research R10).
    // Only new connections (negotiate) count: long-polling transports make a request per poll and must not be throttled.
    limiter.AddPolicy(SigningHubRateLimitPolicy, context =>
        context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true
            ? RateLimitPartition.GetFixedWindowLimiter(SponsorEndpoints.ClientKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            })
            : RateLimitPartition.GetNoLimiter("signing-hub-transport"));
    limiter.OnRejected = async (context, token) =>
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/oracle"))
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(new { reason = "rate limited" }, token);
            return;
        }
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/digiid"))
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(new { source = "proxy", reason = "rate limited" }, token);
            return;
        }
        context.HttpContext.Response.Headers.RetryAfter = "3600";
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new SponsorSubmissionResult { State = SponsorSubmissionState.RateLimited }, SponsorJson.Options, token);
    };
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("WasmClient", policy =>
    {
        var origins = (builder.Configuration.GetValue<string>("ClientOrigin") ?? "https://localhost:5001")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Remember the raw visitor address before the forwarded-headers middleware rewrites it (used by the sponsor submission rate limiter).
app.Use((context, next) =>
{
    context.Items[SponsorEndpoints.ClientKeyItem] = SponsorEndpoints.ComputeClientKey(context);
    return next(context);
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseCors("WasmClient");
app.UseRateLimiter();

// P2P API endpoints
app.MapGroup("/api/p2p")
    .MapP2PEndpoints();

// Username/phone directory for remittances
app.MapGroup("/api/directory")
    .MapDirectoryEndpoints();

// Price proxy (avoids CORS/rate-limit issues from browser → CoinGecko)
app.MapGroup("/api/price")
    .MapPriceEndpoints();

// Oracle price and DigiDollar protection status, relayed from digibyte.io (read-only, anonymous, no input)
app.MapGroup("/api/oracle")
    .MapOracleEndpoints();

// Digi-ID callback proxy (avoids CORS when posting signed auth to third-party servers)
app.MapGroup("/api/digiid")
    .MapDigiIdEndpoints(app.Configuration);

// Sponsor submissions (public intake relay; opens pull requests on the sponsors branch, never merges)
app.MapGroup("/api/sponsors")
    .MapSponsorEndpoints();

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow, commit = BuildInfo.Commit }));

// SignalR hubs
app.MapHub<TradeChatHub>("/hubs/trade");
app.MapHub<MultisigRoomHub>("/hubs/multisig-room");
app.MapHub<DigiByte.Api.Hubs.MultisigSigningHub>("/hubs/multisig-signing").RequireRateLimiting(SigningHubRateLimitPolicy);

app.Run();

// Exposed so the API test project can host the app in-process (tests/DigiByte.Api.Tests).
public partial class Program
{
    /// <summary>Rate-limit policy for connections to the signing-room hub.</summary>
    public const string SigningHubRateLimitPolicy = "signing-hub-connect";
}
