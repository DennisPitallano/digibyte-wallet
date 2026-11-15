using System.Security.Cryptography;
using System.Text;
using DigiByte.SpvGateway.Index;

namespace DigiByte.SpvGateway.Endpoints;

public static class StatusPage
{
    private static readonly Lazy<string> Html = new(LoadHtml);

    /// <summary>
    /// GET /: a page for the operator that shows the sync progress by polling /v1/health and /status.json every two seconds. It shows only
    /// what the gateway reports about itself (no wallet or client data exists to show), and everything it uses (fonts, the mascot images)
    /// is served by the gateway itself, so it loads nothing from another site.
    /// </summary>
    public static void MapStatusPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Content(Html.Value, "text/html; charset=utf-8"));
        // Where the block index has got to: the part of the picture /v1/health does not carry. Heights only.
        // The recent requests come with it (route, status and time only; see RequestLog), unless the operator turned that off.
        app.MapGet("/status.json", (BlockIndexStore index, RequestLog log, SpvGatewayOptions options) =>
        {
            var (total, errors, byRoute, recent) = log.Snapshot();
            return Results.Json(new
            {
                indexedFromHeight = index.IndexedFromHeight(),
                indexedToHeight = index.IndexedToHeight(),
                requests = options.ShowRequestLog ? new { total, errors, byRoute, recent, canReset = options.AdminToken is not null } : null,
            });
        });
    }

    /// <summary>
    /// POST /status/reset: clears the request list. It exists only when the operator set <c>SpvGateway:AdminToken</c> (otherwise 404), needs that
    /// token in the <c>X-Admin-Token</c> header (compared without leaking how much of it matched), and answers a wrong token slowly so guessing
    /// gets nowhere. It never touches wallet data, because there is none.
    /// </summary>
    public static void MapStatusReset(this IEndpointRouteBuilder app)
    {
        app.MapPost("/status/reset", async (HttpContext context, RequestLog log, SpvGatewayOptions options, ILoggerFactory loggers) =>
        {
            if (options.AdminToken is not { } token)
                return Results.NotFound();

            var given = context.Request.Headers["X-Admin-Token"].ToString();
            var ok = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            if (!ok)
            {
                await Task.Delay(750, context.RequestAborted);
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            log.Clear();
            loggers.CreateLogger("StatusPage").LogWarning("The request list was reset from the status page.");
            return Results.Ok(new { cleared = true });
        });
    }

    private static string LoadHtml()
    {
        var assembly = typeof(StatusPage).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("status.html", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
