using DigiByte.NodeApi.DigiDollar;
using DigiByte.NodeApi.Endpoints;
using DigiByte.NodeApi.Metrics;
using DigiByte.NodeApi.Middleware;
using DigiByte.NodeApi.RpcClient;
using DigiByte.NodeApi.Services;
using DigiByte.Wallet.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT env var — bind to it for cloud deployment
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Configuration — appsettings.json with env var overrides (12-factor)
var nodeConfig = builder.Configuration.GetSection("DigiByteNode").Get<NodeConfig>() ?? new NodeConfig();

// Allow env var overrides for sensitive/deployment config
if (Environment.GetEnvironmentVariable("DIGIBYTE_RPC_USER") is { } rpcUser)
    nodeConfig.RpcUser = rpcUser;
if (Environment.GetEnvironmentVariable("DIGIBYTE_RPC_PASSWORD") is { } rpcPass)
    nodeConfig.RpcPassword = rpcPass;
if (Environment.GetEnvironmentVariable("DIGIBYTE_HOST") is { } host)
    nodeConfig.Host = host;
if (Environment.GetEnvironmentVariable("DIGIBYTE_TESTNET") is { } testnet)
    nodeConfig.IsTestnet = testnet.Equals("true", StringComparison.OrdinalIgnoreCase);

if (NodeConfigValidator.Validate(nodeConfig, builder.Environment.IsProduction()) is { } configError)
    throw new InvalidOperationException(configError);

builder.Services.AddSingleton(nodeConfig);

// DigiDollar configuration + env var overrides
var ddConfig = builder.Configuration.GetSection("DigiDollar").Get<DigiDollarConfig>() ?? new DigiDollarConfig();
if (Environment.GetEnvironmentVariable("DIGIDOLLAR_ENABLED") is { } ddEnabled)
    ddConfig.Enabled = ddEnabled.Equals("true", StringComparison.OrdinalIgnoreCase);
if (Environment.GetEnvironmentVariable("DIGIDOLLAR_WALLET") is { } ddWallet)
    ddConfig.WalletName = ddWallet;
if (Environment.GetEnvironmentVariable("NODEAPI_WRITE_KEY") is { } writeKey)
    ddConfig.WriteKey = writeKey;
if (Environment.GetEnvironmentVariable("NODEAPI_WALLET_ORIGINS") is { Length: > 0 } walletOrigins)
    ddConfig.WalletOrigins = walletOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (Environment.GetEnvironmentVariable("NODEAPI_METRICS_DB") is { Length: > 0 } metricsDb)
    ddConfig.MetricsDbPath = metricsDb;
builder.Services.AddSingleton(ddConfig);

// DigiDollar output index (wallet-facing, address-scoped reads): SQLite store + mempool overlay + RPC chain source.
// Only the indexer service touches the store, and only when DigiDollar is enabled.
var ddNetwork = nodeConfig.IsTestnet ? DigiByte.Crypto.Networks.DigiByteNetwork.Testnet : DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet;
if (Environment.GetEnvironmentVariable("DIGIDOLLAR_INDEX_DB") is { Length: > 0 } ddIndexDb)
    ddConfig.IndexDbPath = ddIndexDb;
builder.Services.AddSingleton<IDdChainSource, RpcDdChainSource>();
builder.Services.AddSingleton<DdIndexStatus>();
builder.Services.AddSingleton(new DdMempoolOverlay(ddNetwork));
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<DigiDollarConfig>();
    if (!cfg.Enabled) return DdIndexStore.InMemory();
    var dir = Path.GetDirectoryName(Path.GetFullPath(cfg.IndexDbPath));
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    return new DdIndexStore($"Data Source={cfg.IndexDbPath}");
});
builder.Services.AddSingleton<IDdIndex, DdIndex>();
builder.Services.AddHostedService<DdOutputIndexerService>();

// Anonymous aggregate usage counters (research R17): per-day counts only, no identifiers.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MetricsRateLimiter>();
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<DigiDollarConfig>();
    var dir = Path.GetDirectoryName(Path.GetFullPath(cfg.MetricsDbPath));
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    return new MetricsStore($"Data Source={cfg.MetricsDbPath}");
});

// RPC client
builder.Services.AddHttpClient<DigiByteRpcClient>();
builder.Services.AddSingleton<DigiByteRpcClient>();

