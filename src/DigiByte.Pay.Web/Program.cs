using DigiByte.Pay.Web.Components;
using Microsoft.AspNetCore.HttpOverrides;

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

// Pay.Api base URL — the source of truth for sessions + SignalR.
// Override with DigiPay:ApiUrl in production.
var payApiUrl = builder.Configuration["DigiPay:ApiUrl"] ?? "http://localhost:5008";
builder.Services.AddHttpClient("PayApi", c => c.BaseAddress = new Uri(payApiUrl));
builder.Services.AddSingleton(new PayApiUrl(payApiUrl));

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

// HTTPS redirect only outside prod — Railway's edge already terminates TLS.
// Redirecting again at the app layer causes 307 loops when the forwarded
// scheme isn't correctly applied.
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public record PayApiUrl(string Value);
