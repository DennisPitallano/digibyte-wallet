using System.Text.Json.Serialization;
using DigiByte.SpvGateway.Matching;
using NBitcoin;

namespace DigiByte.SpvGateway.Endpoints;

public sealed class ProofItemRequest
{
    [JsonPropertyName("txid")] public string? Txid { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
}

public sealed class ProofsRequest
{
    [JsonPropertyName("items")] public List<ProofItemRequest>? Items { get; set; }
}

public static class ProofEndpoints
{
    /// <summary>
    /// POST /v1/proofs (at most 20 items) and GET /v1/tx/{txid}. Transaction ids are public data, but nothing about a request is stored
    /// or logged, and the response carries the raw transaction and branch so the wallet checks them itself.
    /// </summary>
    public static void MapProofs(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/proofs", async (ProofsRequest? request, ProofService proofs, HttpContext context) =>
        {
            if (request?.Items is not { Count: > 0 } items)
                throw new GatewayException(GatewayError.BadRequest, "The request needs at least one item.");
            if (items.Count > ProofService.MaxItems)
                throw new GatewayException(GatewayError.OverLimit, $"Too many items in one request (max {ProofService.MaxItems}).");

            var parsed = new List<(uint256, int)>();
            foreach (var item in items)
            {
                if (!TryTxid(item.Txid, out var txid) || item.Height is not >= 0)
                    throw new GatewayException(GatewayError.BadRequest, "An item needs a transaction id and a height.");
                parsed.Add((txid, item.Height.Value));
            }
            return Results.Ok(await proofs.ProveAsync(parsed, context.RequestAborted));
        });

        app.MapGet("/v1/tx/{txid}", async (string txid, ProofService proofs, HttpContext context) =>
        {
            if (!TryTxid(txid, out var id))
                throw new GatewayException(GatewayError.BadRequest, "The transaction id is not valid.");
            var found = await proofs.GetTransactionAsync(id, context.RequestAborted);
            return found is null
                ? throw new GatewayException(GatewayError.NotFound, "The gateway does not know this transaction.")
                : Results.Ok(found);
        });
    }

    private static bool TryTxid(string? text, out uint256 txid)
    {
        txid = uint256.Zero;
        return text is { Length: 64 } && uint256.TryParse(text, out txid);
    }
}
