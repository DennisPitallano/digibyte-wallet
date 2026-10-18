using System.Text.Json;
using DigiByte.NodeApi.DigiDollar;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Endpoints;

/// <summary>
/// REST wrappers around DigiByte Core's DigiDollar RPC surface.
/// See DIGIDOLLAR_WALLET_INTEGRATION.md for the underlying RPC contract.
///
/// All wallet-scoped RPCs are routed via <see cref="DigiByteRpcClient.CallWalletAsync{T}"/>
/// through /wallet/{DigiDollarConfig.WalletName}.
///
/// When DigiDollarConfig.Enabled=false, every endpoint returns 503.
/// Wallet-scoped routes (see <see cref="Middleware.WalletScopedRoutes"/>) require X-NodeApi-Key
/// for GET and POST alike and are not CORS-reachable from a browser origin
/// (enforced by <see cref="Middleware.WriteKeyAuthMiddleware"/> and the "DdWalletScoped" CORS policy).
/// </summary>
public static class DigiDollarEndpoints
{
    public static RouteGroupBuilder MapDigiDollarEndpoints(this RouteGroupBuilder group)
    {
        // Status (no wallet needed)
        group.MapGet("/deployment", GetDeploymentInfo).WithName("GetDdDeploymentInfo");
        group.MapGet("/stats", GetStats).WithName("GetDdStats");
        group.MapGet("/oracle/price", GetOraclePrice).WithName("GetDdOraclePrice");
        group.MapGet("/protection", GetProtectionStatus).WithName("GetDdProtectionStatus");
        group.MapGet("/health", GetHealth).WithName("GetDdHealth");

        // Wallet-facing, address-scoped read surface (public chain data; addresses only in POST bodies).
        group.MapPost("/addresses/utxos", DigiDollarReadHandlers.Utxos).WithName("GetDdAddressesUtxos");
        group.MapPost("/addresses/history", DigiDollarReadHandlers.History).WithName("GetDdAddressesHistory");
        group.MapGet("/tx/{txid}/status", DigiDollarReadHandlers.TxStatus).WithName("GetDdTxStatus");

        // Address
        group.MapPost("/address/new", GetNewAddress).WithName("GetNewDdAddress").RequireCors("DdWalletScoped");
        group.MapPost("/address/validate", ValidateAddress).WithName("ValidateDdAddress");
        group.MapGet("/addresses", ListAddresses).WithName("ListDdAddresses").RequireCors("DdWalletScoped");

        // Balance & UTXOs (wallet)
        group.MapGet("/balance", GetBalance).WithName("GetDdBalance").RequireCors("DdWalletScoped");
        group.MapGet("/balance/{address}", GetAddressBalance).WithName("GetDdAddressBalance").RequireCors("DdWalletScoped");
        group.MapGet("/unspent", ListUnspent).WithName("ListDdUnspent").RequireCors("DdWalletScoped");

        // Transactions (wallet)
        group.MapGet("/transactions", ListTransactions).WithName("ListDdTransactions").RequireCors("DdWalletScoped");
        group.MapPost("/send", SendDigiDollar).WithName("SendDigiDollar").RequireCors("DdWalletScoped");
        group.MapPost("/send-many", SendManyDigiDollar).WithName("SendManyDigiDollar").RequireCors("DdWalletScoped");

        // Mint / Redeem (wallet, Phase 5)
        group.MapPost("/mint", MintDigiDollar).WithName("MintDigiDollar").RequireCors("DdWalletScoped");
        group.MapPost("/redeem", RedeemDigiDollar).WithName("RedeemDigiDollar").RequireCors("DdWalletScoped");
        group.MapGet("/collateral/estimate", EstimateCollateral).WithName("EstimateDdCollateral");

        // Positions (wallet)
        group.MapGet("/positions", ListPositions).WithName("ListDdPositions").RequireCors("DdWalletScoped");
        group.MapGet("/positions/{positionId}/redemption", GetRedemptionInfo).WithName("GetDdRedemptionInfo").RequireCors("DdWalletScoped");

        return group;
    }

    // ==================== Feature-flag helper ====================

    /// <summary>
    /// Returns 503 IResult if DD is not enabled; otherwise null (proceed).
    /// </summary>
    private static IResult? RequireEnabled(DigiDollarConfig config) =>
        config.Enabled
            ? null
            : Results.Json(new
            {
                error = "digidollar_not_enabled",
                message = "DigiDollar support is not enabled on this NodeApi instance.",
            }, statusCode: 503);

