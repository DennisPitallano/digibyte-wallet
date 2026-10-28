using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// Runs <see cref="NodeSourceMonitor.CheckAsync"/> in the background, once per check interval, from app start
/// (specs/005-wallet-own-node-source). Checks never overlap, never throw, and nothing runs when node use is off.
/// </summary>
public sealed class NodeSourceMonitorLoop : IAsyncDisposable
{
    private readonly NodeSourceMonitor _monitor;
    private readonly NodeSourceOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public NodeSourceMonitorLoop(NodeSourceMonitor monitor, NodeSourceOptions options)
    {
        _monitor = monitor;
        _options = options;
    }

    /// <summary>Starts the loop once. Does nothing when node use is off, or when it is already running.</summary>
    public void Start()
    {
        if (!_options.Enabled || _loop is not null)
            return;
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // The first check runs at once; later ones follow the interval. A slow check delays the next tick, it never overlaps it.
            await _monitor.CheckAsync(ct);
            using var timer = new PeriodicTimer(_options.CheckInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await _monitor.CheckAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
        catch (Exception)
        {
            // the monitor does not throw; this is only a last guard so the background task can never fault the app
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (Exception) { /* already handled */ }
        }
        _cts.Dispose();
    }
}