// Health model for /api/health. DGB_RECOMMENDED_VERSION (optional) is the node version this deployment expects.
builder.Services.AddSingleton(sp =>
{
    var rpc = sp.GetRequiredService<DigiByteRpcClient>();
    return new NodeHealthService(
        method => rpc.CallAsync<System.Text.Json.JsonElement>(method),
        Environment.GetEnvironmentVariable("DGB_RECOMMENDED_VERSION"));
});

// IBlockchainService implementation via RPC
builder.Services.AddSingleton<NodeBlockchainService>();
builder.Services.AddSingleton<IBlockchainService>(sp => sp.GetRequiredService<NodeBlockchainService>());

// DigiDollar fee reserve monitor (BackgroundService — no-ops when DD disabled)
builder.Services.AddHostedService<DdFeeReserveMonitor>();

// OpenAPI
builder.Services.AddOpenApi();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
    // Wallet-scoped DigiDollar routes (DigiPay only, server to server): no origin is allowed, so a
    // browser on any origin (including the wallet's) cannot read or call them cross-origin.
    options.AddPolicy("DdWalletScoped", policy => policy.SetIsOriginAllowed(_ => false));
    // Usage counters answer the wallet's own origin only (configured via DigiDollar:WalletOrigins / NODEAPI_WALLET_ORIGINS).
    options.AddMetricsCorsPolicy(ddConfig.WalletOrigins);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "DigiByte Node API";
        options.Theme = ScalarTheme.BluePlanet;
        options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
        options.Favicon = "https://dgbwallet.app/favicon.svg";
    });
}

app.UseCors("AllowAll");

// Guard wallet-scoped DigiDollar routes (GET and POST) with X-NodeApi-Key; fails closed if unset (outside Development).
app.UseMiddleware<WriteKeyAuthMiddleware>();

// Node-control, key, wallet, mining and other costly routes need the same key outside Development (see OperatorRouteGate).
app.UseMiddleware<OperatorRouteGate>();

// Global RPC error handler
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (RpcException ex)
    {
        context.Response.StatusCode = ex.Code == -1 ? 503 : 400;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message, code = ex.Code });
    }
});

// ===== Tier 1 Endpoints =====
app.MapGroup("/api/blockchain").MapBlockchainEndpoints().WithTags("Blockchain");
app.MapGroup("/api/address").MapAddressEndpoints().WithTags("Address");
app.MapGroup("/api/tx").MapTransactionEndpoints().WithTags("Transaction");
app.MapGroup("/api/wallet").MapWalletEndpoints().WithTags("Wallet");
app.MapGroup("/api/network").MapNetworkEndpoints().WithTags("Network");
app.MapGroup("/api/faucet").MapFaucetEndpoints().WithTags("Faucet");

// ===== Tier 2 Endpoints =====
app.MapGroup("/api/mining").MapMiningEndpoints().WithTags("Mining");
app.MapGroup("/api/keys").MapKeyEndpoints().WithTags("Key Management");
app.MapGroup("/api/psbt").MapPsbtEndpoints().WithTags("PSBT");
app.MapGroup("/api/descriptor").MapDescriptorEndpoints().WithTags("Descriptors");
app.MapGroup("/api/util").MapUtilityEndpoints().WithTags("Utility");

// ===== Anonymous usage counters (SC-005) =====
app.MapGroup("/api/metrics").MapMetricsEndpoints().WithTags("Metrics");

// ===== DigiDollar Endpoints (feature-flagged via DigiDollarConfig.Enabled) =====
app.MapGroup("/api/digidollar").MapDigiDollarEndpoints().WithTags("DigiDollar");

// Health check (contract: specs/004-railway-digibyte-node/contracts/nodeapi-health.md).
// 200 while the node is starting, syncing or ok; 503 only when the node cannot be reached.
app.MapGet("/api/health", async (NodeHealthService health) =>
{
    var h = await health.GetAsync();
    return Results.Json(new
    {
        status = h.Status,
        network = nodeConfig.IsTestnet ? "testnet" : "mainnet",
        nodeVersion = h.NodeVersion,
        blocks = h.Blocks,
        headers = h.Headers,
        lag = h.Lag,
        peers = h.Peers,
        txindex = h.Txindex,
        recommendedVersion = h.RecommendedVersion,
        versionOk = h.VersionOk,
        timestamp = DateTime.UtcNow,
    }, statusCode: h.HttpStatus);
}).WithName("HealthCheck").WithTags("System");

app.Run();
