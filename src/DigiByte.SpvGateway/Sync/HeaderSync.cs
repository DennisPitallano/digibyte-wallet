using System.Numerics;
using DigiByte.Crypto.Headers;

namespace DigiByte.SpvGateway.Sync;

/// <param name="Accepted">How many headers were stored.</param>
/// <param name="Duplicates">How many were already known.</param>
/// <param name="Rejected">The first header that was refused and why, or null when none was.</param>
/// <param name="ReorgDepth">How many best-chain headers were replaced when the best chain changed to another branch (0 if it only grew or stayed).</param>
/// <param name="TipHeight">The height of the best chain after the call.</param>
public sealed record IngestResult(int Accepted, int Duplicates, HeaderRejection? Rejected, int ReorgDepth, int TipHeight);

/// <summary>
/// Takes headers from any peer, checks each one against the branch it extends (linkage, algorithm, difficulty rule, median time and
/// proof of work), stores it with its cumulative work, and moves the best-chain mark to the branch with the most work. The gateway
/// never follows a chain whose proof of work it has not checked (spec FR-002).
/// </summary>
public sealed class HeaderSync
{
    /// <summary>How far back a branch is loaded to give the difficulty rule its window: 61 headers, plus room to find each algorithm's last block.</summary>
    private const int WindowHeaders = 400;

    private readonly HeaderStore _store;
    private readonly DigiByteConsensus _consensus;
    private readonly IPowHasher? _hasher;

    public HeaderSync(HeaderStore store, Checkpoint checkpoint, IPowHasher? hasher, DigiByteConsensus? consensus = null)
    {
        _store = store;
        _consensus = consensus ?? DigiByteConsensus.Mainnet;
        _hasher = hasher;
        _store.SeedCheckpoint(checkpoint);
    }

    public int TipHeight => _store.BestTip()?.Height ?? 0;

    /// <summary>Checks and stores the headers in order, then updates the best chain. Stops at the first header it refuses.</summary>
    public IngestResult Ingest(IEnumerable<BlockHeader80> headers)
    {
        var accepted = 0;
        var duplicates = 0;
        HeaderRejection? rejected = null;
        StoredHeader? highest = null;

        foreach (var header in headers)
        {
            if (_store.Get(header.Hash) is { } known)
            {
                duplicates++;
                highest = Better(highest, known);
                continue;
            }

            var parent = _store.Get(header.PrevHash);
            if (parent is null)
            {
                rejected = HeaderRejection.NotLinked;
                break;
            }

            var branch = _store.BranchEndingAt(parent.Header.Hash, WindowHeaders);
            var window = branch.Select(b => b.Header).ToList();
            var chain = new HeaderChain(new Checkpoint(branch[0].Height, window), _consensus, _hasher);
            var result = chain.TryAppend(header);
            if (!result.Accepted)
            {
                rejected = result.Reason;
                break;
            }

            var height = parent.Height + 1;
            var work = parent.ChainWork + chain.WorkSinceCheckpoint;
            _store.TryAdd(header, height, work);
            accepted++;
            highest = Better(highest, new StoredHeader(height, header, work, false));
        }

        var reorgDepth = 0;
        var best = _store.BestTip();
        if (highest is not null && best is not null && highest.ChainWork > best.ChainWork)
            reorgDepth = _store.SetBestTip(highest.Header.Hash);

        return new IngestResult(accepted, duplicates, rejected, reorgDepth, _store.BestTip()?.Height ?? 0);
    }

    private static StoredHeader Better(StoredHeader? current, StoredHeader candidate) =>
        current is null || candidate.ChainWork > current.ChainWork ? candidate : current;
}
