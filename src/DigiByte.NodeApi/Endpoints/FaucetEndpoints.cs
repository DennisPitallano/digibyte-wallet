using System.Collections.Concurrent;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class FaucetEndpoints
{
    // Rate limiting: track last request time per IP
    private static readonly ConcurrentDictionary<string, DateTime> LastRequest = new();

    public static RouteGroupBuilder MapFaucetEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/send", SendTestCoins).WithName("FaucetSend");
        group.MapGet("/balance", GetFaucetBalance).WithName("FaucetBalance");
        group.MapGet("/status", GetFaucetStatus).WithName("FaucetStatus");
        return group;
    }

    private static async Task<IResult> SendTestCoins(DigiByteRpcClient rpc, NodeConfig config, HttpContext ctx, FaucetRequest req)
    {
        if (!config.FaucetEnabled || !config.IsTestnet)
            return Results.BadRequest(new { error = "Faucet is only available on testnet" });

        if (req.Amount <= 0 || req.Amount > config.FaucetMaxAmount)
            return Results.BadRequest(new { error = $"Amount must be between 0 and {config.FaucetMaxAmount} DGB" });

        if (string.IsNullOrWhiteSpace(req.Address))
            return Results.BadRequest(new { error = "Address is required" });

        // Rate limit by IP
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (LastRequest.TryGetValue(ip, out var lastTime))
        {
            var elapsed = DateTime.UtcNow - lastTime;
            if (elapsed.TotalMinutes < config.FaucetCooldownMinutes)
            {
                var remaining = config.FaucetCooldownMinutes - (int)elapsed.TotalMinutes;
                return Results.Json(
                    new { error = $"Rate limited. Try again in {remaining} minutes." },
                    statusCode: 429);
            }
        }

        try
        {
            var txid = await rpc.CallAsync<string>("sendtoaddress", req.Address, req.Amount);
            LastRequest[ip] = DateTime.UtcNow;

            return Results.Ok(new
            {
                txid,
                address = req.Address,
                amount = req.Amount,
                message = $"Sent {req.Amount} testnet DGB!"
            });
        }
        catch (RpcException ex) when (ex.Message.Contains("Insufficient"))
        {
            return Results.Json(
                new { error = "Faucet wallet is empty. Try mining some blocks first." },
                statusCode: 503);
        }
    }

    private static async Task<IResult> GetFaucetBalance(DigiByteRpcClient rpc, NodeConfig config)
    {
        if (!config.FaucetEnabled || !config.IsTestnet)
            return Results.BadRequest(new { error = "Faucet is only available on testnet" });

        var balance = await rpc.CallAsync<decimal>("getbalance");
        return Results.Ok(new
        {
            balanceDgb = balance,
            balanceSatoshis = (long)(balance * 100_000_000m),
        });
    }

    private static IResult GetFaucetStatus(NodeConfig config)
    {
        return Results.Ok(new
        {
            enabled = config.FaucetEnabled && config.IsTestnet,
            network = config.IsTestnet ? "testnet" : "mainnet",
            maxAmountPerRequest = config.FaucetMaxAmount,
            cooldownMinutes = config.FaucetCooldownMinutes,
        });
    }

    public record FaucetRequest(string Address, decimal Amount);
}
