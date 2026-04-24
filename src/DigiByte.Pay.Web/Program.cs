using DigiByte.Pay.Web.Components;
using Microsoft.AspNetCore.HttpOverrides;
using ApexCharts;

var builder = WebApplication.CreateBuilder(args);

// Railway (and most PaaS) sets PORT; bind to it for cloud deployment.
// Same pattern as the wallet's DigiByte.Api/DigiByte.Pay.Api.
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ApexCharts — client-side chart lib; service registration wires up the
// JS interop singleton used by <ApexChart>.
builder.Services.AddApexCharts();

// Pay.Api base URL — the source of truth for sessions + SignalR.
// Override with DigiPay:ApiUrl in production.
var payApiUrl = builder.Configuration["DigiPay:ApiUrl"] ?? "http://localhost:5008";
// Separate public URL for links the browser has to reach — e.g. the Scalar
// reference at /scalar. When DigiPay:ApiUrl points at Railway's private
// network (digipay-api.railway.internal:8080) the browser can't resolve it,
// so prod deployments should set DigiPay:ApiPublicUrl to api.pay.dgbwallet.app.
// Falls back to the internal URL if not configured (fine in dev).
var payApiPublicUrl = builder.Configuration["DigiPay:ApiPublicUrl"] ?? payApiUrl;
builder.Services.AddHttpClient("PayApi", c => c.BaseAddress = new Uri(payApiUrl));
builder.Services.AddSingleton(new PayApiUrl(payApiUrl));
builder.Services.AddSingleton(new PayApiPublicUrl(payApiPublicUrl));

var app = builder.Build();

// Railway terminates TLS at the edge and forwards the original scheme as
// X-Forwarded-Proto. Without this, link generation thinks everything is http.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// HTTPS redirect only outside prod (Railway's edge terminates TLS — redirecting
// again at the app layer causes 307 loops when the forwarded scheme isn't
// applied) AND only when an https:// URL is actually bound. Without this guard,
// `dotnet run --urls http://…` emits a "Failed to determine the https port"
// warning on every startup.
if (!app.Environment.IsProduction())
{
    var urls = (app.Urls.Count > 0 ? app.Urls : (builder.Configuration["urls"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        .Concat(new[] { Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "" });
    var hasHttps = urls.Any(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    if (hasHttps)
    {
        app.UseHttpsRedirection();
    }
}

app.UseAntiforgery();

// Uniform /api/health across every service in the deployment — Railway's
// healthcheck default in railway.toml hits this path, and Pay.Api +
// DigiByte.Api + NodeApi all already serve it. Keeps the [deploy] block
// in railway.toml valid for Pay.Web too.
app.MapGet("/api/health", () => Results.Ok(new
{
    service = "DigiByte.Pay.Web",
    status = "healthy",
    timestamp = DateTime.UtcNow,
}));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public record PayApiUrl(string Value);
public record PayApiPublicUrl(string Value);
