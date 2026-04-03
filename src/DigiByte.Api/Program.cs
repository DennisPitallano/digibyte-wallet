using DigiByte.Api.Endpoints;
using DigiByte.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("WasmClient", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetValue<string>("ClientOrigin") ?? "https://localhost:5001")
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

app.UseHttpsRedirection();
app.UseCors("WasmClient");

// P2P API endpoints
app.MapGroup("/api/p2p")
    .MapP2PEndpoints();

// Username/phone directory for remittances
app.MapGroup("/api/directory")
    .MapDirectoryEndpoints();

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// SignalR hubs
app.MapHub<TradeChatHub>("/hubs/trade");

app.Run();
