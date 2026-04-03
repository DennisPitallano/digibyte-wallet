using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class WalletEndpoints
{
    public static RouteGroupBuilder MapWalletEndpoints(this RouteGroupBuilder group)
    {
        // Tier 1
        group.MapGet("/info", GetWalletInfo).WithName("GetWalletInfo");
        group.MapGet("/balance", GetBalance).WithName("GetWalletBalance");
        group.MapGet("/balances", GetBalances).WithName("GetWalletBalances");
        group.MapGet("/newaddress", GetNewAddress).WithName("GetNewAddress");
        group.MapGet("/addressinfo/{address}", GetAddressInfo).WithName("GetWalletAddressInfo");
        group.MapGet("/unspent", ListUnspent).WithName("ListUnspent");
        group.MapPost("/send", SendToAddress).WithName("SendToAddress");
        group.MapGet("/transactions", ListTransactions).WithName("ListTransactions");
        group.MapGet("/sinceblock/{hash}", ListSinceBlock).WithName("ListSinceBlock");
        group.MapPost("/sign", SignRawTransaction).WithName("SignRawTransaction");
        group.MapPost("/create", CreateWallet).WithName("CreateWallet");
        // Tier 2
        group.MapPost("/load", LoadWallet).WithName("LoadWallet");
        group.MapPost("/unload", UnloadWallet).WithName("UnloadWallet");
        group.MapGet("/list", ListWallets).WithName("ListWallets");
        group.MapPost("/backup", BackupWallet).WithName("BackupWallet");
        group.MapPost("/encrypt", EncryptWallet).WithName("EncryptWallet");
        group.MapPost("/setlabel", SetLabel).WithName("SetLabel");
        return group;
    }

    // ---- Tier 1 ----

    private static async Task<IResult> GetWalletInfo(DigiByteRpcClient rpc)
    {
        var info = await rpc.CallAsync<JsonElement>("getwalletinfo");
        return Results.Ok(info);
    }

    private static async Task<IResult> GetBalance(DigiByteRpcClient rpc)
    {
        var balance = await rpc.CallAsync<decimal>("getbalance");
        return Results.Ok(new
        {
            balanceDgb = balance,
            balanceSatoshis = (long)(balance * 100_000_000m),
        });
    }

    private static async Task<IResult> GetBalances(DigiByteRpcClient rpc)
    {
        var balances = await rpc.CallAsync<JsonElement>("getbalances");
        return Results.Ok(balances);
    }

    private static async Task<IResult> GetNewAddress(DigiByteRpcClient rpc, string? label = null, string? type = null)
    {
        var address = type != null
            ? await rpc.CallAsync<string>("getnewaddress", label ?? "", type)
            : await rpc.CallAsync<string>("getnewaddress", label ?? "");
        return Results.Ok(new { address });
    }

    private static async Task<IResult> GetAddressInfo(DigiByteRpcClient rpc, string address)
    {
        var info = await rpc.CallAsync<JsonElement>("getaddressinfo", address);
        return Results.Ok(info);
    }

    private static async Task<IResult> ListUnspent(DigiByteRpcClient rpc, int minConfirmations = 1, int maxConfirmations = 9999999)
    {
        var utxos = await rpc.CallAsync<JsonElement>("listunspent", minConfirmations, maxConfirmations);
        return Results.Ok(utxos);
    }

    private static async Task<IResult> SendToAddress(DigiByteRpcClient rpc, SendRequest req)
    {
        var txid = await rpc.CallAsync<string>("sendtoaddress", req.Address, req.Amount, req.Comment ?? "", req.CommentTo ?? "");
        return Results.Ok(new { txid, address = req.Address, amount = req.Amount });
    }

    private static async Task<IResult> ListTransactions(DigiByteRpcClient rpc, int count = 50, int skip = 0)
    {
        var txs = await rpc.CallAsync<JsonElement>("listtransactions", "*", count, skip);
        return Results.Ok(txs);
    }

    private static async Task<IResult> ListSinceBlock(DigiByteRpcClient rpc, string hash)
    {
        var result = await rpc.CallAsync<JsonElement>("listsinceblock", hash);
        return Results.Ok(result);
    }

    private static async Task<IResult> SignRawTransaction(DigiByteRpcClient rpc, SignRequest req)
    {
        var result = await rpc.CallAsync<JsonElement>("signrawtransactionwithwallet", req.Hex);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateWallet(DigiByteRpcClient rpc, CreateWalletRequest req)
    {
        var result = await rpc.CallAsync<JsonElement>("createwallet", req.Name,
            req.DisablePrivateKeys, req.Blank, req.Passphrase ?? "");
        return Results.Ok(result);
    }

    // ---- Tier 2 ----

    private static async Task<IResult> LoadWallet(DigiByteRpcClient rpc, WalletNameRequest req)
    {
        var result = await rpc.CallAsync<JsonElement>("loadwallet", req.Name);
        return Results.Ok(result);
    }

    private static async Task<IResult> UnloadWallet(DigiByteRpcClient rpc, WalletNameRequest req)
    {
        var result = await rpc.CallAsync<JsonElement>("unloadwallet", req.Name);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListWallets(DigiByteRpcClient rpc)
    {
        var wallets = await rpc.CallAsync<JsonElement>("listwallets");
        return Results.Ok(wallets);
    }

    private static async Task<IResult> BackupWallet(DigiByteRpcClient rpc, BackupRequest req)
    {
        await rpc.CallVoidAsync("backupwallet", req.Destination);
        return Results.Ok(new { message = "Wallet backed up", destination = req.Destination });
    }

    private static async Task<IResult> EncryptWallet(DigiByteRpcClient rpc, EncryptRequest req)
    {
        var result = await rpc.CallAsync<string>("encryptwallet", req.Passphrase);
        return Results.Ok(new { message = result });
    }

    private static async Task<IResult> SetLabel(DigiByteRpcClient rpc, SetLabelRequest req)
    {
        await rpc.CallVoidAsync("setlabel", req.Address, req.Label);
        return Results.Ok(new { address = req.Address, label = req.Label });
    }

    public record SendRequest(string Address, decimal Amount, string? Comment = null, string? CommentTo = null);
    public record SignRequest(string Hex);
    public record CreateWalletRequest(string Name, bool DisablePrivateKeys = false, bool Blank = false, string? Passphrase = null);
    public record WalletNameRequest(string Name);
    public record BackupRequest(string Destination);
    public record EncryptRequest(string Passphrase);
    public record SetLabelRequest(string Address, string Label);
}
