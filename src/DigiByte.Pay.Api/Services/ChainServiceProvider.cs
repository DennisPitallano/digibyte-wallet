using DigiByte.Wallet.Services;

namespace DigiByte.Pay.Api.Services;

/// <summary>The DGB chain reader per network, for <see cref="InvoiceMonitor"/>. Replaceable in tests.</summary>
public interface IChainServiceProvider
{
    IBlockchainService For(string network);
}

/// <summary>One explorer-backed <see cref="BlockchainApiService"/> per network, over the "DigiPayChain" HttpClient.</summary>
public sealed class ChainServiceProvider(IHttpClientFactory http) : IChainServiceProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IBlockchainService> _byNetwork = new();

    public IBlockchainService For(string network) => _byNetwork.GetOrAdd(network, n =>
        new BlockchainApiService(http.CreateClient("DigiPayChain"), isTestnet: n is "testnet" or "regtest"));
}
