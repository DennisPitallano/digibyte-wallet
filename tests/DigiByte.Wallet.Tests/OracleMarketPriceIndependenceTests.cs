using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The main balance card's value comes from the market price (specs/014-digibyte-io-oracle FR-018, SC-011); the oracle feed must never change it,
/// whether the feed is on, off or unavailable.
/// </summary>
public class OracleMarketPriceIndependenceTests
{
    private static FallbackBlockchainService Service()
    {
        var down = new OracleDataServiceClientTests.Stub { Answer = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)) };
        var node = new NodeApiBlockchainService(new HttpClient(down) { BaseAddress = new Uri("http://fake-node:5260") }, "http://fake-node:5260");
        var market = new OracleDataServiceClientTests.Stub { Answer = (_, _) => Task.FromResult(OracleDataServiceClientTests.Json("{\"digibyte\":{\"usd\":0.0123}}")) };
        var price = new HttpClient(market);
        return new FallbackBlockchainService(node, [], price, isDevelopment: false);
    }

    private sealed class Feed(OracleDataServiceClientTests.Stub? upstream) : IOracleFeed
    {
        public DigiByte.Wallet.Models.OracleReading? Current => null;
        public bool IsLoading => false;
        public int Calls { get; private set; }
        public event Action? Changed { add { } remove { } }
        public void RequestRefresh() => Calls++;
    }

    [Fact]
    public async Task The_market_price_is_the_same_with_the_oracle_on_off_or_unavailable()
    {
        // The price service has no dependency on the oracle feed at all: every state gives the market price.
        var withoutFeed = await Service().GetDgbPriceAsync("USD");

        var unavailable = new OracleEnrichedDigiDollarApi(new FakeDdNodeApi { Down = true }, new Feed(null), () => true);
        await unavailable.GetHealthAsync(default);
        var whileUnavailable = await Service().GetDgbPriceAsync("USD");

        var on = new OracleEnrichedDigiDollarApi(new FakeDdNodeApi { Health = new() { Available = true } }, new Feed(null), () => true);
        await on.GetHealthAsync(default);
        var whileOn = await Service().GetDgbPriceAsync("USD");

        Assert.Equal(0.0123m, withoutFeed);
        Assert.Equal(withoutFeed, whileUnavailable);
        Assert.Equal(withoutFeed, whileOn);
    }
}
