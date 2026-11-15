using DigiByte.Api.Services;

namespace DigiByte.Api.Endpoints;

/// <summary>
/// GET /api/oracle (specs/014-digibyte-io-oracle, contracts/oracle-api.md): the oracle price and DigiDollar protection status, relayed from
/// digibyte.io. Read-only and anonymous; it reads no input, serves only the fixed oracle data, is rate-limited per visitor, and logs no visitor.
/// </summary>
public static class OracleEndpoints
{
    public const string RateLimitPolicy = "oracle";

    public static RouteGroupBuilder MapOracleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (OracleRelay relay, HttpContext context, CancellationToken ct) =>
        {
            // The service keeps its own shared answer; nothing in between may cache a wallet's copy.
            context.Response.Headers.CacheControl = "no-store";

            if (!relay.Enabled)
                return Results.NotFound();

            var snapshot = await relay.GetAsync(ct);
            return snapshot is null
                ? Results.Json(new { reason = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Json(snapshot);
        }).RequireRateLimiting(RateLimitPolicy);

        return group;
    }
}
