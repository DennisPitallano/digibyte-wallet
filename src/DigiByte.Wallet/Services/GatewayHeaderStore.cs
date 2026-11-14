using System.Numerics;
using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>The outcome of appending a batch of headers.</summary>
/// <param name="Accepted">How many headers were taken.</param>
/// <param name="Rejected">Why the first refused header was refused, or null when none was.</param>
public sealed record HeaderAppendResult(int Accepted, HeaderRejection? Rejected);

/// <summary>
/// The wallet's own copy of the header chain from the built-in checkpoint to the tip (specs/011-p2p-spv-gateway, T044). A header is
/// kept only after it passes <see cref="HeaderChain"/>: it links, uses an allowed algorithm, carries the difficulty DigiByte's rule
/// requires, moves time forward, and (when a hasher is supplied) meets its proof-of-work target. Merkle proofs from the gateway are
/// checked against these headers, never against ones the gateway just supplied.
///
/// The headers are public data. They are kept in the wallet's key/value storage in chunks of <c>chunkSize</c> records, under keys that
/// start with <c>gwhdr:</c>, apart from the encrypted seed store. On load every stored header is re-checked for linkage, algorithm,
/// difficulty and time (not proof of work, which would take minutes); data that fails is discarded and the store starts again.
/// </summary>
public sealed class GatewayHeaderStore
{
    private readonly ISecureStorage _storage;
    private readonly Checkpoint _checkpoint;
    private readonly string _prefix;
    private readonly IPowHasher? _hasher;
    private readonly DigiByteConsensus _consensus;
    private readonly int _chunkSize;
    private HeaderChain _chain;
    private int _persistedCount;

    public GatewayHeaderStore(ISecureStorage storage, Checkpoint checkpoint, string network, IPowHasher? hasher = null,
        DigiByteConsensus? consensus = null, int chunkSize = 2000)
    {
        if (chunkSize < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        _storage = storage;
        _checkpoint = checkpoint;
        _prefix = $"gwhdr:{network}:";
        _hasher = hasher;
        _consensus = consensus ?? DigiByteConsensus.Mainnet;
        _chunkSize = chunkSize;
        _chain = new HeaderChain(checkpoint, _consensus, hasher);
    }

    public int TipHeight => _chain.TipHeight;

    public uint256 TipHash => _chain.TipHash;

    /// <summary>The work added since the checkpoint, counted the way DigiByte Core counts it.</summary>
    public BigInteger WorkSinceCheckpoint => _chain.WorkSinceCheckpoint;

    /// <summary>The number of headers kept after the checkpoint.</summary>
    public int Count => _chain.TipHeight - _checkpoint.Height;

    private string MetaKey => _prefix + "meta";

    private string ChunkKey(int index) => $"{_prefix}chunk:{index}";

    /// <summary>Loads what was saved, or starts from the checkpoint when nothing usable was.</summary>
    public async Task InitializeAsync()
    {
        _chain = new HeaderChain(_checkpoint, _consensus, _hasher);
        _persistedCount = 0;

        var loaded = await TryLoadAsync();
        if (!loaded)
        {
            await ClearStoredAsync();
            _chain = new HeaderChain(_checkpoint, _consensus, _hasher);
            _persistedCount = 0;
        }
    }

    private async Task<bool> TryLoadAsync()
    {
        var metaText = await _storage.GetAsync(MetaKey);
        if (metaText is null)
            return true; // nothing saved yet

        Meta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<Meta>(metaText);
        }
        catch (JsonException)
        {
            return false;
        }
        if (meta is null || meta.CheckpointHeight != _checkpoint.Height || meta.CheckpointHash != _checkpoint.Hash.ToString() || meta.Count < 0)
            return false;

        // Re-check as the chain is rebuilt, without the proof-of-work step.
        var rebuilt = new HeaderChain(_checkpoint, _consensus, hasher: null);
        var remaining = meta.Count;
        for (var chunk = 0; remaining > 0; chunk++)
        {
            var text = await _storage.GetAsync(ChunkKey(chunk));
            if (text is null)
                return false;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return false;
            }
            var inChunk = Math.Min(_chunkSize, remaining);
            if (bytes.Length != inChunk * BlockHeader80.Size)
                return false;
            for (var i = 0; i < inChunk; i++)
            {
                BlockHeader80 header;
                try
                {
                    header = BlockHeader80.Parse(bytes.AsSpan(i * BlockHeader80.Size, BlockHeader80.Size));
                }
                catch (FormatException)
                {
                    return false;
                }
                if (!rebuilt.TryAppend(header).Accepted)
                    return false;
            }
            remaining -= inChunk;
        }

