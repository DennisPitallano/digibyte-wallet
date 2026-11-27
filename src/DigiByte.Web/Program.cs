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

// Blockchain explorer HttpClient (Esplora — digiexplorer.info). The counting handler sits inside the retries, so the /dev/node
// benchmark sees every request that goes out; it counts nothing unless a benchmark run is open (spec 023, FR-010b).
builder.Services.AddSingleton<DigiByte.Web.Services.RequestCounter>();
builder.Services.AddTransient<DigiByte.Web.Services.RequestCountingHandler>();
var blockchainClient = builder.Services.AddHttpClient("Blockchain");
blockchainClient.AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromSeconds(1);
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
    });
blockchainClient.AddHttpMessageHandler<DigiByte.Web.Services.RequestCountingHandler>();

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
builder.Services.AddScoped<DigiIdSweepStore>();
builder.Services.AddScoped<WalletSyncStateStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IMigrationLock, WebLockMigrationLock>();
builder.Services.AddScoped<CommonPasswordList>();
builder.Services.AddScoped<ICommonPasswordList>(sp => sp.GetRequiredService<CommonPasswordList>());
// Change-only history sync (spec 023): saved address summaries and chain history, the refresh cycle, and the /dev/node switch.
builder.Services.AddSingleton<HistorySyncOptions>();
builder.Services.AddScoped(sp => new AddressSummaryStore(sp.GetRequiredService<ISecureStorage>()));
builder.Services.AddScoped(sp => new WalletHistorySync(sp.GetRequiredService<IBlockchainService>(), sp.GetRequiredService<AddressSummaryStore>()));
builder.Services.AddScoped<DigiByte.Web.Services.WalletSnapshotCache>();
// First-receive flow (spec 025): whether a wallet started empty, its one-time first-payment celebration, and the
// per-device "first session ended" marker the install banner and sponsors strip wait for.
builder.Services.AddScoped(sp => new FirstReceiveStore(sp.GetRequiredService<ISecureStorage>()));
builder.Services.AddScoped<DigiByte.Web.Services.FirstReceiveCoordinator>();
// "Search further" offer (spec 027): how each wallet came to be on this device and whether its offer was answered.
builder.Services.AddScoped(sp => new SearchOfferStore(sp.GetRequiredService<ISecureStorage>()));
builder.Services.AddScoped<DigiByte.Web.Services.SearchOfferCoordinator>();
builder.Services.AddScoped<DigiByte.Web.Services.InstallAppState>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<ContactService>();
// CSV export of transaction history (spec 031): built on the device from the complete history.
builder.Services.AddScoped<DigiByte.Wallet.Services.Export.TransactionExportService>();
builder.Services.AddScoped<IRequestAddressSource>(sp => sp.GetRequiredService<WalletService>());
builder.Services.AddScoped<PaymentRequestService>();
builder.Services.AddScoped<RequestWatcher>();
builder.Services.AddScoped<RequestAnnouncer>();
builder.Services.AddScoped<RequestAttentionState>();
builder.Services.AddScoped<PriceQuoteService>();
// Multisig follows the network chosen in Settings (mainnet / testnet / regtest), like the rest of the wallet.
builder.Services.AddScoped<MultisigWalletService>(sp =>
{
    var service = ActivatorUtilities.CreateInstance<MultisigWalletService>(sp);
    var app = sp.GetRequiredService<AppState>();
    service.NetworkModeSource = () => app.NetworkMode;
    return service;
});
builder.Services.AddScoped<DigiByte.Web.Services.ActiveWalletContext>();
// Shared-wallet menu and counts (specs/026-multisig-wallet-experience): one owner of "payments that need you",
// and the switch a full-screen page uses to hide the bottom menu.
builder.Services.AddScoped<DigiByte.Web.Services.MultisigAttentionState>();
builder.Services.AddScoped<DigiByte.Web.Services.ShellChrome>();
// One-shot, in-memory hand-off of a scanned proposal to the multisig it belongs to (specs/022-multisig-proposal-scan-import).
builder.Services.AddScoped<DigiByte.Web.Services.ProposalHandoff>();
builder.Services.AddScoped<DigiByte.Web.Services.PaymentHandoff>();
// Online multisig signing rooms (specs/017-multisig-online-signing): connects only when a room is opened or joined.
builder.Services.AddScoped<DigiByte.Web.Services.SigningRoomClient>();
builder.Services.AddScoped<BiometricService>();
builder.Services.AddScoped<SpendingLimitService>();
builder.Services.AddScoped<AddressLabelService>();
builder.Services.AddScoped<MarketSnapshotService>();
builder.Services.AddScoped<ClipboardService>();
builder.Services.AddScoped<SoundService>();
builder.Services.AddScoped<ShellActions>();

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

