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

// ── Deployment settings written when the web container starts (config.json) ──
// Only the OwnNode section is accepted, so the own-node switch, node address and thresholds change without a rebuild. A missing or
// invalid file means "no overrides" (node use stays off). See specs/005-wallet-own-node-source/contracts/runtime-config.md.
using (var configHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
    builder.Configuration.AddInMemoryCollection(await RuntimeConfigLoader.FetchAsync(configHttp));

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

// ── Our own node as a blockchain source (specs/005-wallet-own-node-source) ──
// Off by default. When on, the node is used only while its health report says ready (see NodeSourceMonitor); the node client
// then runs in strict mode so a failed request is an error, not a zero. The DigiDollar client and the usage counter below keep NodeApiUrl.
var nodeSource = NodeSourceOptions.FromConfiguration(key => builder.Configuration[key]);
var ownNodeApiUrl = nodeSource.ApiUrl ?? nodeApiUrl;
builder.Services.AddSingleton(nodeSource);
// One short probe, no retries: deliberately no resilience handler (a dead node must never become a slow probe).
builder.Services.AddHttpClient("NodeHealth");

builder.Services.AddScoped(sp =>
    new NodeApiBlockchainService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("NodeApi"), ownNodeApiUrl, strictErrors: nodeSource.Enabled));
builder.Services.AddScoped(sp =>
    new BlockchainApiService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Blockchain"),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("CoinGecko"),
        priceApiUrl,
        defaultIsTestnet));
if (isDevelopment)
    builder.Services.AddScoped<MockBlockchainService>();

// Register multiple explorer backends — tried in order, if one fails the next is used.
// USD exchange rates (via the price proxy) for display currencies CoinGecko does not price directly.
builder.Services.AddScoped(sp => new FiatRateProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("CoinGecko"), priceApiUrl));
builder.Services.AddScoped(sp =>
{
    var esplora = sp.GetRequiredService<BlockchainApiService>();
    var blockbookHttp = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Blockchain");
    var blockbook = new BlockbookApiService(blockbookHttp, "https://digibyteblockexplorer.com", "blockbook-main", priceApiUrl);

    return new ExplorerBackends(
    [
        esplora,   // Esplora (digiexplorer.info) — primary
        blockbook, // Blockbook (digibyteblockexplorer.com) — fallback
    ]);
});

// Node readiness: the health report plus an explorer height cross-check, judged by one monitor shared by the
// fallback service, the background loop and (later) the status UI.
builder.Services.AddScoped(sp => new NodeHealthHttpSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("NodeHealth"), nodeSource));
builder.Services.AddScoped(sp => new ExplorerHeightSource(sp.GetRequiredService<ExplorerBackends>().All, nodeSource.ExplorerBudget));
builder.Services.AddScoped(sp => new NodeSourceMonitor(
    nodeSource, sp.GetRequiredService<NodeHealthHttpSource>(), sp.GetRequiredService<ExplorerHeightSource>(), new SystemClock()));
builder.Services.AddScoped<INodeSourceState>(sp => sp.GetRequiredService<NodeSourceMonitor>());
builder.Services.AddScoped<INodeReadFailureSink>(sp => sp.GetRequiredService<NodeSourceMonitor>());
builder.Services.AddScoped<NodeSourceMonitorLoop>();
builder.Services.AddScoped<NodeSourceStatusService>();

builder.Services.AddScoped<FallbackBlockchainService>(sp =>
{
    var nodeApi = sp.GetRequiredService<NodeApiBlockchainService>();
    var explorers = sp.GetRequiredService<ExplorerBackends>().All;

    var priceHttp = sp.GetRequiredService<IHttpClientFactory>().CreateClient("CoinGecko");
    var mock = isDevelopment ? sp.GetRequiredService<MockBlockchainService>() : null;
    return new FallbackBlockchainService(nodeApi, explorers, priceHttp, isDevelopment, priceApiUrl, mock,
        sp.GetRequiredService<FiatRateProvider>(),
        sp.GetRequiredService<INodeSourceState>(), sp.GetRequiredService<INodeReadFailureSink>());
});
builder.Services.AddScoped<IBlockchainService>(sp => sp.GetRequiredService<FallbackBlockchainService>());

