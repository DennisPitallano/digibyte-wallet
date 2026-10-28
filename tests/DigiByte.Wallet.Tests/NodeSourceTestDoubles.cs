using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Test doubles shared by the node-source tests (specs/005-wallet-own-node-source).</summary>
internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; private set; } = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

internal sealed class FakeHealthSource : INodeHealthSource
{
    public Func<CancellationToken, Task<NodeHealthResult>> Respond { get; set; } =
        _ => Task.FromResult(Ok());

    public int Calls;

    public Task<NodeHealthResult> GetAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Respond(cancellationToken);
    }

    public static NodeHealthResult Ok(int blocks = 1000) =>
        NodeReadiness.ParseHealth(200,
            $"{{\"status\":\"ok\",\"blocks\":{blocks},\"headers\":{blocks},\"lag\":0,\"peers\":8,\"txindex\":true,\"versionOk\":true}}");

    public static NodeHealthResult WithStatus(string status, int blocks = 1000) =>
        NodeReadiness.ParseHealth(200,
            $"{{\"status\":\"{status}\",\"blocks\":{blocks},\"headers\":{blocks},\"lag\":0,\"peers\":8,\"txindex\":true,\"versionOk\":true}}");

    public void Returns(NodeHealthResult result) => Respond = _ => Task.FromResult(result);
}

internal sealed class FakeExplorerHeightSource : IExplorerHeightSource
{
    public Func<CancellationToken, Task<int?>> Respond { get; set; } = _ => Task.FromResult<int?>(1000);

    public int Calls;

    public Task<int?> GetHeightAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Respond(cancellationToken);
    }

    public void Returns(int? height) => Respond = _ => Task.FromResult(height);
}

/// <summary>An explorer that only knows its block height; every other call is unsupported.</summary>
internal sealed class HeightOnlyExplorer(Func<Task<int>> height) : IBlockchainService
{
    public int Calls;

    public Task<int> GetBlockHeightAsync()
    {
        Interlocked.Increment(ref Calls);
        return height();
    }

    public Task<long> GetBalanceAsync(string address) => throw new NotSupportedException();
    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
    public Task<List<UtxoInfo>> GetUtxosAsync(string address) => throw new NotSupportedException();
    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new NotSupportedException();
    public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new NotSupportedException();
    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => throw new NotSupportedException();
    public Task<decimal> GetFeeRateAsync() => throw new NotSupportedException();
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();
}
