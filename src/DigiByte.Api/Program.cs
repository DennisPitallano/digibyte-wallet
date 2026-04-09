using DigiByte.Api.Endpoints;
using DigiByte.Api.Hubs;

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
    .MapDigiIdEndpoints();

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// SignalR hubs
app.MapHub<TradeChatHub>("/hubs/trade");
app.MapHub<MultisigRoomHub>("/hubs/multisig-room");

app.Run();
