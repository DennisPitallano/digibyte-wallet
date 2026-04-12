using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class MiningEndpoints
{
    public static RouteGroupBuilder MapMiningEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/info", GetMiningInfo).WithName("GetMiningInfo");
        group.MapGet("/difficulty", GetDifficulty).WithName("GetDifficulty");
        group.MapPost("/generate/{count:int}", GenerateToAddress).WithName("GenerateToAddress");
        group.MapGet("/hashrate", GetNetworkHashPs).WithName("GetNetworkHashPs");
        return group;
    }

    private static async Task<IResult> GetMiningInfo(DigiByteRpcClient rpc) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("getmininginfo"));

    private static async Task<IResult> GetDifficulty(DigiByteRpcClient rpc) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("getdifficulty"));

    private static async Task<IResult> GenerateToAddress(DigiByteRpcClient rpc, int count, GenerateRequest req) =>
        Results.Ok(new { blocks = await rpc.CallAsync<JsonElement>("generatetoaddress", count, req.Address) });

    private static async Task<IResult> GetNetworkHashPs(DigiByteRpcClient rpc, int nblocks = 120) =>
        Results.Ok(new { hashesPerSecond = await rpc.CallAsync<decimal>("getnetworkhashps", nblocks) });

    public record GenerateRequest(string Address);
}