// Sponsors page: the list is read straight from the project's own GitHub branch (specs/006-sponsors-page). One plain request per page open,
// no retries (a failure shows the last good copy or a retry button), and nothing here touches wallet data.
var sponsorsBaseUrl = builder.Configuration["Sponsors:BaseUrl"]
    ?? "https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/data/";
builder.Services.AddHttpClient("SponsorsRead");
builder.Services.AddScoped(sp => new SponsorsService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("SponsorsRead"),
    sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
    sponsorsBaseUrl));

// The "add me to the list" form posts to the project's API (one endpoint, opens a pull request for a maintainer). Form fields only.
var sponsorsSubmitUrl = builder.Configuration["Sponsors:SubmitUrl"]
    ?? "https://digibyte-api-production.up.railway.app/api/sponsors/submissions";
builder.Services.AddHttpClient("SponsorsSubmit");
builder.Services.AddScoped(sp => new SponsorSubmissionClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("SponsorsSubmit"),
    sponsorsSubmitUrl));

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

    // digibyteblockexplorer.com (Blockbook) was removed on 2026-11-16: it had answered every request with 502 for a long time. The
    // Blockbook client (BlockbookApiService) is still in the code; add it back to this list if an instance comes back.
    return new ExplorerBackends(
    [
        esplora,   // Esplora (digiexplorer.info)
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

// ── The P2P light-client gateway as a blockchain source (specs/011-p2p-spv-gateway) ──
// Off by default (SpvGateway:Enabled). When on, it is used only while its own monitor says it is ready, never above the explorers
// until the wallet checks proof of work itself (see GatewayCapabilities), and every answer it gives is checked against headers the
// wallet verified. Mainnet only: the gateway has a mainnet checkpoint and no testnet one yet.
// A gateway address that starts with "/" is a path on the wallet's own server (nginx proxies it to the gateway over the private network),
// so it becomes an address on the wallet's own origin before it is read.
var spvGateway = P2pGatewayOptions.FromConfiguration(key =>
{
    var value = builder.Configuration[key];
    return key is "SpvGateway:ApiUrl" or "SpvGateway:ApiUrlAlt" && value is { } v && v.StartsWith('/') && !v.StartsWith("//")
        ? builder.HostEnvironment.BaseAddress.TrimEnd('/') + v
        : value;
});
var spvGatewayOn = spvGateway.Enabled && spvGateway.HasApiUrl;
builder.Services.AddSingleton(spvGateway);
builder.Services.AddHttpClient("SpvGateway");
builder.Services.AddHttpClient("SpvGatewayHealth");
builder.Services.AddScoped(sp => new GatewayRouteTimer(sp.GetRequiredService<IHttpClientFactory>().CreateClient("SpvGatewayHealth"), spvGateway));
builder.Services.AddScoped(sp => new GatewayClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("SpvGateway"), spvGateway));
builder.Services.AddScoped(sp => new GatewayHeaderStore(sp.GetRequiredService<ISecureStorage>(), DigiByte.Crypto.Headers.Checkpoints.Mainnet, "mainnet",
    // Proof of work is checked only when asked for (spec FR-013): the cost under WebAssembly is not measured yet, so the default is off.
    spvGateway.CheckProofOfWork ? DigiByte.Crypto.Headers.Hashers.PowHashers.Available : null));
builder.Services.AddScoped(sp => new GatewayHeaderSync(sp.GetRequiredService<GatewayHeaderStore>(), sp.GetRequiredService<GatewayClient>()));
builder.Services.AddScoped(sp => new GatewayProofChecker(sp.GetRequiredService<GatewayHeaderStore>()));
builder.Services.AddScoped(sp => new GatewayIncrementalSync(
    sp.GetRequiredService<GatewayClient>(), sp.GetRequiredService<GatewayHeaderStore>(), sp.GetRequiredService<GatewayProofChecker>(),
    sp.GetRequiredService<WalletSyncStateStore>()));
builder.Services.AddScoped<IGatewayWalletScopeProvider>(sp => new LazyGatewayScopeProvider(() => sp.GetRequiredService<WalletService>()));
// An independent view of the chain for the header cross-check: plain requests with a hard budget (the "Blockchain" client retries for up
// to 45 seconds, which a read must never wait for). Mainnet explorers only; the gateway is mainnet only.
builder.Services.AddHttpClient("BlockHash");
builder.Services.AddScoped<IExplorerBlockHashSource>(sp => new ExplorerBlockHashSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("BlockHash"),
    [new ExplorerHashEndpoint("https://digiexplorer.info/api", ExplorerHashKind.Esplora)]));
