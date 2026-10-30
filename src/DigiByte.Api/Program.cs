using System.Threading.RateLimiting;
using DigiByte.Api.Endpoints;
using DigiByte.Api.Hubs;
using DigiByte.Api.Services;
using DigiByte.P2P.Shared.Sponsors;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT env var — bind to it for cloud deployment
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("CoinGeckoProxy", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet/1.0");
});
builder.Services.AddHttpClient("DigiIdProxy", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet/1.0");
    client.Timeout = TimeSpan.FromSeconds(15);
});
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
// Explorers in the wallet's order: digiexplorer.info (Esplora) first, then digibyteblockexplorer.com (Blockbook).
builder.Services.AddSingleton<IExplorerTxLookup>(sp => new EsploraTxLookup(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("SponsorExplorers"), "https://digiexplorer.info/api"));
builder.Services.AddSingleton<IExplorerTxLookup>(sp => new BlockbookTxLookup(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("SponsorExplorers"), "https://digibyteblockexplorer.com"));
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
    limiter.OnRejected = async (context, token) =>
    {
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

// Digi-ID callback proxy (avoids CORS when posting signed auth to third-party servers)
app.MapGroup("/api/digiid")
    .MapDigiIdEndpoints(app.Configuration);

// Sponsor submissions (public intake relay; opens pull requests on the sponsors branch, never merges)
app.MapGroup("/api/sponsors")
    .MapSponsorEndpoints();

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// SignalR hubs
app.MapHub<TradeChatHub>("/hubs/trade");
app.MapHub<MultisigRoomHub>("/hubs/multisig-room");

app.Run();

// Exposed so the API test project can host the app in-process (tests/DigiByte.Api.Tests).
public partial class Program { }
