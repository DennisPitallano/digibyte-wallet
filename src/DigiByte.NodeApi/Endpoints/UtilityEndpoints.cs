using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class UtilityEndpoints
{
    public static RouteGroupBuilder MapUtilityEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/version", GetVersion).WithName("GetVersion");
        group.MapPost("/verify", VerifyMessage).WithName("VerifyMessage");
        group.MapPost("/stop", StopNode).WithName("StopNode");
        group.MapGet("/uptime", GetUptime).WithName("GetUptime");
        group.MapPost("/rescan", RescanBlockchain).WithName("RescanBlockchain");
        group.MapGet("/txout/{txid}/{n:int}", GetTxOut).WithName("GetTxOut");
        group.MapGet("/txoutset", GetTxOutSetInfo).WithName("GetTxOutSetInfo");
        // UTXO locking
        group.MapPost("/utxo/lock", LockUnspent).WithName("LockUnspent");
        group.MapPost("/utxo/unlock", UnlockUnspent).WithName("UnlockUnspent");
        return group;
    }

    private static async Task<IResult> GetVersion(DigiByteRpcClient rpc)
    {
        var networkInfo = await rpc.CallAsync<JsonElement>("getnetworkinfo");
        return Results.Ok(new
        {
            version = networkInfo.TryGetProperty("version", out var v) ? v.GetInt32() : 0,
            subversion = networkInfo.TryGetProperty("subversion", out var sv) ? sv.GetString() : null,
            protocolversion = networkInfo.TryGetProperty("protocolversion", out var pv) ? pv.GetInt32() : 0,
        });
    }

    private static async Task<IResult> VerifyMessage(DigiByteRpcClient rpc, VerifyRequest req)
    {
        var valid = await rpc.CallAsync<bool>("verifymessage", req.Address, req.Signature, req.Message);
        return Results.Ok(new { valid, address = req.Address });
    }

    private static async Task<IResult> StopNode(DigiByteRpcClient rpc)
    {
        var result = await rpc.CallAsync<string>("stop");
        return Results.Ok(new { message = result });
    }

    private static async Task<IResult> GetUptime(DigiByteRpcClient rpc)
    {
        var seconds = await rpc.CallAsync<int>("uptime");
        return Results.Ok(new { uptimeSeconds = seconds, uptimeHuman = TimeSpan.FromSeconds(seconds).ToString() });
    }

    private static async Task<IResult> RescanBlockchain(DigiByteRpcClient rpc, int startHeight = 0)
    {
        var result = await rpc.CallAsync<JsonElement>("rescanblockchain", startHeight);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTxOut(DigiByteRpcClient rpc, string txid, int n, bool includeMempool = true)
    {
        var txout = await rpc.CallAsync<JsonElement>("gettxout", txid, n, includeMempool);
        return Results.Ok(txout);
    }

    private static async Task<IResult> GetTxOutSetInfo(DigiByteRpcClient rpc) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("gettxoutsetinfo"));

    private static async Task<IResult> LockUnspent(DigiByteRpcClient rpc, LockRequest req)
    {
        var result = await rpc.CallAsync<bool>("lockunspent", false, req.Outputs);
        return Results.Ok(new { locked = result });
    }

    private static async Task<IResult> UnlockUnspent(DigiByteRpcClient rpc, LockRequest req)
    {
        var result = await rpc.CallAsync<bool>("lockunspent", true, req.Outputs);
        return Results.Ok(new { unlocked = result });
    }

    public record VerifyRequest(string Address, string Signature, string Message);
    public record LockRequest(JsonElement Outputs);
}
