using DigiByte.NodeApi.RpcClient;
using NBitcoin;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary>
/// Background service that keeps the DigiDollar output index and mempool overlay current (research R3).
/// No-ops when DigiDollar is disabled. Each pass applies up to <see cref="DigiDollarConfig.IndexBlocksPerPass"/>
/// blocks, refreshes the mempool overlay, and publishes tip/lag to <see cref="DdIndexStatus"/>.
/// </summary>
public sealed class DdOutputIndexerService : BackgroundService
{
    /// <summary>DigiDollar activation height on mainnet.</summary>
    public const int MainnetActivationHeight = 23_869_440;

    private readonly DigiDollarConfig _config;
    private readonly NodeConfig _node;
    private readonly DdIndexStore _store;
    private readonly IDdChainSource _source;
    private readonly DdMempoolOverlay _mempool;
    private readonly DdIndexStatus _status;
    private readonly ILogger<DdOutputIndexerService> _logger;

    public DdOutputIndexerService(
        DigiDollarConfig config, NodeConfig node, DdIndexStore store, IDdChainSource source,
        DdMempoolOverlay mempool, DdIndexStatus status, ILogger<DdOutputIndexerService> logger)
    {
        _config = config;
        _node = node;
        _store = store;
        _source = source;
        _mempool = mempool;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("DdOutputIndexerService idle: DigiDollar not enabled.");
            return;
        }

        var network = _node.IsTestnet ? Crypto.Networks.DigiByteNetwork.Testnet : Crypto.Networks.DigiByteNetwork.Mainnet;
        var start = _config.IndexStartHeight ?? (_node.IsTestnet ? 0 : MainnetActivationHeight);
        var sync = new DdIndexSync(_store, _source, network, start);
        _logger.LogInformation("DdOutputIndexerService started (network={Network}, startHeight={Start})",
            _node.IsTestnet ? "testnet" : "mainnet", start);

        while (!stoppingToken.IsCancellationRequested)
        {
            var behind = false;
            try
            {
                var applied = await sync.SyncAsync(Math.Max(1, _config.IndexBlocksPerPass), stoppingToken);
                await _mempool.RefreshAsync(_source, stoppingToken);

                var tip = await _source.GetTipHeightAsync(stoppingToken);
                var net = await _source.GetNetworkHeightAsync(stoppingToken);
                _status.Update(tip, net, _store.GetState().LastHeight);
                behind = applied >= _config.IndexBlocksPerPass;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DD index pass failed; retrying after the poll interval.");
            }

            if (behind) continue; // still catching up: go straight to the next batch
            try { await Task.Delay(_config.IndexPollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
