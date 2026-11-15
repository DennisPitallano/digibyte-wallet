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
        app.MapGet("/status.json", (BlockIndexStore index) => Results.Json(new { indexedFromHeight = index.IndexedFromHeight(), indexedToHeight = index.IndexedToHeight() }));
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