builder.Services.AddScoped(sp => new GatewayTipCrossCheck(sp.GetRequiredService<GatewayHeaderStore>(), sp.GetRequiredService<IExplorerBlockHashSource>()));
// Proof requests reveal transaction ids, so this upgrader is only ever started by the gateway source itself (the gateway is then the
// answering source). The background-proving switch for explorer-served wallets is not wired in the first release.
builder.Services.AddScoped(sp => new GatewayProofUpgrader(
    sp.GetRequiredService<GatewayClient>(), sp.GetRequiredService<GatewayProofChecker>(), sp.GetRequiredService<WalletSyncStateStore>(), () => spvGatewayOn));
builder.Services.AddScoped(sp => new P2pGatewayBlockchainService(
    sp.GetRequiredService<GatewayHeaderSync>(), sp.GetRequiredService<GatewayIncrementalSync>(), sp.GetRequiredService<WalletSyncStateStore>(),
    sp.GetRequiredService<GatewayHeaderStore>(), sp.GetRequiredService<IGatewayWalletScopeProvider>(), sp.GetRequiredService<GatewayClient>(),
    crossCheck: sp.GetRequiredService<GatewayTipCrossCheck>(),
    proofUpgrader: sp.GetRequiredService<GatewayProofUpgrader>()));
builder.Services.AddScoped(sp => new BirthHeightService(sp.GetRequiredService<ISecureStorage>(), sp.GetRequiredService<GatewayHeaderStore>()));
builder.Services.AddScoped(sp => new ScanProgressStore(sp.GetRequiredService<ISecureStorage>()));
builder.Services.AddScoped(sp => new GatewayRestoreScanner(
    sp.GetRequiredService<GatewayClient>(), sp.GetRequiredService<GatewayHeaderStore>(), sp.GetRequiredService<GatewayProofChecker>(),
    sp.GetRequiredService<ScanProgressStore>(), sp.GetRequiredService<GatewayIncrementalSync>(), sp.GetRequiredService<WalletSyncStateStore>()));
builder.Services.AddScoped(sp => new GatewayRestoreServices(
    sp.GetRequiredService<BirthHeightService>(), sp.GetRequiredService<GatewayRestoreScanner>(), sp.GetRequiredService<ScanProgressStore>()));
