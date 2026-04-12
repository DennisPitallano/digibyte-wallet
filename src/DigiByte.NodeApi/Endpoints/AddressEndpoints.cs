using System.Text.Json;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

public static class AddressEndpoints
{
    public static RouteGroupBuilder MapAddressEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{address}/balance", GetBalance).WithName("GetAddressBalance");
        group.MapGet("/{address}/utxos", GetUtxos).WithName("GetAddressUtxos");
        group.MapGet("/{address}/validate", ValidateAddress).WithName("ValidateAddress");
        group.MapPost("/utxos", GetBatchUtxos).WithName("GetBatchUtxos");
        group.MapPost("/balances", GetBatchBalances).WithName("GetBatchBalances");
        return group;
    }

    private static async Task<IResult> GetBalance(DigiByteRpcClient rpc, string address)
    {
        // Use scantxoutset to get balance for an arbitrary address (not in wallet)
        var result = await rpc.CallAsync<JsonElement>("scantxoutset", "start",
            new object[] { new { desc = $"addr({address})" } });

        var totalAmount = result.TryGetProperty("total_amount", out var amt)
            ? amt.GetDecimal()
            : 0m;

        return Results.Ok(new
        {
            address,
            balanceSatoshis = (long)(totalAmount * 100_000_000m),
            balanceDgb = totalAmount,
        });
    }

    private static async Task<IResult> GetUtxos(DigiByteRpcClient rpc, string address)
    {
        var currentHeight = await rpc.CallAsync<int>("getblockcount");
        var result = await rpc.CallAsync<JsonElement>("scantxoutset", "start",
            new object[] { new { desc = $"addr({address})" } });

        var utxos = new List<object>();
        if (result.TryGetProperty("unspents", out var unspents))
        {
            foreach (var u in unspents.EnumerateArray())
            {
                var height = u.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                utxos.Add(new
                {
                    txid = u.GetProperty("txid").GetString(),
                    vout = u.GetProperty("vout").GetInt32(),
                    amount = u.GetProperty("amount").GetDecimal(),
                    amountSatoshis = (long)(u.GetProperty("amount").GetDecimal() * 100_000_000m),
                    height,
                    confirmations = height > 0 ? currentHeight - height + 1 : 0,
                    scriptPubKey = u.TryGetProperty("scriptPubKey", out var s) ? s.GetString() : null,
                });
            }
        }

        return Results.Ok(new { address, utxos, count = utxos.Count });
    }

    private static async Task<IResult> ValidateAddress(DigiByteRpcClient rpc, string address)
    {
        var result = await rpc.CallAsync<JsonElement>("validateaddress", address);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetBatchUtxos(DigiByteRpcClient rpc, string[] addresses)
    {
        if (addresses.Length == 0)
            return Results.BadRequest(new { error = "No addresses provided" });
        if (addresses.Length > 100)
            return Results.BadRequest(new { error = "Maximum 100 addresses per request" });

        var currentHeight = await rpc.CallAsync<int>("getblockcount");
        var descriptors = addresses.Select(a => (object)new { desc = $"addr({a})" }).ToArray();
        var result = await rpc.CallAsync<JsonElement>("scantxoutset", "start", (object)descriptors);

        var utxos = new List<object>();
        if (result.TryGetProperty("unspents", out var unspents))
        {
            foreach (var u in unspents.EnumerateArray())
            {
                var height = u.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                utxos.Add(new
                {
                    txid = u.GetProperty("txid").GetString(),
                    vout = u.GetProperty("vout").GetInt32(),
                    amount = u.GetProperty("amount").GetDecimal(),
                    amountSatoshis = (long)(u.GetProperty("amount").GetDecimal() * 100_000_000m),
                    height,
                    confirmations = height > 0 ? currentHeight - height + 1 : 0,
                    scriptPubKey = u.TryGetProperty("scriptPubKey", out var s) ? s.GetString() : null,
                    desc = u.TryGetProperty("desc", out var d) ? d.GetString() : null,
                });
            }
        }

        return Results.Ok(new { utxos, count = utxos.Count });
    }

    private static async Task<IResult> GetBatchBalances(DigiByteRpcClient rpc, string[] addresses)
    {
        if (addresses.Length == 0)
            return Results.BadRequest(new { error = "No addresses provided" });
        if (addresses.Length > 100)
            return Results.BadRequest(new { error = "Maximum 100 addresses per request" });

        var descriptors = addresses.Select(a => (object)new { desc = $"addr({a})" }).ToArray();
        var result = await rpc.CallAsync<JsonElement>("scantxoutset", "start", (object)descriptors);

        var totalAmount = result.TryGetProperty("total_amount", out var amt)
            ? amt.GetDecimal()
            : 0m;

        return Results.Ok(new
        {
            balanceSatoshis = (long)(totalAmount * 100_000_000m),
            balanceDgb = totalAmount,
            addressCount = addresses.Length,
        });
    }
}
