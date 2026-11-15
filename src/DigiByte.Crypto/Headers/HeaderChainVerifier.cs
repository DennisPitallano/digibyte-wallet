using System.Numerics;
using NBitcoin;

namespace DigiByte.Crypto.Headers;

/// <summary>Why a header was refused.</summary>
public enum HeaderRejection
{
    None = 0,
    /// <summary>The header's previous hash is not the current tip.</summary>
    NotLinked,
    /// <summary>The version bits do not name an algorithm DigiByte defines.</summary>
    UnknownAlgorithm,
    /// <summary>The algorithm is known but may not mine at this height (for example Groestl after it was switched off).</summary>
    AlgorithmNotAllowed,
    /// <summary>The bits differ from what the difficulty rule requires.</summary>
    WrongDifficulty,
    /// <summary>The header's time is not after the median of the previous eleven.</summary>
    TimeNotAfterMedian,
    /// <summary>The proof-of-work hash is above the target.</summary>
    ProofOfWorkFailed,
    /// <summary>The headers held do not reach back far enough to compute the difficulty.</summary>
    WindowTooShort,
}

/// <param name="Accepted">True when the header was appended.</param>
/// <param name="Reason">Why it was refused, or <see cref="HeaderRejection.None"/>.</param>
/// <param name="ProofOfWorkChecked">True only when a hasher computed this header's proof-of-work hash and it met the target.
/// An accepted header with this false has passed every other check but its work has not been verified.</param>
public readonly record struct AppendResult(bool Accepted, HeaderRejection Reason, bool ProofOfWorkChecked);

/// <summary>
/// A header chain that starts at a built-in checkpoint and only grows by headers that link, use an allowed algorithm, carry exactly the
/// difficulty DigiByte's V4 rule requires, move time forward, and (when a hasher is supplied) meet their proof-of-work target.
/// Not thread safe. A rejected header leaves the chain unchanged.
/// </summary>
public sealed class HeaderChain
{
    private readonly List<BlockHeader80> _headers;
    // One entry per header appended after the checkpoint, so a reorganization can take them back off.
    private readonly List<BigInteger> _work = [];
    private readonly List<bool> _workUnchecked = [];
    private readonly DigiByteConsensus _consensus;
    private readonly IPowHasher? _hasher;
    private readonly int _firstHeight;

    public HeaderChain(Checkpoint checkpoint, DigiByteConsensus consensus, IPowHasher? hasher = null)
    {
        _headers = checkpoint.Headers.ToList();
        _firstHeight = checkpoint.FirstHeight;
        _consensus = consensus;
        _hasher = hasher;
        CheckpointHeight = checkpoint.Height;
        UncheckedProofOfWorkCount = 0;
    }

    public int CheckpointHeight { get; }

    public int TipHeight => _firstHeight + _headers.Count - 1;

    public uint256 TipHash => _headers[^1].Hash;

    /// <summary>
    /// The work added by the headers appended since the checkpoint, counted the way DigiByte Core counts block work (see <see cref="BlockWork"/>).
    /// Only meaningful for comparing chains that start at the same checkpoint.
    /// </summary>
    public BigInteger WorkSinceCheckpoint { get; private set; }

    /// <summary>How many appended headers passed every check except proof of work, because no hasher could compute theirs.</summary>
    public int UncheckedProofOfWorkCount { get; private set; }

    /// <summary>Headers from the checkpoint, oldest first. A copy.</summary>
    public IReadOnlyList<BlockHeader80> Headers => _headers.ToList();

    public AppendResult TryAppend(BlockHeader80 header)
    {
        if (header.PrevHash != TipHash)
            return Reject(HeaderRejection.NotLinked);

        var height = TipHeight + 1;
        var algorithm = header.Algorithm;
        if (algorithm == PowAlgorithm.Unknown)
            return Reject(HeaderRejection.UnknownAlgorithm);
        if (!_consensus.IsAlgorithmAllowed(algorithm, height))
            return Reject(HeaderRejection.AlgorithmNotAllowed);

        if (!DigiByteRetarget.TryNextRequiredBits(_headers, _firstHeight, algorithm, _consensus, out var expectedBits))
            return Reject(HeaderRejection.WindowTooShort);
        if (header.Bits != expectedBits)
            return Reject(HeaderRejection.WrongDifficulty);

        if (header.Time <= DigiByteRetarget.MedianTimePast(_headers, _headers.Count - 1, _consensus.MedianTimeSpan))
            return Reject(HeaderRejection.TimeNotAfterMedian);

        var workChecked = false;
        if (_hasher is not null && _hasher.Supports(algorithm))
        {
            if (!CompactTarget.MeetsTarget(_hasher.Hash(header), header.Bits, _consensus.PowLimit))
                return Reject(HeaderRejection.ProofOfWorkFailed);
            workChecked = true;
        }

        // The block's work is computed from the state before it is added: every algorithm's next target, given the parent.
        if (!BlockWork.TryProofOfNextBlock(_headers, _firstHeight, _consensus, out var blockWork))
            return Reject(HeaderRejection.WindowTooShort);

        _headers.Add(header);
        _work.Add(blockWork);
        _workUnchecked.Add(!workChecked);
        WorkSinceCheckpoint += blockWork;
        if (!workChecked)
            UncheckedProofOfWorkCount++;
        return new AppendResult(true, HeaderRejection.None, workChecked);
    }

    /// <summary>
    /// Takes headers back off the tip until the chain ends at <paramref name="height"/>, for a reorganization. The checkpoint itself can
    /// never be removed.
    /// </summary>
    public void TruncateTo(int height)
    {
        if (height < CheckpointHeight || height > TipHeight)
            throw new ArgumentOutOfRangeException(nameof(height), $"The height must be between the checkpoint ({CheckpointHeight}) and the tip ({TipHeight}).");

        var remove = TipHeight - height;
        for (var i = 0; i < remove; i++)
        {
            var last = _work.Count - 1;
            WorkSinceCheckpoint -= _work[last];
            if (_workUnchecked[last])
                UncheckedProofOfWorkCount--;
            _work.RemoveAt(last);
            _workUnchecked.RemoveAt(last);
            _headers.RemoveAt(_headers.Count - 1);
        }
    }

    private static AppendResult Reject(HeaderRejection reason) => new(false, reason, false);
}
