// DigiByte SPV gateway: a small light-client service that talks the DigiByte peer protocol and answers the wallet over HTTP
// (specs/011-p2p-spv-gateway). This file wires the parts that exist so far: options, the header store and sync, the rate limiter,
// the health report and the header endpoint. Scan, broadcast and the peer layer arrive in later tasks.

using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Headers.Hashers;
using DigiByte.SpvGateway;
using DigiByte.SpvGateway.Endpoints;
using DigiByte.SpvGateway.Broadcast;
using DigiByte.SpvGateway.Health;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Matching;
using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Sync;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT; bind to it for cloud deployment, like the other services.
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// The framework request log writes the full URL, and GET /v1/tx/{txid} puts a transaction id in it. The gateway logs nothing about
// what a client asks for (FR-021), so that log is raised to warnings only. Nothing else in this service logs request data.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result", LogLevel.Warning);

// Everything is built from the options when first needed, so a test host can change the configuration.
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return SpvGatewayOptions.FromConfiguration(key => configuration[key]);
});
builder.Services.AddSingleton(sp => new ClientLimiter(sp.GetRequiredService<SpvGatewayOptions>().RateLimitPerMinute));
// Peer layer: the book of known peers, the connector (real sockets) and the manager that keeps enough of them connected.
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<SpvGatewayOptions>();
    Directory.CreateDirectory(options.DataDirectory);
    return PeerBook.Open(Path.Combine(options.DataDirectory, "peers.db"));
});
builder.Services.AddSingleton(sp => new NBitcoinPeerConnector(
    sp.GetRequiredService<SpvGatewayOptions>().Network == GatewayNetwork.Testnet ? DigiByteNetworkSet.Instance.Testnet : DigiByteNetworkSet.Instance.Mainnet));
builder.Services.AddSingleton(sp => new PeerManager(
    sp.GetRequiredService<SpvGatewayOptions>(), sp.GetRequiredService<PeerBook>(),
    sp.GetRequiredService<NBitcoinPeerConnector>(), sp.GetRequiredService<NBitcoinPeerConnector>()));
builder.Services.AddSingleton<IPeerStatusSource>(sp => sp.GetRequiredService<PeerManager>());
builder.Services.AddSingleton(sp => new HeaderDownloader(
    sp.GetRequiredService<HeaderStore>(), sp.GetRequiredService<HeaderSync>(), sp.GetRequiredService<PeerManager>()));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<SpvGatewayOptions>();
    Directory.CreateDirectory(options.DataDirectory);
    var network = options.Network == GatewayNetwork.Testnet ? "testnet" : "mainnet";
    return BlockIndexStore.Open(Path.Combine(options.DataDirectory, $"index-{network}.db"));
});
builder.Services.AddSingleton(sp => new BlockFetcher(
    sp.GetRequiredService<PeerManager>(), sp.GetRequiredService<HeaderStore>(),
    sp.GetRequiredService<SpvGatewayOptions>().Network == GatewayNetwork.Testnet ? DigiByteNetworkSet.Instance.Testnet : DigiByteNetworkSet.Instance.Mainnet));
builder.Services.AddSingleton(sp => new BlockIndexer(
    sp.GetRequiredService<SpvGatewayOptions>(), sp.GetRequiredService<HeaderStore>(), sp.GetRequiredService<BlockIndexStore>(), sp.GetRequiredService<BlockFetcher>()));
builder.Services.AddSingleton<IBlockMatcher>(sp => new FullBlockMatcher(sp.GetRequiredService<BlockIndexStore>()));
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<ProofService>();
builder.Services.AddSingleton(sp => new BroadcastRelay(
    sp.GetRequiredService<PeerManager>(),
    sp.GetRequiredService<SpvGatewayOptions>().Network == GatewayNetwork.Testnet ? DigiByteNetworkSet.Instance.Testnet : DigiByteNetworkSet.Instance.Mainnet));
if (SpvGatewayOptions.FromConfiguration(key => builder.Configuration[key]).PeersEnabled)
    builder.Services.AddHostedService<PeerSyncService>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<SpvGatewayOptions>();
    Directory.CreateDirectory(options.DataDirectory);
    var network = options.Network == GatewayNetwork.Testnet ? "testnet" : "mainnet";
    return HeaderStore.Open(Path.Combine(options.DataDirectory, $"headers-{network}.db"), network);
});
builder.Services.AddSingleton(sp =>
{
    // Only mainnet has a built-in checkpoint so far; testnet needs one before it can sync (task T103).
    if (sp.GetRequiredService<SpvGatewayOptions>().Network == GatewayNetwork.Testnet)
        throw new InvalidOperationException("The testnet gateway needs a testnet checkpoint, which is not built in yet.");
    return new HeaderSync(sp.GetRequiredService<HeaderStore>(), Checkpoints.Mainnet, PowHashers.Available);
});

// The wallet runs in a browser on another origin, so the gateway answers cross-origin requests. It is anonymous and sets no cookies, so any
// origin may read it; credentials are never allowed. The extra request header lets a wallet pass the free ngrok browser warning page.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .WithMethods("GET", "POST", "OPTIONS")
    .WithHeaders("Content-Type", "ngrok-skip-browser-warning")
    .WithExposedHeaders("X-Network", "X-Tip-Height", "X-Tip-Hash", "Retry-After")
    .SetPreflightMaxAge(TimeSpan.FromHours(1))));

var app = builder.Build();

// Create the sync (which seeds the checkpoint into the store) before the first request.
app.Services.GetRequiredService<HeaderSync>();

app.UseCors();
app.UseMiddleware<GatewayMiddleware>();
app.MapHealth();
app.MapHeaders();
app.MapScan();
app.MapBroadcast();
app.MapProofs();

app.Run();

/// <summary>Marker so tests can host the app in-process.</summary>
public partial class Program;