        _chain = rebuilt;
        _persistedCount = meta.Count;
        return true;
    }

    /// <summary>
    /// Checks the headers in order, keeps the ones that pass, and saves the result. Stops at the first header it refuses; the ones before
    /// it are kept.
    /// </summary>
    public async Task<HeaderAppendResult> AppendRangeAsync(IEnumerable<BlockHeader80> headers)
    {
        var accepted = 0;
        HeaderRejection? rejected = null;
        foreach (var header in headers)
        {
            var result = _chain.TryAppend(header);
            if (!result.Accepted)
            {
                rejected = result.Reason;
                break;
            }
            accepted++;
        }
        if (accepted > 0)
            await PersistAsync();
        return new HeaderAppendResult(accepted, rejected);
    }

    /// <summary>Takes headers back off the tip until the chain ends at <paramref name="height"/>, and saves the shorter chain.</summary>
    public async Task RewindAsync(int height)
    {
        _chain.TruncateTo(height);
        await PersistAsync();
    }

    public BlockHeader80? HeaderAt(int height)
    {
        var index = height - _checkpoint.FirstHeight;
        var all = _chain.Headers;
        return index >= 0 && index < all.Count ? all[index] : null;
    }

    public IReadOnlyList<BlockHeader80> HeadersFrom(int height, int count)
    {
        var all = _chain.Headers;
        var start = height - _checkpoint.FirstHeight;
        if (start < 0 || start >= all.Count || count < 1)
            return [];
        return all.Skip(start).Take(count).ToList();
    }

    private async Task PersistAsync()
    {
        var appended = _chain.Headers.Skip(_checkpoint.Headers.Count).ToList();
        var count = appended.Count;
        var chunkCount = (count + _chunkSize - 1) / _chunkSize;

        // Rewrite from the first chunk that changed: the one that was partly full, or the first one a rewind cut into.
        var firstDirty = Math.Min(_persistedCount, count) / _chunkSize;
        for (var chunk = firstDirty; chunk < chunkCount; chunk++)
        {
            var slice = appended.Skip(chunk * _chunkSize).Take(_chunkSize).ToList();
            var bytes = new byte[slice.Count * BlockHeader80.Size];
            for (var i = 0; i < slice.Count; i++)
                slice[i].ToBytes().CopyTo(bytes, i * BlockHeader80.Size);
            await _storage.SetAsync(ChunkKey(chunk), Convert.ToBase64String(bytes));
        }

        // Drop chunks a rewind left past the end.
        var oldChunkCount = (_persistedCount + _chunkSize - 1) / _chunkSize;
        for (var chunk = chunkCount; chunk < oldChunkCount; chunk++)
            await _storage.RemoveAsync(ChunkKey(chunk));

        var meta = new Meta(_checkpoint.Height, _checkpoint.Hash.ToString(), count);
        await _storage.SetAsync(MetaKey, JsonSerializer.Serialize(meta));
        _persistedCount = count;
    }

    private async Task ClearStoredAsync()
    {
        foreach (var key in await _storage.GetKeysWithPrefixAsync(_prefix))
            await _storage.RemoveAsync(key);
    }

    private sealed record Meta(int CheckpointHeight, string CheckpointHash, int Count);
}
