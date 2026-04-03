using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class TransactionEndpoints
{
    public static RouteGroupBuilder MapTransactionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{txid}", GetTransaction).WithName("GetTransaction");
        group.MapGet("/{txid}/raw", GetRawTransaction).WithName("GetRawTransaction");
        group.MapPost("/broadcast", BroadcastTransaction).WithName("BroadcastTransaction");
        group.MapPost("/decode", DecodeTransaction).WithName("DecodeTransaction");
        group.MapPost("/create", CreateRawTransaction).WithName("CreateRawTransaction");
        group.MapPost("/fund", FundRawTransaction).WithName("FundRawTransaction");
        // Tier 2
        group.MapPost("/testmempool", TestMempoolAccept).WithName("TestMempoolAccept");
        return group;
    }

    private static async Task<IResult> GetTransaction(DigiByteRpcClient rpc, string txid)
    {
        var tx = await rpc.CallAsync<JsonElement>("getrawtransaction", txid, true);
        return Results.Ok(tx);
    }

    private static async Task<IResult> GetRawTransaction(DigiByteRpcClient rpc, string txid)
    {
        var hex = await rpc.CallAsync<string>("getrawtransaction", txid, false);
        return Results.Ok(new { txid, hex });
    }

    private static async Task<IResult> BroadcastTransaction(DigiByteRpcClient rpc, BroadcastRequest req)
    {
        var txid = await rpc.CallAsync<string>("sendrawtransaction", req.Hex);
        return Results.Ok(new { txid });
    }

    private static async Task<IResult> DecodeTransaction(DigiByteRpcClient rpc, DecodeRequest req)
    {
        var decoded = await rpc.CallAsync<JsonElement>("decoderawtransaction", req.Hex);
        return Results.Ok(decoded);
    }

    private static async Task<IResult> CreateRawTransaction(DigiByteRpcClient rpc, CreateRawTxRequest req)
    {
        var hex = await rpc.CallAsync<string>("createrawtransaction", req.Inputs, req.Outputs);
        return Results.Ok(new { hex });
    }

    private static async Task<IResult> FundRawTransaction(DigiByteRpcClient rpc, FundRawTxRequest req)
    {
        var result = await rpc.CallAsync<JsonElement>("fundrawtransaction", req.Hex);
        return Results.Ok(result);
    }

    private static async Task<IResult> TestMempoolAccept(DigiByteRpcClient rpc, TestMempoolRequest req)
    {
        var result = await rpc.CallAsync<JsonElement>("testmempoolaccept", new[] { req.Hex });
        return Results.Ok(result);
    }

    public record BroadcastRequest(string Hex);
    public record DecodeRequest(string Hex);
    public record CreateRawTxRequest(JsonElement Inputs, JsonElement Outputs);
    public record FundRawTxRequest(string Hex);
    public record TestMempoolRequest(string Hex);
}
