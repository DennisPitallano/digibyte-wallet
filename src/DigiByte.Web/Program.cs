using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DigiByte.Web;
using DigiByte.Web.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HTTP client for API calls
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress)
});

// Storage & Crypto
builder.Services.AddScoped<ISecureStorage, IndexedDbStorage>();
builder.Services.AddScoped<ICryptoService, JsCryptoService>();
builder.Services.AddScoped<WalletKeyStore>();
builder.Services.AddScoped<IKeyStore>(sp => sp.GetRequiredService<WalletKeyStore>());

// Wallet & services
builder.Services.AddScoped<TransactionTracker>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<PaymentRequestService>();

// Blockchain service chain: NodeApi → Esplora explorers → Mock demo data
var nodeApiUrl = builder.Configuration["NodeApiUrl"] ?? "http://localhost:5260";
builder.Services.AddScoped(sp => new NodeApiBlockchainService(sp.GetRequiredService<HttpClient>(), nodeApiUrl));
builder.Services.AddScoped<BlockchainApiService>();
builder.Services.AddScoped<MockBlockchainService>();
builder.Services.AddScoped<FallbackBlockchainService>();
builder.Services.AddScoped<IBlockchainService>(sp => sp.GetRequiredService<FallbackBlockchainService>());

// App state, theme & UI services
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<NfcService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddScoped<LocalizationService>();

await builder.Build().RunAsync();
