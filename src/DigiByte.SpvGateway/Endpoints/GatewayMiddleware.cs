using System.Text.Json;
using DigiByte.SpvGateway.Health;

namespace DigiByte.SpvGateway.Endpoints;

/// <summary>
/// The outermost layer of the API: labels every response with the network, applies the per-client rate limit, and turns errors into the
/// JSON error shape of the contract. An unexpected failure is reported as <c>internal</c> with no detail, so no address, transaction or
/// internal message can leak through an error.
/// </summary>
public sealed class GatewayMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ClientLimiter _limiter;
    private readonly string _network;
    private readonly bool _trustForwardedFor;
    private readonly ILogger<GatewayMiddleware> _logger;

    public GatewayMiddleware(RequestDelegate next, ClientLimiter limiter, SpvGatewayOptions options, ILogger<GatewayMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _network = options.Network == GatewayNetwork.Testnet ? "testnet" : "mainnet";
        _trustForwardedFor = options.TrustForwardedFor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Network"] = _network;
        try
        {
            // The health report is cheap and monitors must always be able to read it.
            if (!context.Request.Path.StartsWithSegments("/v1/health"))
            {
                var client = ClientKey(context);
                if (!_limiter.TryAcquire(client, out var retryAfter))
                    throw new GatewayException(GatewayError.RateLimited, "Too many requests. Slow down and retry.", retryAfter);
            }
            await _next(context);
        }
        catch (GatewayException e)
        {
            await Write(context, e.Error, e.Message, e.RetryAfterSeconds);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Log the type only: the message could carry request data.
            _logger.LogError("Unhandled {ExceptionType} in {Path}", e.GetType().Name, context.Request.Path.Value);
            await Write(context, GatewayError.Internal, "The gateway could not handle the request.", null);
        }
    }

    private string ClientKey(HttpContext context)
    {
        if (_trustForwardedFor && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',')[0].Trim();
            if (first.Length > 0)
                return first;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private async Task Write(HttpContext context, GatewayError error, string message, int? retryAfter)
    {
        if (context.Response.HasStarted)
            return;
        context.Response.Clear();
        context.Response.Headers["X-Network"] = _network;
        context.Response.StatusCode = GatewayErrors.Status(error);
        context.Response.ContentType = "application/json";
        if (retryAfter is { } seconds)
            context.Response.Headers["Retry-After"] = seconds.ToString();
        await context.Response.WriteAsync(JsonSerializer.Serialize(
            new ErrorBody(GatewayErrors.Code(error), message, retryAfter), Json));
    }

    private sealed record ErrorBody(string Error, string Message, int? RetryAfterSeconds);
}
