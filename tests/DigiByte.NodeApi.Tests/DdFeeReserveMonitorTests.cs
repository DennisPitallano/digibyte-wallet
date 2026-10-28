using DigiByte.NodeApi.RpcClient;
using DigiByte.NodeApi.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T010: a read-only deployment (node without a wallet) leaves DigiDollar:WalletName blank; the fee reserve
/// monitor must then stay idle instead of polling a wallet that does not exist.
/// </summary>
public class DdFeeReserveMonitorTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new HttpRequestException("no node in tests");
        }
    }

    private static async Task<int> RunBriefly(DigiDollarConfig config)
    {
        var handler = new CountingHandler();
        var rpc = new DigiByteRpcClient(new HttpClient(handler), new NodeConfig(), NullLogger<DigiByteRpcClient>.Instance);
        var monitor = new DdFeeReserveMonitor(rpc, config, NullLogger<DdFeeReserveMonitor>.Instance);

        await monitor.StartAsync(CancellationToken.None);
        // The monitor waits 5 s before its first poll, so a short wait is enough to see an immediate exit.
        await Task.Delay(300);
        await monitor.StopAsync(CancellationToken.None);
        return handler.Calls;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankWalletName_StaysIdle_AndNeverCallsTheNode(string walletName)
    {
        var calls = await RunBriefly(new DigiDollarConfig { Enabled = true, WalletName = walletName });

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task DisabledDigiDollar_StaysIdle()
    {
        var calls = await RunBriefly(new DigiDollarConfig { Enabled = false, WalletName = "digipay-hot" });

        Assert.Equal(0, calls);
    }
}