builder.Services.AddScoped(sp => new GatewayOwnSendRecorder(sp.GetRequiredService<WalletSyncStateStore>()));
builder.Services.AddScoped(sp => new GatewayStateBootstrap(sp.GetRequiredService<WalletSyncStateStore>(), sp.GetRequiredService<GatewayHeaderStore>()));
builder.Services.AddScoped(sp => new GatewayBootstrapCoordinator(
    sp.GetRequiredService<GatewayStateBootstrap>(), sp.GetRequiredService<FallbackBlockchainService>(), () => spvGatewayOn));
// Its own readiness monitor (the same rules as the node one): health report plus an explorer height cross-check.
builder.Services.AddKeyedScoped("gateway", (sp, _) => new NodeSourceMonitor(
    spvGateway.ToMonitorOptions(),
    new GatewayHealthHttpSource(sp.GetRequiredService<IHttpClientFactory>().CreateClient("SpvGatewayHealth"), spvGateway),
    sp.GetRequiredService<ExplorerHeightSource>(), new SystemClock()));
builder.Services.AddKeyedScoped("gateway", (sp, _) => new NodeSourceMonitorLoop(sp.GetRequiredKeyedService<NodeSourceMonitor>("gateway"), spvGateway.ToMonitorOptions()));

builder.Services.AddScoped<FallbackBlockchainService>(sp =>
{
    var nodeApi = sp.GetRequiredService<NodeApiBlockchainService>();
    var explorers = sp.GetRequiredService<ExplorerBackends>().All;

    var priceHttp = sp.GetRequiredService<IHttpClientFactory>().CreateClient("CoinGecko");
    var mock = isDevelopment ? sp.GetRequiredService<MockBlockchainService>() : null;
    return new FallbackBlockchainService(nodeApi, explorers, priceHttp, isDevelopment, priceApiUrl, mock,
        sp.GetRequiredService<FiatRateProvider>(),
        sp.GetRequiredService<INodeSourceState>(), sp.GetRequiredService<INodeReadFailureSink>(),
        gatewaySource: spvGatewayOn ? sp.GetRequiredService<P2pGatewayBlockchainService>() : null,
        gatewayState: spvGatewayOn ? sp.GetRequiredKeyedService<NodeSourceMonitor>("gateway") : null,
        gatewayFailureSink: spvGatewayOn ? sp.GetRequiredKeyedService<NodeSourceMonitor>("gateway") : null,
        gatewayPosition: spvGateway.Position);
});
builder.Services.AddScoped<IBlockchainService>(sp => sp.GetRequiredService<FallbackBlockchainService>());
builder.Services.AddScoped(sp => new NodeSourceStatusService(
    sp.GetRequiredService<INodeSourceState>(), sp.GetRequiredService<FallbackBlockchainService>(),
    spvGatewayOn ? sp.GetRequiredKeyedService<NodeSourceMonitor>("gateway") : null));

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
// ── Oracle price and protection status for the explorer backend (specs/014-digibyte-io-oracle) ──
// A public explorer has no oracle data, so the wallet reads it from the project's own data service, which relays digibyte.io: the browser never
// contacts digibyte.io itself, so the content policy is unchanged. One shared feed (it never makes a caller wait) is wrapped around the explorer
// backend only when the oracle switch is on; the address must be https (or loopback in development) and is not overridable from config.json.
// Display only: nothing here feeds a transaction.
var ddOracleUrl = builder.Configuration["DigiDollar:OracleApiUrl"];
var ddOracleOn = OracleDataServiceClient.IsEnabled(ddOracle, ddUseExplorer, ddOracleUrl);
builder.Services.AddHttpClient("Oracle");
builder.Services.AddScoped<IOracleFeed>(sp => new OracleDataServiceClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Oracle"), ddOracleOn ? ddOracleUrl : null));
builder.Services.AddScoped<IDigiDollarNodeApi>(sp =>
{
    if (!ddUseExplorer)
        return new DigiDollarNodeApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("NodeApi"), nodeApiUrl);

    var explorer = new EsploraDigiDollarApi(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Blockchain"),
        builder.Configuration["DigiDollar:EsploraMainnetUrl"] ?? "https://digiexplorer.info/api",
        builder.Configuration["DigiDollar:EsploraTestnetUrl"] ?? "https://digiexplorer.info/testnet/api",
        () => sp.GetRequiredService<AppState>().NetworkMode != "mainnet");
    return ddOracleOn
        ? new OracleEnrichedDigiDollarApi(explorer, sp.GetRequiredService<IOracleFeed>(), () => sp.GetRequiredService<AppState>().NetworkMode == "mainnet")
        : explorer;
});
builder.Services.AddScoped<DigiDollarStore>(sp => new DigiDollarStore(
    sp.GetRequiredService<ISecureStorage>(), sp.GetRequiredService<ICryptoService>()));
