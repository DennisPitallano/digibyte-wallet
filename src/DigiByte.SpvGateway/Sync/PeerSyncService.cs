using DigiByte.SpvGateway.Peers;

namespace DigiByte.SpvGateway.Sync;

/// <summary>Background loop: keep peers connected, bring the header chain level with them, then index the new blocks. Failures are logged and retried.</summary>
public sealed class PeerSyncService(PeerManager peers, HeaderDownloader downloader, BlockIndexer indexer, ILogger<PeerSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await peers.MaintainAsync(stoppingToken);
                var result = await downloader.DownloadAsync(stoppingToken);
                var indexed = await indexer.IndexAsync(stoppingToken);
                if (indexed > 0)
                    logger.LogInformation("Indexed {Count} block(s)", indexed);
                if (result.Accepted > 0 || result.PeersDropped > 0)
                    logger.LogInformation("Headers: {Accepted} new, tip {Tip}, {Dropped} peer(s) dropped", result.Accepted, result.TipHeight, result.PeersDropped);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Peer sync round failed; trying again");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
