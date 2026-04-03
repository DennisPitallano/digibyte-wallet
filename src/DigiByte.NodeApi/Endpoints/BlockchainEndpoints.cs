using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class BlockchainEndpoints
{
    public static RouteGroupBuilder MapBlockchainEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/info", GetBlockchainInfo).WithName("GetBlockchainInfo");
        group.MapGet("/height", GetBlockCount).WithName("GetBlockCount");
        group.MapGet("/block/{hashOrHeight}", GetBlock).WithName("GetBlock");
        group.MapGet("/blockhash/{height:int}", GetBlockHash).WithName("GetBlockHash");
        group.MapGet("/chaintips", GetChainTips).WithName("GetChainTips");
        // Tier 2
        group.MapGet("/block/{hash}/header", GetBlockHeader).WithName("GetBlockHeader");
        group.MapGet("/block/{hash}/filter", GetBlockFilter).WithName("GetBlockFilter");
        group.MapPost("/block/invalidate", InvalidateBlock).WithName("InvalidateBlock");
        group.MapPost("/block/reconsider", ReconsiderBlock).WithName("ReconsiderBlock");
        return group;
    }

    private static async Task<IResult> GetBlockchainInfo(DigiByteRpcClient rpc)
    {
        var info = await rpc.CallAsync<JsonElement>("getblockchaininfo");
        return Results.Ok(info);
    }

    private static async Task<IResult> GetBlockCount(DigiByteRpcClient rpc)
    {
        var height = await rpc.CallAsync<int>("getblockcount");
        return Results.Ok(new { height });
    }

    private static async Task<IResult> GetBlock(DigiByteRpcClient rpc, string hashOrHeight)
    {
        string hash;
        if (int.TryParse(hashOrHeight, out var height))
            hash = await rpc.CallAsync<string>("getblockhash", height);
        else
            hash = hashOrHeight;

        var block = await rpc.CallAsync<JsonElement>("getblock", hash, 2); // verbosity=2 includes tx details
        return Results.Ok(block);
    }

    private static async Task<IResult> GetBlockHash(DigiByteRpcClient rpc, int height)
    {
        var hash = await rpc.CallAsync<string>("getblockhash", height);
        return Results.Ok(new { height, hash });
    }

    private static async Task<IResult> GetChainTips(DigiByteRpcClient rpc)
    {
        var tips = await rpc.CallAsync<JsonElement>("getchaintips");
        return Results.Ok(tips);
    }

    // Tier 2
    private static async Task<IResult> GetBlockHeader(DigiByteRpcClient rpc, string hash)
    {
        var header = await rpc.CallAsync<JsonElement>("getblockheader", hash, true);
        return Results.Ok(header);
    }

    private static async Task<IResult> GetBlockFilter(DigiByteRpcClient rpc, string hash)
    {
        var filter = await rpc.CallAsync<JsonElement>("getblockfilter", hash);
        return Results.Ok(filter);
    }

    private static async Task<IResult> InvalidateBlock(DigiByteRpcClient rpc, BlockHashRequest req)
    {
        await rpc.CallVoidAsync("invalidateblock", req.Hash);
        return Results.Ok(new { message = "Block invalidated", hash = req.Hash });
    }

    private static async Task<IResult> ReconsiderBlock(DigiByteRpcClient rpc, BlockHashRequest req)
    {
        await rpc.CallVoidAsync("reconsiderblock", req.Hash);
        return Results.Ok(new { message = "Block reconsidered", hash = req.Hash });
    }

    public record BlockHashRequest(string Hash);
}