// WalletService doubles as the narrow session provider DigiDollar uses (it shares the derivation, never the mnemonic).
builder.Services.AddScoped<IDdSessionProvider>(sp => sp.GetRequiredService<WalletService>());
builder.Services.AddScoped<IDdFeeSource>(sp => sp.GetRequiredService<WalletService>());
// PSBT signing reaches keys only through this narrow provider, which WalletService implements.
builder.Services.AddScoped<IPsbtKeyProvider>(sp => sp.GetRequiredService<WalletService>());
builder.Services.AddScoped<PsbtSigningService>();
builder.Services.AddScoped<IDigiDollarService, DigiDollarService>();
// Anonymous aggregate counts (SC-005): an empty POST naming an event, no identifiers. Only while the view switch is on.
// Only with our own NodeApi: the counter endpoint lives there, so the explorer backend sends nothing.
builder.Services.AddScoped(sp => new UsageCounter(new HttpClient(), nodeApiUrl, () => ddView && !ddUseExplorer));
builder.Services.AddScoped<DigiDollarViewModel>();

// ── App state, theme & UI services ──
// Locking the app locks the wallet: the decrypted sessions and keys are dropped (resolved lazily, so AppState and WalletService
// never need each other to be built).
builder.Services.AddScoped<AppState>(sp => new AppState(
    sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
    sp.GetRequiredService<IConfiguration>(),
    () => sp.GetRequiredService<WalletService>()));
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

// The /dev/node switches (change-only history sync, tiered refresh). They are for developers, testing and benchmarks only: a saved
// value counts only while the developer view is on, so a value left on a device never changes a regular user's wallet (FR-013a).
try
{
    var storage = host.Services.GetRequiredService<ISecureStorage>();
    var saved = nodeSource.DevView
        ? HistorySyncOptions.FromStorage(true, await storage.GetAsync(HistorySyncOptions.StorageKey), await storage.GetAsync(HistorySyncOptions.TieredStorageKey))
        : HistorySyncOptions.FromStorage(false, null, null);
    var options = host.Services.GetRequiredService<HistorySyncOptions>();
    options.ChangeOnly = saved.ChangeOnly;
    options.Tiered = saved.Tiered;
}
catch (Exception ex)
{
    Console.WriteLine($"[HistorySync] Could not read the switches, keeping them on: {ex.GetType().Name}");
}

// Start network monitoring
var network = host.Services.GetRequiredService<NetworkStatusService>();
await network.InitializeAsync();

// Load the saved locale (and the English fallback) so localized strings, including DigiDollar's dd.* keys, resolve.
var localization = host.Services.GetRequiredService<LocalizationService>();
await localization.InitializeAsync();

// Start the own-node readiness checks in the background (does nothing unless OwnNode:Enabled is true).
host.Services.GetRequiredService<NodeSourceMonitorLoop>().Start();
// The gateway readiness checks, likewise (does nothing unless SpvGateway:Enabled is true and an address is set).
if (spvGatewayOn)
    host.Services.GetRequiredKeyedService<NodeSourceMonitorLoop>("gateway").Start();

// Watch payment requests for the whole app, from load, so a request paid while locked is noticed (spec 036).
host.Services.GetRequiredService<RequestAnnouncer>().Start();
await host.Services.GetRequiredService<RequestWatcher>().StartAsync();

await host.RunAsync();
