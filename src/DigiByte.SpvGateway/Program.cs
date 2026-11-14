// DigiByte SPV gateway: a small light-client service that talks the DigiByte peer protocol and answers the wallet over HTTP.
// This is the Phase 1 skeleton (specs/011-p2p-spv-gateway, T001). Real endpoints arrive in later phases.

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT; bind to it for cloud deployment, like the other services.
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

app.MapGet("/v1/health", () => Results.Ok(new { status = "starting", network = "mainnet" }));

app.Run();

/// <summary>Marker so tests can host the app in-process.</summary>
public partial class Program;
