using System.Text.Json.Serialization;
using DigiByte.SpvGateway.Matching;
using NBitcoin;

namespace DigiByte.SpvGateway.Endpoints;

public sealed class ScanRequest
{
    [JsonPropertyName("addresses")] public List<string>? Addresses { get; set; }
    [JsonPropertyName("fromHeight")] public int? FromHeight { get; set; }
    [JsonPropertyName("toHeight")] public int? ToHeight { get; set; }
    [JsonPropertyName("knownOutpoints")] public List<string>? KnownOutpoints { get; set; }
}

public sealed class UtxoRequest
{
    [JsonPropertyName("addresses")] public List<string>? Addresses { get; set; }
    [JsonPropertyName("fromHeight")] public int? FromHeight { get; set; }
}

public static class ScanEndpoints
{
    /// <summary>
    /// POST /v1/scan and POST /v1/utxos. Addresses and outpoints travel only in the body, are held only while the request runs, and are
    /// never logged. Responses carry the raw transaction and its merkle branch so the wallet can check everything itself.
    /// </summary>
    public static void MapScan(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/scan", async (ScanRequest? request, SpvGatewayOptions options, ScanService scan, HttpContext context) =>
        {
            if (request is null || request.FromHeight is null || request.ToHeight is null)
                throw new GatewayException(GatewayError.BadRequest, "The request needs 'addresses', 'fromHeight' and 'toHeight'.");
            var addresses = AddressNetworkGuard.Require(options.Network, request.Addresses, options.MaxAddressesPerRequest);
            var outpoints = ParseOutpoints(request.KnownOutpoints);
            return Results.Ok(await scan.ScanAsync(addresses, request.FromHeight.Value, request.ToHeight.Value, outpoints, context.RequestAborted));
        });

        app.MapPost("/v1/utxos", async (UtxoRequest? request, SpvGatewayOptions options, ScanService scan, HttpContext context) =>
        {
            if (request is null || request.FromHeight is null)
                throw new GatewayException(GatewayError.BadRequest, "The request needs 'addresses' and 'fromHeight'.");
            var addresses = AddressNetworkGuard.Require(options.Network, request.Addresses, options.MaxAddressesPerRequest);
            return Results.Ok(await scan.UtxosAsync(addresses, request.FromHeight.Value, context.RequestAborted));
        });
    }

    private static List<OutPoint> ParseOutpoints(List<string>? texts)
    {
        var result = new List<OutPoint>();
        if (texts is null)
            return result;
        if (texts.Count > ScanService.MaxOutpoints)
            throw new GatewayException(GatewayError.OverLimit, $"Too many outpoints in one request (max {ScanService.MaxOutpoints}).");
        foreach (var text in texts)
        {
            var parts = text?.Split(':');
            if (parts is not { Length: 2 } || parts[0].Length != 64 || !uint256.TryParse(parts[0], out var txid) || !uint.TryParse(parts[1], out var n))
                throw new GatewayException(GatewayError.BadRequest, "An outpoint is not valid; expected txid:index.");
            result.Add(new OutPoint(txid, n));
        }
        return result;
    }
}
