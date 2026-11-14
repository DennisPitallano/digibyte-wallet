using DigiByte.Crypto.Headers;

namespace DigiByte.Wallet.Services;

/// <summary>Where the wallet reads headers from. The gateway client is the real one; tests use a scripted one.</summary>
public interface IGatewayHeaderFeed
{
    Task<GatewayHeaders> GetHeadersAsync(int from, int count, CancellationToken cancellationToken = default);
}

/// <summary>The gateway sent headers that fail the wallet own checks. The caller marks the source faulty and uses the next one.</summary>
public sealed class GatewayHeadersRejectedException(HeaderRejection reason) : Exception($"The gateway sent a header the wallet refused ({reason}).")
{
    public HeaderRejection Reason { get; } = reason;
}

/// <summary>What one sync did.</summary>
/// <param name="Appended">Headers newly stored.</param>
/// <param name="ReorgDepth">How many stored headers were replaced by a different branch, 0 when there was none.</param>
/// <param name="TipHeight">The wallet header tip afterwards.</param>
/// <param name="KeptOwnChain">The gateway offered a branch with less work than the wallet already had, so the wallet kept its own.</param>
public sealed record GatewayHeaderSyncResult(int Appended, int ReorgDepth, int TipHeight, bool KeptOwnChain);

/// <summary>
/// Brings the wallet header chain up to the gateway tip (spec FR-013, T067). Every header goes through <see cref="GatewayHeaderStore"/>,
/// so it is stored only after linkage, retarget and (once the wallet checks it) proof-of-work checks pass. When the gateway chain
/// parts from the wallet one, the wallet steps back until the chains meet, takes the gateway branch, and keeps it only if it has at
/// least as much work as the branch it replaced; otherwise it puts its own branch back.
/// </summary>
public sealed class GatewayHeaderSync(GatewayHeaderStore store, IGatewayHeaderFeed feed)
{
    public const int BatchSize = 2000;
    public const int MaxReorgDepth = 1000;

    public async Task<GatewayHeaderSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var appended = 0;
        var reorgDepth = 0;
        var keptOwn = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var from = store.TipHeight + 1;
            var batch = await feed.GetHeadersAsync(from, BatchSize, cancellationToken);
            if (batch.Headers.Count == 0)
                break;

            var result = await store.AppendRangeAsync(batch.Headers);
            appended += result.Accepted;

            if (result.Rejected is null)
            {
                if (batch.Headers.Count < BatchSize)
                    break;
                continue;
            }
            if (result.Rejected != HeaderRejection.NotLinked || result.Accepted > 0)
                throw new GatewayHeadersRejectedException(result.Rejected.Value);

            // The first header does not follow the wallet tip: the gateway is on another branch.
            var outcome = await FollowBranchAsync(cancellationToken);
            appended += outcome.Appended;
            reorgDepth = Math.Max(reorgDepth, outcome.Depth);
            keptOwn |= outcome.KeptOwn;
            if (outcome.KeptOwn)
                break;
        }
        return new GatewayHeaderSyncResult(appended, reorgDepth, store.TipHeight, keptOwn);
    }

    private async Task<(int Appended, int Depth, bool KeptOwn)> FollowBranchAsync(CancellationToken cancellationToken)
    {
        var originalTip = store.TipHeight;
        var oldWork = store.WorkSinceCheckpoint;
        var checkpointHeight = originalTip - store.Count;

        // Remember the branch that may be replaced, so it can be put back.
        var oldBranch = store.HeadersFrom(Math.Max(checkpointHeight + 1, originalTip - MaxReorgDepth + 1), MaxReorgDepth).ToList();
        var oldBranchStart = originalTip - oldBranch.Count + 1;

        var step = 6;
        var rewound = 0;
        while (true)
        {
            if (rewound >= MaxReorgDepth || store.TipHeight <= checkpointHeight)
                throw new GatewayHeadersRejectedException(HeaderRejection.NotLinked);
            var target = Math.Max(checkpointHeight, store.TipHeight - step);
            rewound += store.TipHeight - target;
            await store.RewindAsync(target);
            step *= 2;

            var batch = await feed.GetHeadersAsync(store.TipHeight + 1, BatchSize, cancellationToken);
            if (batch.Headers.Count == 0)
                continue;
            var attempt = await store.AppendRangeAsync(batch.Headers);
            if (attempt.Accepted == 0 && attempt.Rejected == HeaderRejection.NotLinked)
                continue; // still not meeting: step back further
            if (attempt.Rejected is not null && attempt.Rejected != HeaderRejection.NotLinked)
                throw new GatewayHeadersRejectedException(attempt.Rejected.Value);

            var added = attempt.Accepted;
            // Take the rest of the branch.
            while (attempt.Rejected is null && batch.Headers.Count == BatchSize)
            {
                batch = await feed.GetHeadersAsync(store.TipHeight + 1, BatchSize, cancellationToken);
                if (batch.Headers.Count == 0)
                    break;
                attempt = await store.AppendRangeAsync(batch.Headers);
                added += attempt.Accepted;
                if (attempt.Rejected is not null)
                    throw new GatewayHeadersRejectedException(attempt.Rejected.Value);
            }

            // The branches part after the last height where the wallet old header and the new one are the same header.
            var forkHeight = store.TipHeight - added;
            for (var height = forkHeight + 1; height <= originalTip && height <= store.TipHeight; height++)
            {
                var oldIndex = height - oldBranchStart;
                if (oldIndex < 0 || oldIndex >= oldBranch.Count || store.HeaderAt(height)?.Hash != oldBranch[oldIndex].Hash)
                    break;
                forkHeight = height;
            }
            var depth = originalTip - forkHeight;
            if (store.WorkSinceCheckpoint >= oldWork)
                return (added, Math.Max(0, depth), false);

            // The gateway branch has less work than the wallet own: keep the wallet own.
            await store.RewindAsync(forkHeight);
            var restore = oldBranch.Where((_, i) => oldBranchStart + i > forkHeight);
            await store.AppendRangeAsync(restore);
            return (0, 0, true);
        }
    }
}