    // ==================== Status (no wallet) ====================

    private static async Task<IResult> GetDeploymentInfo(DigiByteRpcClient rpc, DigiDollarConfig config)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var info = await rpc.CallAsync<JsonElement>("getdigidollardeploymentinfo");
        return Results.Ok(info);
    }

    private static async Task<IResult> GetStats(DigiByteRpcClient rpc, DigiDollarConfig config)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var stats = await rpc.CallAsync<JsonElement>("getdigidollarstats");
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetOraclePrice(DigiByteRpcClient rpc, DigiDollarConfig config)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var price = await rpc.CallAsync<JsonElement>("getoracleprice");
        return Results.Ok(price);
    }

    private static async Task<IResult> GetProtectionStatus(DigiByteRpcClient rpc, DigiDollarConfig config)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var status = await rpc.CallAsync<JsonElement>("getprotectionstatus");
        return Results.Ok(status);
    }

    /// <summary>
    /// Combined health check: deployment status + wallet load + DGB fee reserve + oracle freshness.
    /// Consumed by Railway health checks, wallet UI banner, and DigiPay dashboard.
    /// </summary>
    private static async Task<IResult> GetHealth(DigiByteRpcClient rpc, DigiDollarConfig config, DdIndexStatus indexStatus)
    {
        if (!config.Enabled)
        {
            return Results.Ok(new
            {
                enabled = false,
                deploymentActive = false,
                deployment_active = false,
                wallet_loaded = false,
                reserve_healthy = false,
                message = "DigiDollar not enabled",
            });
        }

        // Deployment
        var deploymentActive = false;
        var blockHeight = 0;
        try
        {
            var deployment = await rpc.CallAsync<JsonElement>("getdigidollardeploymentinfo");
            deploymentActive = deployment.TryGetProperty("enabled", out var e) && e.GetBoolean();
            blockHeight = deployment.TryGetProperty("current_height", out var h) ? h.GetInt32() : 0;
        }
        catch { /* leave defaults */ }

        // Fee reserve (via wallet balance)
        decimal feeReserveDgb = 0;
        var walletLoaded = false;
        try
        {
            feeReserveDgb = await rpc.CallWalletAsync<decimal>(config.WalletName, "getbalance");
            walletLoaded = true;
        }
        catch { /* wallet not loaded or RPC error */ }

        // Oracle price
        decimal oraclePriceUsd = 0;
        long oraclePriceAgeSeconds = -1;
        try
        {
            var oracle = await rpc.CallAsync<JsonElement>("getoracleprice");
            if (oracle.TryGetProperty("price_usd", out var pu) && pu.ValueKind == JsonValueKind.Number)
                oraclePriceUsd = pu.GetDecimal();
            else if (oracle.TryGetProperty("price_usd", out var puStr) && puStr.ValueKind == JsonValueKind.String
                     && decimal.TryParse(puStr.GetString(), out var parsed))
                oraclePriceUsd = parsed;
            if (oracle.TryGetProperty("age_seconds", out var age)) oraclePriceAgeSeconds = age.GetInt64();
        }
        catch { /* leave defaults */ }

        // Network health (system-wide collateral ratio)
        decimal networkHealthPercent = 0;
        try
        {
            var stats = await rpc.CallAsync<JsonElement>("getdigidollarstats");
            if (stats.TryGetProperty("system_health_percent", out var sh) && sh.ValueKind == JsonValueKind.Number)
                networkHealthPercent = sh.GetDecimal();
        }
        catch { /* leave defaults */ }

        // Protection flags (ERR / volatility freeze) for the wallet banner.
        JsonElement? protectionRaw = null;
        try { protectionRaw = await rpc.CallAsync<JsonElement>("getprotectionstatus"); }
        catch { /* leave null: flags default to false */ }

        var reserveHealthy = feeReserveDgb >= config.MinDgbFeeReserveDgb;

        // Wallet-facing fields (camelCase, contracts/nodeapi-digidollar-read.md). The snake_case fields above
        // stay for DigiPay and the dashboard.
        var nodeHeight = indexStatus.TipHeight ?? blockHeight;
        var oracleInfo = DdHealthMapper.MapOracle(oraclePriceUsd, oraclePriceAgeSeconds, nodeHeight);
        var protection = DdHealthMapper.MapProtection(protectionRaw, networkHealthPercent);

        return Results.Ok(new
        {
            enabled = true,
            deployment_active = deploymentActive,
            wallet_loaded = walletLoaded,
            wallet_name = config.WalletName,
            dgb_fee_reserve_dgb = feeReserveDgb,
            min_dgb_fee_reserve_dgb = config.MinDgbFeeReserveDgb,
            reserve_healthy = reserveHealthy,
            oracle_price_usd = oraclePriceUsd,
            oracle_price_age_seconds = oraclePriceAgeSeconds,
            node_block_height = blockHeight,
            network_health_percent = networkHealthPercent,
            timestamp = DateTime.UtcNow,

            deploymentActive,
            nodeHeight,
            networkHeight = indexStatus.NetworkHeight,
            oracle = oracleInfo is null ? null : new { priceMicroUsd = oracleInfo.PriceMicroUsd, height = oracleInfo.Height },
            protection = new
            {
                errActive = protection.ErrActive,
                volatilityFreeze = protection.VolatilityFreeze,
                systemCollateralPercent = protection.SystemCollateralPercent,
            },
            indexer = new { height = indexStatus.LastIndexedHeight, lagBlocks = indexStatus.LagBlocks },
        });
    }

    // ==================== Address ====================

    private static async Task<IResult> GetNewAddress(DigiByteRpcClient rpc, DigiDollarConfig config, GetNewAddressRequest? req = null)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var label = req?.Label ?? "";
        var address = string.IsNullOrEmpty(label)
            ? await rpc.CallWalletAsync<string>(config.WalletName, "getdigidollaraddress")
            : await rpc.CallWalletAsync<string>(config.WalletName, "getdigidollaraddress", label);
        return Results.Ok(new { address });
    }

    private static async Task<IResult> ValidateAddress(DigiByteRpcClient rpc, DigiDollarConfig config, ValidateAddressRequest req)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var result = await rpc.CallAsync<JsonElement>("validateddaddress", req.Address);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListAddresses(DigiByteRpcClient rpc, DigiDollarConfig config,
        bool includeWatchonly = false, decimal minBalance = 0, bool includeEmpty = false)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var addresses = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "listdigidollaraddresses", includeWatchonly, minBalance, includeEmpty);
        return Results.Ok(addresses);
    }

    // ==================== Balance & UTXOs ====================

    private static async Task<IResult> GetBalance(DigiByteRpcClient rpc, DigiDollarConfig config, int minConfirmations = 1)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var balance = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "getdigidollarbalance", "", minConfirmations);
        return Results.Ok(balance);
    }

    private static async Task<IResult> GetAddressBalance(DigiByteRpcClient rpc, DigiDollarConfig config,
        string address, int minConfirmations = 1)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var balance = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "getdigidollarbalance", address, minConfirmations);
        return Results.Ok(balance);
    }

    private static async Task<IResult> ListUnspent(DigiByteRpcClient rpc, DigiDollarConfig config,
        int minConfirmations = 1, int maxConfirmations = 9999999)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var utxos = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "listdigidollarunspent", minConfirmations, maxConfirmations);
        return Results.Ok(utxos);
    }

    // ==================== Transactions ====================

    private static async Task<IResult> ListTransactions(DigiByteRpcClient rpc, DigiDollarConfig config,
        int count = 20, int skip = 0, string? address = null, string? category = null)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var txs = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "listdigidollartxs", count, skip, address ?? "", category ?? "");
        return Results.Ok(txs);
    }

    private static async Task<IResult> SendDigiDollar(DigiByteRpcClient rpc, DigiDollarConfig config, SendDdRequest req)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        if (string.IsNullOrWhiteSpace(req.Address))
            return Results.BadRequest(new { error = "address_required" });
        if (req.AmountCents <= 0)
            return Results.BadRequest(new { error = "amount_cents_must_be_positive" });

        // Pass integer cents so digibyted interprets as USD cents (not decimal dollars).
        var result = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "senddigidollar", req.Address, req.AmountCents, req.Comment ?? "");
        return Results.Ok(result);
    }

    private static async Task<IResult> SendManyDigiDollar(DigiByteRpcClient rpc, DigiDollarConfig config, SendManyDdRequest req)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        if (req.Recipients is null || req.Recipients.Count == 0)
            return Results.BadRequest(new { error = "recipients_required" });

        // sendmanydigidollar expects "" as the first (dummy) arg and a {addr: amountCents} object.
        var result = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "sendmanydigidollar", "", req.Recipients);
        return Results.Ok(result);
    }

    // ==================== Mint / Redeem ====================

    private static async Task<IResult> MintDigiDollar(DigiByteRpcClient rpc, DigiDollarConfig config, MintDdRequest req)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        if (req.AmountCents < 10_000)
            return Results.BadRequest(new { error = "amount_below_minimum", detail = "Minimum mint is $100 (10000 cents)" });
        if (req.AmountCents > 10_000_000)
            return Results.BadRequest(new { error = "amount_exceeds_maximum", detail = "Maximum mint is $100,000 (10000000 cents)" });
        if (req.Tier is < 0 or > 9)
            return Results.BadRequest(new { error = "invalid_tier", detail = "Tier must be 0-9" });

        var result = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "mintdigidollar", req.AmountCents, req.Tier);
        return Results.Ok(result);
    }

    private static async Task<IResult> RedeemDigiDollar(DigiByteRpcClient rpc, DigiDollarConfig config, RedeemDdRequest req)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        if (string.IsNullOrWhiteSpace(req.PositionId))
            return Results.BadRequest(new { error = "position_id_required" });
        if (req.AmountCents <= 0)
            return Results.BadRequest(new { error = "amount_cents_must_be_positive" });

        var result = string.IsNullOrEmpty(req.RedemptionAddress)
            ? await rpc.CallWalletAsync<JsonElement>(config.WalletName,
                "redeemdigidollar", req.PositionId, req.AmountCents)
            : await rpc.CallWalletAsync<JsonElement>(config.WalletName,
                "redeemdigidollar", req.PositionId, req.AmountCents, req.RedemptionAddress);
        return Results.Ok(result);
    }

    private static async Task<IResult> EstimateCollateral(DigiByteRpcClient rpc, DigiDollarConfig config,
        long amountCents, int tier)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        if (amountCents <= 0)
            return Results.BadRequest(new { error = "amount_cents_must_be_positive" });
        if (tier is < 0 or > 9)
            return Results.BadRequest(new { error = "invalid_tier" });

        var result = await rpc.CallAsync<JsonElement>("estimatecollateral", amountCents, tier);
        return Results.Ok(result);
    }

    // ==================== Positions ====================

    private static async Task<IResult> ListPositions(DigiByteRpcClient rpc, DigiDollarConfig config,
        bool activeOnly = false, int? tierFilter = null, long? minAmount = null, int count = 100, int skip = 0)
    {
        if (RequireEnabled(config) is { } gate) return gate;

        // Build args list matching digibyted parameter order.
        var args = new List<object> { activeOnly };
        if (tierFilter.HasValue) args.Add(tierFilter.Value);
        if (minAmount.HasValue) args.Add(minAmount.Value);
        args.Add(count);
        args.Add(skip);

        var positions = await rpc.CallWalletAsync<JsonElement>(config.WalletName,
            "listdigidollarpositions", args.ToArray());
        return Results.Ok(positions);
    }

    private static async Task<IResult> GetRedemptionInfo(DigiByteRpcClient rpc, DigiDollarConfig config,
        string positionId, long? amount = null)
    {
        if (RequireEnabled(config) is { } gate) return gate;
        var info = amount.HasValue
            ? await rpc.CallWalletAsync<JsonElement>(config.WalletName, "getredemptioninfo", positionId, amount.Value)
            : await rpc.CallWalletAsync<JsonElement>(config.WalletName, "getredemptioninfo", positionId);
        return Results.Ok(info);
    }

    // ==================== Request records ====================

    public record GetNewAddressRequest(string? Label);
    public record ValidateAddressRequest(string Address);
    public record SendDdRequest(string Address, long AmountCents, string? Comment = null);
    public record SendManyDdRequest(Dictionary<string, long> Recipients, string? Comment = null);
    public record MintDdRequest(long AmountCents, int Tier);
    public record RedeemDdRequest(string PositionId, long AmountCents, string? RedemptionAddress = null);
}
