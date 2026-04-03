using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class KeyEndpoints
{
    public static RouteGroupBuilder MapKeyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/dump/{address}", DumpPrivKey).WithName("DumpPrivKey");
        group.MapPost("/import/privkey", ImportPrivKey).WithName("ImportPrivKey");
        group.MapPost("/import/pubkey", ImportPubKey).WithName("ImportPubKey");
        group.MapPost("/import/address", ImportAddress).WithName("ImportAddress");
        group.MapPost("/import/multi", ImportMulti).WithName("ImportMulti");
        group.MapPost("/multisig", AddMultisigAddress).WithName("AddMultisigAddress");
        group.MapPost("/refill", KeyPoolRefill).WithName("KeyPoolRefill");
        return group;
    }

    private static async Task<IResult> DumpPrivKey(DigiByteRpcClient rpc, string address) =>
        Results.Ok(new { address, privateKey = await rpc.CallAsync<string>("dumpprivkey", address) });

    private static async Task<IResult> ImportPrivKey(DigiByteRpcClient rpc, ImportKeyRequest req)
    {
        await rpc.CallVoidAsync("importprivkey", req.Key, req.Label ?? "", req.Rescan);
        return Results.Ok(new { message = "Private key imported" });
    }

    private static async Task<IResult> ImportPubKey(DigiByteRpcClient rpc, ImportKeyRequest req)
    {
        await rpc.CallVoidAsync("importpubkey", req.Key, req.Label ?? "", req.Rescan);
        return Results.Ok(new { message = "Public key imported" });
    }

    private static async Task<IResult> ImportAddress(DigiByteRpcClient rpc, ImportAddressRequest req)
    {
        await rpc.CallVoidAsync("importaddress", req.Address, req.Label ?? "", req.Rescan);
        return Results.Ok(new { message = "Address imported" });
    }

    private static async Task<IResult> ImportMulti(DigiByteRpcClient rpc, ImportMultiRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("importmulti", req.Requests));

    private static async Task<IResult> AddMultisigAddress(DigiByteRpcClient rpc, MultisigRequest req) =>
        Results.Ok(await rpc.CallAsync<JsonElement>("addmultisigaddress", req.NRequired, req.Keys));

    private static async Task<IResult> KeyPoolRefill(DigiByteRpcClient rpc, int size = 100)
    {
        await rpc.CallVoidAsync("keypoolrefill", size);
        return Results.Ok(new { message = $"Key pool refilled to {size}" });
    }

    public record ImportKeyRequest(string Key, string? Label = null, bool Rescan = false);
    public record ImportAddressRequest(string Address, string? Label = null, bool Rescan = false);
    public record ImportMultiRequest(JsonElement Requests);
    public record MultisigRequest(int NRequired, string[] Keys);
}
