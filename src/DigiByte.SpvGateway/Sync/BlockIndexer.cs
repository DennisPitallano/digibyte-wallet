using DigiByte.SpvGateway.Index;

namespace DigiByte.SpvGateway.Sync;

/// <summary>
/// Keeps the match index level with the header chain (spec FR-003, T062): drops blocks that left the best chain and blocks older than
/// the scan window, then fetches, checks and indexes the blocks that are missing, oldest first, a limited number per round. Blocks
/// before the earliest stored header cannot be checked, so the index never starts before the header store does (and not before the
/// optional floor height an operator or test may set).
/// </summary>
public sealed class BlockIndexer(SpvGatewayOptions options, HeaderStore headers, BlockIndexStore index, BlockFetcher fetcher, TimeProvider? time = null, int floorHeight = 0)
{
    /// <summary>DigiByte makes a block about every 15 seconds.</summary>
    public const int BlocksPerDay = 5760;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The first height the window needs, given the tip.</summary>
    public int WindowStart(int tipHeight, int earliestHeader) =>
        Math.Max(Math.Max(earliestHeader + 1, floorHeight), tipHeight - options.ScanWindowDays * BlocksPerDay + 1);

    /// <summary>Indexes up to <paramref name="maxBlocks"/> missing blocks. Returns how many were added.</summary>
    public async Task<int> IndexAsync(CancellationToken cancellationToken, int maxBlocks = 500)
    {
        var tip = headers.BestTip()?.Height;
        var earliest = headers.EarliestHeight();
        if (tip is null || earliest is null)
            return 0;

        index.RemoveNotOnBestChain(headers);
        var start = WindowStart(tip.Value, earliest.Value);
        index.PruneBelow(start);

        var next = (index.IndexedToHeight() ?? (start - 1)) + 1;
        var added = 0;
        var parallel = Math.Max(1, options.IndexFetchParallelism);
        var first = Math.Max(next, start);
        var last = Math.Min(tip.Value, first + maxBlocks - 1);

        // A sliding window: up to <parallel> blocks are on the way at once (spread over the peers), and each is added the moment it and all
        // the blocks before it have arrived, so one slow peer delays only the blocks behind it and the index stays contiguous.
        var pending = new Queue<(int Height, Task<NBitcoin.Block?> Block)>();
        var nextToRequest = first;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            while (!stop.IsCancellationRequested && (pending.Count > 0 || nextToRequest <= last))
            {
                while (pending.Count < parallel && nextToRequest <= last)
                {
                    var height = nextToRequest++;
                    pending.Enqueue((height, fetcher.FetchAsync(height, stop.Token)));
                }
                var (headHeight, headTask) = pending.Dequeue();
                var block = await headTask;
                if (block is null)
                    break; // no peer could supply it right now; the next round tries again from here
                index.Add(BlockIndexBuilder.Build(block, headHeight), _time.GetUtcNow());
                added++;
            }
        }
        finally
        {
            // Whatever is still in flight is no longer needed.
            await stop.CancelAsync();
            foreach (var (_, task) in pending)
            {
                try { await task; } catch (OperationCanceledException) { }
            }
        }
        return added;
    }
}
