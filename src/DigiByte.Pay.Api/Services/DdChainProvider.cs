using DigiByte.Wallet.Services;

namespace DigiByte.Pay.Api.Services;

/// <summary>The DigiDollar chain reader per network (specs/035 research R7): DD arrivals, confirmations and health.</summary>
public interface IDdChainProvider
{
    /// <summary>The reader for "mainnet" or "testnet" (anything else reads as testnet).</summary>
    IDigiDollarNodeApi For(string network);
}

/// <summary>
/// One <see cref="EsploraDigiDollarApi"/> per network over the "DigiPayDd" HttpClient, on the public explorer. One explorer
/// per network and no fallback (spec 035 FR-016). URLs come from <c>DigiPay:DigiDollar:EsploraMainnetUrl</c> /
/// <c>EsploraTestnetUrl</c>, or the <c>DIGIPAY_DD_ESPLORA_MAINNET_URL</c> / <c>_TESTNET_URL</c> environment variables.
/// </summary>
public sealed class DdChainProvider : IDdChainProvider
{
    public const string HttpClientName = "DigiPayDd";
    public const string DefaultMainnetUrl = "https://digiexplorer.info/api";
    public const string DefaultTestnetUrl = "https://digiexplorer.info/testnet/api";

    private readonly EsploraDigiDollarApi _mainnet;
    private readonly EsploraDigiDollarApi _testnet;

    public DdChainProvider(IHttpClientFactory http, IConfiguration config)
    {
        var mainnet = Environment.GetEnvironmentVariable("DIGIPAY_DD_ESPLORA_MAINNET_URL") is { Length: > 0 } m
            ? m : config["DigiPay:DigiDollar:EsploraMainnetUrl"] ?? DefaultMainnetUrl;
        var testnet = Environment.GetEnvironmentVariable("DIGIPAY_DD_ESPLORA_TESTNET_URL") is { Length: > 0 } t
            ? t : config["DigiPay:DigiDollar:EsploraTestnetUrl"] ?? DefaultTestnetUrl;
        // Each instance serves one network: GetHealthAsync and GetTxStatusAsync read the network from isTestnet.
        _mainnet = new EsploraDigiDollarApi(http.CreateClient(HttpClientName), mainnet, testnet, () => false);
        _testnet = new EsploraDigiDollarApi(http.CreateClient(HttpClientName), mainnet, testnet, () => true);
    }

    public IDigiDollarNodeApi For(string network) => network == "mainnet" ? _mainnet : _testnet;
}
