using DigiByte.NodeApi.Endpoints;
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

builder.Services.AddSingleton(nodeConfig);

// RPC client
builder.Services.AddHttpClient<DigiByteRpcClient>();
builder.Services.AddSingleton<DigiByteRpcClient>();

// IBlockchainService implementation via RPC
builder.Services.AddSingleton<NodeBlockchainService>();
builder.Services.AddSingleton<IBlockchainService>(sp => sp.GetRequiredService<NodeBlockchainService>());

// OpenAPI
builder.Services.AddOpenApi();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
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
    });
}

app.UseCors("AllowAll");

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

// Health check
app.MapGet("/api/health", async (DigiByteRpcClient rpc) =>
{
    var connected = await rpc.IsConnectedAsync();
    return Results.Ok(new
    {
        status = connected ? "connected" : "disconnected",
        network = nodeConfig.IsTestnet ? "testnet" : "mainnet",
        nodeUrl = nodeConfig.RpcUrl,
        timestamp = DateTime.UtcNow,
    });
}).WithName("HealthCheck").WithTags("System");

app.Run();
