using System.Net;
using System.Text.Json;
using DigiByte.Crypto.DigiId;
using DigiByte.Server.Shared;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Endpoints;

public static class DigiIdEndpoints
{
    public const string RateLimitPolicy = "digiid-callback";

    /// <summary>The longest part of the site's reply relayed to the wallet (plain text).</summary>
    private const int MaxRelayedChars = 2048;

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

        // POST /api/digiid/callback — proxy the signed Digi-ID response to the site named in the signed URI.
        // The destination is DERIVED from the signature-verified digiid:// URI (specs/008-security-hardening, DGB-02); a caller-supplied
        // callbackUrl is only accepted if it equals the derived one. Requests go through the guarded client (no redirects, no private addresses).
        group.MapPost("/callback", Callback).RequireRateLimiting(RateLimitPolicy);

        return group;
    }

    public static IResult Refuse(OutboundReason reason, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new { source = "proxy", reason = reason.ToText() }, statusCode: status);

    private static async Task<IResult> Callback(
        HttpRequest httpRequest,
        IHttpClientFactory httpFactory,
        IOptions<OutboundOptions> outbound,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("DigiIdProxy");

        string? uri, address, signature, callbackUrl = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Refuse(OutboundReason.InvalidUrl);
            uri = Str(root, "uri");
            address = Str(root, "address");
            signature = Str(root, "signature");
            callbackUrl = Str(root, "callbackUrl");
        }
        catch (JsonException) { return Refuse(OutboundReason.InvalidUrl); }
        catch (InvalidOperationException) { return Refuse(OutboundReason.InvalidUrl); }

        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(signature))
            return Refuse(OutboundReason.InvalidUrl);

        var parsed = DigiIdService.ParseUri(uri);
        if (parsed is null) return Refuse(OutboundReason.InvalidUrl);
        if (parsed.IsUnsecure) return Refuse(OutboundReason.MustUseHttps);

        // The target is whatever the signed URI says; a different callbackUrl is a sign of tampering.
        var target = parsed.CallbackUrl;
        if (!string.IsNullOrEmpty(callbackUrl) && !string.Equals(callbackUrl, target, StringComparison.Ordinal))
            return Refuse(OutboundReason.InvalidUrl);

        if (!DigiIdService.Verify(address, uri, signature))
            return Results.Json(new { source = "proxy", reason = "bad signature" }, statusCode: StatusCodes.Status400BadRequest);

        var policy = outbound.Value.DigiId();
        var refusal = OutboundUrlGuard.Validate(target, policy);
        if (refusal is not null)
        {
            log.LogWarning("Digi-ID callback refused ({Reason}) for {Target}", refusal.Value.ToText(), target);
            return Refuse(refusal.Value);
        }

        var http = httpFactory.CreateClient("DigiIdProxy");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, target)
            {
                Content = JsonContent.Create(new { address, uri, signature }),
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400)
                return Refuse(OutboundReason.RedirectsNotAllowed);

            var (text, truncated) = await response.Content.ReadCappedAsync(policy.MaxResponseBytes, ct);
            if (truncated)
            {
                log.LogWarning("Digi-ID callback to {Target} returned more than {Max} bytes", target, policy.MaxResponseBytes);
                return Refuse(OutboundReason.SiteUnreachable, StatusCodes.Status502BadGateway);
            }

            var body = text.Length > MaxRelayedChars ? text[..MaxRelayedChars] : text;
            return Results.Json(new { status = code, body }, statusCode: code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            if (ct.IsCancellationRequested) throw;

            if (FindBlocked(ex) is { } blocked)
            {
                log.LogWarning("Digi-ID callback blocked at connect time: {Detail}", blocked.Detail);
                return Refuse(OutboundReason.AddressNotAllowed);
            }

            log.LogWarning(ex, "Digi-ID callback to {Target} failed", target);
            return Refuse(OutboundReason.SiteUnreachable, StatusCodes.Status502BadGateway);
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static OutboundBlockedException? FindBlocked(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is OutboundBlockedException b) return b;
        return null;
    }
}
