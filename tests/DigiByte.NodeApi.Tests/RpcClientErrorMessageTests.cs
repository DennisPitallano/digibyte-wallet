using DigiByte.NodeApi.RpcClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T037: the RPC error message is returned to API clients by the global error handler, so a failed connection
/// must not reveal the node's internal host name or port.
/// </summary>
public class RpcClientErrorMessageTests
{
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known (digibyted.internal.example:14022)");
    }

    private static DigiByteRpcClient Client() =>
        new(new HttpClient(new FailingHandler()),
            new NodeConfig { Host = "digibyted.internal.example", MainnetPort = 14022, IsTestnet = false, RpcUser = "u", RpcPassword = "p" },
            NullLogger<DigiByteRpcClient>.Instance);

    [Fact]
    public async Task FailedConnection_MessageNamesNoHostOrPort()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => Client().CallAsync<int>("getblockcount"));

        Assert.Equal(-1, ex.Code);
        Assert.DoesNotContain("internal.example", ex.Message);
        Assert.DoesNotContain("14022", ex.Message);
    }

    [Fact]
    public async Task FailedWalletConnection_MessageNamesNoHostOrPort()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => Client().CallWalletAsync<int>("w", "getbalance"));

        Assert.Equal(-1, ex.Code);
        Assert.DoesNotContain("internal.example", ex.Message);
        Assert.DoesNotContain("14022", ex.Message);
    }
}