// ── DigiDollar (non-custodial, being rebuilt per specs/001-digidollar-wallet) ──
// Two independent switches (FR-022), both false by default: ship dark, then beta, then GA with no code change.
var ddView = builder.Configuration.GetValue<bool>("DigiDollar:ViewEnabled");
var ddSend = builder.Configuration.GetValue<bool>("DigiDollar:SendEnabled");
var ddOracle = builder.Configuration.GetValue<bool?>("DigiDollar:OracleEnabled") ?? true;
builder.Services.AddSingleton(new DigiDollarFeatureFlag(ddView, ddSend, ddOracle));
// Read-only NodeApi client (public chain data; never calls a wallet-scoped endpoint) and the encrypted local store.
// Backend: "nodeapi" (our own DigiDollar NodeApi: full oracle data, address-private) or "esplora" (a public explorer:
// balances, history and broadcast only, no oracle price or status, addresses are sent to the explorer).
var ddBackend = builder.Configuration["DigiDollar:Backend"] ?? "nodeapi";
var ddUseExplorer = string.Equals(ddBackend, "esplora", StringComparison.OrdinalIgnoreCase);
builder.Services.AddScoped<IDigiDollarNodeApi>(sp => ddUseExplorer
    ? new EsploraDigiDollarApi(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Blockchain"),
        builder.Configuration["DigiDollar:EsploraMainnetUrl"] ?? "https://digiexplorer.info/api",
        builder.Configuration["DigiDollar:EsploraTestnetUrl"] ?? "https://digiexplorer.info/testnet/api",
        () => sp.GetRequiredService<AppState>().NetworkMode != "mainnet")
    : new DigiDollarNodeApiClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("NodeApi"), nodeApiUrl));
builder.Services.AddScoped<DigiDollarStore>(sp => new DigiDollarStore(
    sp.GetRequiredService<ISecureStorage>(), sp.GetRequiredService<ICryptoService>()));
// WalletService doubles as the narrow session provider DigiDollar uses (it shares the derivation, never the mnemonic).
builder.Services.AddScoped<IDdSessionProvider>(sp => sp.GetRequiredService<WalletService>());
builder.Services.AddScoped<IDdFeeSource>(sp => sp.GetRequiredService<WalletService>());
builder.Services.AddScoped<IDigiDollarService, DigiDollarService>();
// Anonymous aggregate counts (SC-005): an empty POST naming an event, no identifiers. Only while the view switch is on.
// Only with our own NodeApi: the counter endpoint lives there, so the explorer backend sends nothing.
builder.Services.AddScoped(sp => new UsageCounter(new HttpClient(), nodeApiUrl, () => ddView && !ddUseExplorer));
builder.Services.AddScoped<DigiDollarViewModel>();

// ── App state, theme & UI services ──
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<BakuService>();
builder.Services.AddScoped<NfcService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<NetworkStatusService>();

var host = builder.Build();

// Initialize app state from localStorage (persisted preferences)
var appState = host.Services.GetRequiredService<AppState>();
await appState.InitializeAsync();

// Baku mascot preferences (presence mode, dismissed tips) from localStorage
await host.Services.GetRequiredService<BakuService>().InitializeAsync();

// Start network monitoring
var network = host.Services.GetRequiredService<NetworkStatusService>();
await network.InitializeAsync();

// Load the saved locale (and the English fallback) so localized strings, including DigiDollar's dd.* keys, resolve.
var localization = host.Services.GetRequiredService<LocalizationService>();
await localization.InitializeAsync();

// Start the own-node readiness checks in the background (does nothing unless OwnNode:Enabled is true).
host.Services.GetRequiredService<NodeSourceMonitorLoop>().Start();

await host.RunAsync();
