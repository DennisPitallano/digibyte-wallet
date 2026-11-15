using DigiByte.SpvGateway.Sync;

namespace DigiByte.SpvGateway.Endpoints;

public static class HeaderEndpoints
{
    /// <summary>
    /// GET /v1/headers?from=&amp;count=: consecutive 80-byte headers of the best chain as one binary body. The wallet checks every one of
    /// them itself, so this endpoint is a convenience and never a source of trust.
    /// </summary>
    public static void MapHeaders(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/headers", (HttpContext context, SpvGatewayOptions options, HeaderStore store) =>
        {
            if (!int.TryParse(context.Request.Query["from"], out var from) || from < 0)
                throw new GatewayException(GatewayError.BadRequest, "The query needs a non-negative 'from' height.");
            if (!int.TryParse(context.Request.Query["count"], out var count) || count < 1)
                throw new GatewayException(GatewayError.BadRequest, "The query needs a 'count' of at least 1.");
            if (count > options.MaxHeadersPerRequest)
                throw new GatewayException(GatewayError.OverLimit, $"Too many headers in one request (max {options.MaxHeadersPerRequest}).");

            var earliest = store.EarliestHeight();
            var tip = store.BestTip();
            if (earliest is null || tip is null)
                throw new GatewayException(GatewayError.NotReady, "The gateway has no headers yet.");
            if (from < earliest)
                throw new GatewayException(GatewayError.RangeUnavailable, $"Headers below height {earliest} are not available.");

            var headers = store.BestRange(from, count);
            var body = new byte[headers.Count * 80];
            for (var i = 0; i < headers.Count; i++)
                headers[i].ToBytes().CopyTo(body, i * 80);

            context.Response.Headers["X-Tip-Height"] = tip.Height.ToString();
            context.Response.Headers["X-Tip-Hash"] = tip.Header.Hash.ToString();
            return Results.File(body, "application/octet-stream");
        });
    }
}
