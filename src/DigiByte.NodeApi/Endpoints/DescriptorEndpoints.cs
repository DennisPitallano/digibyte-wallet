using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class DescriptorEndpoints
{
    public static RouteGroupBuilder MapDescriptorEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/info", GetDescriptorInfo).WithName("GetDescriptorInfo");
        group.MapPost("/derive", DeriveAddresses).WithName("DeriveAddresses");
        group.MapPost("/scan", ScanTxOutSet).WithName("ScanTxOutSet");
        return group;
    }

    private static async Task<IResult> GetDescriptorInfo(DigiByteRpcClient rpc, DescriptorRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("getdescriptorinfo", req.Descriptor));

    private static async Task<IResult> DeriveAddresses(DigiByteRpcClient rpc, DeriveRequest req) =>
        Results.Ok(new { addresses = await rpc.CallAsync<JsonElement>("deriveaddresses", req.Descriptor, req.Range) });

    private static async Task<IResult> ScanTxOutSet(DigiByteRpcClient rpc, ScanRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("scantxoutset", req.Action, req.Descriptors));

    public record DescriptorRequest(string Descriptor);
    public record DeriveRequest(string Descriptor, int[]? Range = null);
    public record ScanRequest(string Action, JsonElement Descriptors);
}
