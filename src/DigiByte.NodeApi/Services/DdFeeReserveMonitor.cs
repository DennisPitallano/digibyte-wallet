using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Services;

/// <summary>
/// Background service that polls the DigiDollar wallet's DGB balance and logs a warning
/// when the balance falls below <see cref="DigiDollarConfig.MinDgbFeeReserveDgb"/>.
///
/// DD transactions require DGB to pay miner fees (min 0.1 DGB per DD tx). If the hot wallet
/// runs dry, all DD sends fail. This monitor surfaces the condition to Railway logs so
/// external alerting (Railway → alert rule → email/Slack) can trigger.
///
/// The current reserve state is also exposed via GET /api/digidollar/health, which consumers
/// (wallet UI, DigiPay dashboard) can query directly.
///
/// Only runs when DigiDollarConfig.Enabled=true. No-ops otherwise.
/// </summary>
public class DdFeeReserveMonitor : BackgroundService
{
    private readonly DigiByteRpcClient _rpc;
    private readonly DigiDollarConfig _config;
    private readonly ILogger<DdFeeReserveMonitor> _logger;

    public DdFeeReserveMonitor(DigiByteRpcClient rpc, DigiDollarConfig config, ILogger<DdFeeReserveMonitor> logger)
    {
        _rpc = rpc;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("DdFeeReserveMonitor idle — DigiDollar not enabled.");
            return;
        }

        _logger.LogInformation(
            "DdFeeReserveMonitor started (wallet={Wallet}, minReserve={Min} DGB, interval={Interval}s)",
            _config.WalletName, _config.MinDgbFeeReserveDgb, _config.FeeReserveCheckInterval.TotalSeconds);

        // Small initial delay so the node has time to finish loading the wallet.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (TaskCanceledException) { return; }

        var lastLoggedUnhealthy = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var balance = await _rpc.CallWalletAsync<decimal>(_config.WalletName, "getbalance");
                var healthy = balance >= _config.MinDgbFeeReserveDgb;

                if (!healthy && !lastLoggedUnhealthy)
                {
                    _logger.LogWarning(
                        "DD fee reserve LOW: {Balance} DGB < {Min} DGB minimum. DD sends may fail. Top up wallet '{Wallet}'.",
                        balance, _config.MinDgbFeeReserveDgb, _config.WalletName);
                    lastLoggedUnhealthy = true;
                }
                else if (healthy && lastLoggedUnhealthy)
                {
                    _logger.LogInformation(
                        "DD fee reserve recovered: {Balance} DGB >= {Min} DGB minimum.",
                        balance, _config.MinDgbFeeReserveDgb);
                    lastLoggedUnhealthy = false;
                }
            }
            catch (RpcException ex)
            {
                _logger.LogWarning("DdFeeReserveMonitor RPC failed: [{Code}] {Message}", ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DdFeeReserveMonitor unexpected error");
            }

            try { await Task.Delay(_config.FeeReserveCheckInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
