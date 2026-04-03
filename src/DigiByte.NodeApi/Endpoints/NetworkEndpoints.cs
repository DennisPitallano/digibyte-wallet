using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class NetworkEndpoints
{
    public static RouteGroupBuilder MapNetworkEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/info", GetNetworkInfo).WithName("GetNetworkInfo");
        group.MapGet("/peers", GetPeerInfo).WithName("GetPeerInfo");
        group.MapGet("/connections", GetConnectionCount).WithName("GetConnectionCount");
        group.MapGet("/mempool", GetMempoolInfo).WithName("GetMempoolInfo");
        group.MapGet("/mempool/raw", GetRawMempool).WithName("GetRawMempool");
        group.MapGet("/mempool/entry/{txid}", GetMempoolEntry).WithName("GetMempoolEntry");
        group.MapGet("/fee/{blocks:int}", EstimateFee).WithName("EstimateFee");
        group.MapGet("/totals", GetNetTotals).WithName("GetNetTotals");
        group.MapPost("/ping", Ping).WithName("PingNode");
        return group;
    }

    private static async Task<IResult> GetNetworkInfo(DigiByteRpcClient rpc)
    {
        var info = await rpc.CallAsync<JsonElement>("getnetworkinfo");
        return Results.Ok(info);
    }

    private static async Task<IResult> GetPeerInfo(DigiByteRpcClient rpc)
    {
        var peers = await rpc.CallAsync<JsonElement>("getpeerinfo");
        return Results.Ok(peers);
    }

    private static async Task<IResult> GetConnectionCount(DigiByteRpcClient rpc)
    {
        var count = await rpc.CallAsync<int>("getconnectioncount");
        return Results.Ok(new { connections = count });
    }

    private static async Task<IResult> GetMempoolInfo(DigiByteRpcClient rpc)
    {
        var info = await rpc.CallAsync<JsonElement>("getmempoolinfo");
        return Results.Ok(info);
    }

    private static async Task<IResult> GetRawMempool(DigiByteRpcClient rpc)
    {
        var mempool = await rpc.CallAsync<JsonElement>("getrawmempool");
        return Results.Ok(mempool);
    }

    private static async Task<IResult> GetMempoolEntry(DigiByteRpcClient rpc, string txid)
    {
        var entry = await rpc.CallAsync<JsonElement>("getmempoolentry", txid);
        return Results.Ok(entry);
    }

    private static async Task<IResult> EstimateFee(DigiByteRpcClient rpc, int blocks)
    {
        var fee = await rpc.CallAsync<JsonElement>("estimatesmartfee", blocks);
        return Results.Ok(fee);
    }

    private static async Task<IResult> GetNetTotals(DigiByteRpcClient rpc)
    {
        var totals = await rpc.CallAsync<JsonElement>("getnettotals");
        return Results.Ok(totals);
    }

    private static async Task<IResult> Ping(DigiByteRpcClient rpc)
    {
        await rpc.CallVoidAsync("ping");
        return Results.Ok(new { message = "Ping sent to all peers" });
    }
}
