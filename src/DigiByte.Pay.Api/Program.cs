using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Endpoints;
using DigiByte.Pay.Api.Hubs;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT; mirror the wallet's convention.
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

var digipayDb = builder.Configuration["DigiPay:DbPath"] ?? "digipay.db";
builder.Services.AddDbContext<DigiPayDbContext>(opt =>
    opt.UseSqlite($"Data Source={digipayDb}"));

builder.Services.AddHttpClient("DigiPayChain", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiPay/0.1");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("DigiPayWebhook", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiPay-Webhook/0.1");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<CheckoutNotifier>();
builder.Services.AddSingleton<AuthChallengeStore>();
builder.Services.AddScoped<WebhookDispatcher>();
builder.Services.AddHostedService<InvoiceMonitor>();

// CORS scoped to the Pay.Web origin (and whatever else gets added via ClientOrigin).
builder.Services.AddCors(options =>
{
    options.AddPolicy("PayWebClient", policy =>
    {
        var origins = (builder.Configuration.GetValue<string>("ClientOrigin") ?? "http://localhost:5252")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
});

app.UseCors("PayWebClient");

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "DigiByte.Pay.Api",
    status = "healthy",
    timestamp = DateTime.UtcNow,
}));

app.MapGroup("/v1/pay").MapPaymentsEndpoints(app.Configuration);
app.MapGroup("/v1/pay/auth").MapAuthEndpoints(app.Configuration);
app.MapGroup("/v1/pay/me").MapMerchantMeEndpoints();
app.MapHub<CheckoutHub>("/hubs/checkout");

app.Run();
