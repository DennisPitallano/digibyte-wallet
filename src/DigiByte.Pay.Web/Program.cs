using DigiByte.Pay.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Pay.Api base URL — the source of truth for sessions + SignalR.
// Override with DigiPay:ApiUrl in production.
var payApiUrl = builder.Configuration["DigiPay:ApiUrl"] ?? "http://localhost:5008";
builder.Services.AddHttpClient("PayApi", c => c.BaseAddress = new Uri(payApiUrl));
builder.Services.AddSingleton(new PayApiUrl(payApiUrl));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public record PayApiUrl(string Value);
