using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class PsbtEndpoints
{
    public static RouteGroupBuilder MapPsbtEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/create", CreateFundedPsbt).WithName("CreateFundedPsbt");
        group.MapPost("/process", ProcessPsbt).WithName("ProcessPsbt");
        group.MapPost("/combine", CombinePsbt).WithName("CombinePsbt");
        group.MapPost("/finalize", FinalizePsbt).WithName("FinalizePsbt");
        return group;
    }

    private static async Task<IResult> CreateFundedPsbt(DigiByteRpcClient rpc, CreatePsbtRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("walletcreatefundedpsbt", req.Inputs, req.Outputs));

    private static async Task<IResult> ProcessPsbt(DigiByteRpcClient rpc, PsbtStringRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("walletprocesspsbt", req.Psbt));

    private static async Task<IResult> CombinePsbt(DigiByteRpcClient rpc, CombinePsbtRequest req) =>
        Results.Ok(new { psbt = await rpc.CallAsync<string>("combinepsbt", req.Psbts) });

    private static async Task<IResult> FinalizePsbt(DigiByteRpcClient rpc, PsbtStringRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("finalizepsbt", req.Psbt));

    public record CreatePsbtRequest(JsonElement Inputs, JsonElement Outputs);
    public record PsbtStringRequest(string Psbt);
    public record CombinePsbtRequest(string[] Psbts);
}
