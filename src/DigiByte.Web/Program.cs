using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience;
using DigiByte.Web;
using DigiByte.Web.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ── Logging: suppress noisy HTTP / Polly info logs in production ──
if (!builder.HostEnvironment.IsDevelopment())
{
    builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
    builder.Logging.AddFilter("Polly", LogLevel.Warning);
}

// ── HTTP clients with resilience (Polly: retry + circuit breaker + timeout) ──
var baseAddress = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;

// Default HttpClient (general purpose — analytics, localization, etc.)
builder.Services.AddHttpClient("Default", client =>
    client.BaseAddress = new Uri(baseAddress))
    .AddStandardResilienceHandler();
// Keep a scoped HttpClient for existing consumers that inject HttpClient directly
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Default"));

// Blockchain explorer HttpClient (Esplora — digiexplorer.info)
builder.Services.AddHttpClient("Blockchain")
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromSeconds(1);
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
    });

// CoinGecko price HttpClient — no retries to avoid 429 storms
builder.Services.AddHttpClient("CoinGecko");

// Node API HttpClient (own pruned node)
builder.Services.AddHttpClient("NodeApi")
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts = 2;
        o.Retry.Delay = TimeSpan.FromMilliseconds(500);
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    });

// ── Storage & Crypto ──
builder.Services.AddScoped<ISecureStorage, IndexedDbStorage>();
builder.Services.AddScoped<ICryptoService, JsCryptoService>();
builder.Services.AddScoped<WalletKeyStore>();
builder.Services.AddScoped<IKeyStore>(sp => sp.GetRequiredService<WalletKeyStore>());

// ── In-memory cache (session-scoped, TTL + dedup) ──
builder.Services.AddScoped<MemoryCacheService>();

// ── Wallet & services ──
builder.Services.AddScoped<TransactionTracker>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<PaymentRequestService>();
builder.Services.AddScoped<MultisigWalletService>();
builder.Services.AddScoped<BiometricService>();
builder.Services.AddScoped<SpendingLimitService>();
builder.Services.AddScoped<AddressLabelService>();

// ── Blockchain service chain: Own Node (pruned) → Explorer list (with fallback) ──
// Mock demo data is only available in Development — production throws if all backends fail.
var nodeApiUrl = builder.Configuration["NodeApiUrl"] ?? "http://localhost:5260";
var priceApiUrl = builder.Configuration["PriceApiUrl"];
var defaultNetwork = builder.Configuration["DefaultNetwork"] ?? "mainnet";
var defaultIsTestnet = defaultNetwork != "mainnet";
var isDevelopment = builder.HostEnvironment.IsDevelopment();

builder.Services.AddScoped(sp =>
    new NodeApiBlockchainService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("NodeApi"), nodeApiUrl));
builder.Services.AddScoped(sp =>
    new BlockchainApiService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Blockchain"),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("CoinGecko"),
        priceApiUrl,
        defaultIsTestnet));
if (isDevelopment)
    builder.Services.AddScoped<MockBlockchainService>();

// Register multiple explorer backends — tried in order, if one fails the next is used.
builder.Services.AddScoped<FallbackBlockchainService>(sp =>
{
    var nodeApi = sp.GetRequiredService<NodeApiBlockchainService>();
    var esplora = sp.GetRequiredService<BlockchainApiService>();
    var blockbookHttp = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Blockchain");
    var blockbook = new BlockbookApiService(blockbookHttp, "https://digibyteblockexplorer.com", "blockbook-main", priceApiUrl);

    var explorers = new List<IBlockchainService>
    {
        esplora,   // Esplora (digiexplorer.info) — primary
        blockbook, // Blockbook (digibyteblockexplorer.com) — fallback
    };

    var priceHttp = sp.GetRequiredService<IHttpClientFactory>().CreateClient("CoinGecko");
    var mock = isDevelopment ? sp.GetRequiredService<MockBlockchainService>() : null;
    return new FallbackBlockchainService(nodeApi, explorers, priceHttp, isDevelopment, priceApiUrl, mock);
});
builder.Services.AddScoped<IBlockchainService>(sp => sp.GetRequiredService<FallbackBlockchainService>());

// ── App state, theme & UI services ──
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<NfcService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<NetworkStatusService>();

var host = builder.Build();

// Initialize app state from localStorage (persisted preferences)
var appState = host.Services.GetRequiredService<AppState>();
await appState.InitializeAsync();

// Start network monitoring
var network = host.Services.GetRequiredService<NetworkStatusService>();
await network.InitializeAsync();

await host.RunAsync();
