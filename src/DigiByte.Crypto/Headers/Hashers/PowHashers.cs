using NBitcoin;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>Combines the per-algorithm hashers that exist so far into one <see cref="IPowHasher"/>.</summary>
public sealed class PowHashers : IPowHasher
{
    private readonly IReadOnlyList<IPowHasher> _hashers;

    public PowHashers(IEnumerable<IPowHasher> hashers) => _hashers = hashers.ToList();

    /// <summary>Every hasher that has been ported and checked against real blocks.</summary>
    public static PowHashers Available { get; } = new([new Sha256dHasher(), new ScryptHasher(), new SkeinHasher(), new QubitHasher(), new OdocryptHasher()]);

    private static readonly PowAlgorithm[] ActiveAlgorithms =
        [PowAlgorithm.Sha256d, PowAlgorithm.Scrypt, PowAlgorithm.Skein, PowAlgorithm.Qubit, PowAlgorithm.Odo];

    /// <summary>
    /// True only when every algorithm that can mine today has a hasher. Until then proof of work cannot be fully checked and callers
    /// must not describe data as verified (spec FR-013).
    /// </summary>
    public static bool AllActiveAlgorithmsSupported => ActiveAlgorithms.All(Available.Supports);

    public bool Supports(PowAlgorithm algorithm) => _hashers.Any(h => h.Supports(algorithm));

    public uint256 Hash(BlockHeader80 header)
    {
        var hasher = _hashers.FirstOrDefault(h => h.Supports(header.Algorithm))
                     ?? throw new NotSupportedException($"No hasher for {header.Algorithm} blocks yet.");
        return hasher.Hash(header);
    }
}
