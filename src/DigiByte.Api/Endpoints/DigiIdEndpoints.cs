using System.Text.Json;

namespace DigiByte.Api.Endpoints;

public static class DigiIdEndpoints
{
    public static RouteGroupBuilder MapDigiIdEndpoints(this RouteGroupBuilder group, IConfiguration config)
    {
        // GET /api/digiid/redirect — Handle deep link redirect from digiid:// protocol
        // Third-party sites can link here so clicking it opens the wallet with the URI pre-filled.
        // Example: /api/digiid/redirect?uri=digiid://example.com/callback?x=abc123
        group.MapGet("/redirect", (string? uri) =>
        {
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("digiid://", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Missing or invalid 'uri' parameter. Must start with digiid://" });

            var appUrl = config["ClientWebUrl"] ?? "https://dgbwallet.app";
            var redirectUrl = $"{appUrl.TrimEnd('/')}/identity?uri={Uri.EscapeDataString(uri)}";
            return Results.Redirect(redirectUrl);
        });

        // POST /api/digiid/callback — proxy the signed Digi-ID response to the third-party callback
        // This avoids CORS issues when the browser can't POST directly to the callback server.
        group.MapPost("/callback", async (
            HttpRequest httpRequest,
            IHttpClientFactory httpFactory) =>
        {
            using var bodyDoc = await JsonDocument.ParseAsync(httpRequest.Body);
            var root = bodyDoc.RootElement;

            if (!root.TryGetProperty("callbackUrl", out var callbackUrlProp))
                return Results.BadRequest(new { error = "Missing callbackUrl" });

            var callbackUrl = callbackUrlProp.GetString();
            if (string.IsNullOrEmpty(callbackUrl)
                || (!callbackUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && !callbackUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = "Invalid callbackUrl" });

            // Build the payload the callback server expects: { address, uri, signature }
            var payload = new
            {
                address = root.GetProperty("address").GetString(),
                uri = root.GetProperty("uri").GetString(),
                signature = root.GetProperty("signature").GetString(),
            };

            var http = httpFactory.CreateClient("DigiIdProxy");
            var response = await http.PostAsJsonAsync(callbackUrl, payload);
            var body = await response.Content.ReadAsStringAsync();

            return Results.Json(
                new { status = (int)response.StatusCode, body },
                statusCode: (int)response.StatusCode);
        });

        return group;
    }
}
