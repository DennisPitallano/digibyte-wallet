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
