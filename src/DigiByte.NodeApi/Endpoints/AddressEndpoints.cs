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
        var result = await rpc.CallAsync<JsonElement>("scantxoutset", "start",
            new object[] { new { desc = $"addr({address})" } });

        var utxos = new List<object>();
        if (result.TryGetProperty("unspents", out var unspents))
        {
            foreach (var u in unspents.EnumerateArray())
            {
                utxos.Add(new
                {
                    txid = u.GetProperty("txid").GetString(),
                    vout = u.GetProperty("vout").GetInt32(),
                    amount = u.GetProperty("amount").GetDecimal(),
                    amountSatoshis = (long)(u.GetProperty("amount").GetDecimal() * 100_000_000m),
                    height = u.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
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
}
