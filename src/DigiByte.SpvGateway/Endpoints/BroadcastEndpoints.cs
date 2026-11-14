using System.Text.Json.Serialization;
using DigiByte.SpvGateway.Broadcast;

namespace DigiByte.SpvGateway.Endpoints;

public sealed class BroadcastRequest
{
    [JsonPropertyName("raw")] public string? Raw { get; set; }
}

public static class BroadcastEndpoints
{
    /// <summary>The largest request body accepted: a transaction at the size limit in hex, plus a little JSON.</summary>
    private const long MaxBodyBytes = BroadcastRelay.MaxTransactionBytes * 2L + 1024;

    /// <summary>
    /// POST /v1/broadcast: the only write path of the gateway, and it takes an already signed transaction only. Nothing about the
    /// request is logged. A transaction that is not valid serialized data is refused before any peer is contacted.
    /// </summary>
    public static void MapBroadcast(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/broadcast", async (HttpContext context, BroadcastRequest? request, BroadcastRelay relay) =>
        {
            if (context.Request.ContentLength > MaxBodyBytes)
                throw new GatewayException(GatewayError.OverLimit, "The request is too large.");
            if (request?.Raw is not { Length: > 0 } hex || hex.Length % 2 != 0 || hex.Length > MaxBodyBytes)
                throw new GatewayException(GatewayError.BadRequest, "The request needs 'raw', the signed transaction as hex.");

            byte[] raw;
            try
            {
                raw = Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                throw new GatewayException(GatewayError.BadRequest, "The transaction is not valid hex.");
            }

            try
            {
                return Results.Ok(await relay.RelayAsync(raw, context.RequestAborted));
            }
            catch (FormatException)
            {
                throw new GatewayException(GatewayError.BadRequest, "The bytes are not a valid transaction.");
            }
        });
    }
}
