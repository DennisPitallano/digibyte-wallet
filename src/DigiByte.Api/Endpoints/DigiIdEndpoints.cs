using System.Text.Json;

namespace DigiByte.Api.Endpoints;

public static class DigiIdEndpoints
{
    public static RouteGroupBuilder MapDigiIdEndpoints(this RouteGroupBuilder group)
    {
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
